using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Windows.Forms;
using CommunityToolkit.WinUI.Notifications;
using Windows.UI.Notifications;
using VibeAlarm.Models;

namespace VibeAlarm.Services
{
    /// <summary>
    /// The WinRT side of OS-scheduled alarm toasts: applies a
    /// <see cref="ToastSchedulePlanner"/> diff to Windows (schedule / remove via
    /// <see cref="ToastNotificationManagerCompat"/>, which owns the AUMID + Start Menu
    /// shortcut for this unpackaged app), and routes toast activations (Snooze/Dismiss
    /// clicked while the app is closed) back into tasks.json so the alarm re-arms 9
    /// minutes later.
    ///
    /// Toast identity: Group = task Id, Tag = occurrence (fire minute) — see
    /// <see cref="ToastKey"/>. The in-app alarm removes the task's whole group the
    /// instant it fires (the overlay becomes the answer surface).
    /// </summary>
    public sealed class ToastSchedulerService
    {
        /// <summary>Serializes activation-driven snoozes against the host's initial task
        /// load — a background activation can land while the process is still starting.</summary>
        internal static readonly object TaskFileLock = new();

        private readonly Func<IEnumerable<TaskItem>> tasksProvider;
        private readonly Func<DateTime> now;

        public ToastSchedulerService(Func<IEnumerable<TaskItem>> tasksProvider, Func<DateTime> now)
        {
            this.tasksProvider = tasksProvider;
            this.now = now;
        }

        /// <summary>Brings Windows' scheduled-toast set in line with the task list.</summary>
        public void Reconcile()
        {
            try
            {
                var notifier = ToastNotificationManagerCompat.CreateToastNotifier();
                IReadOnlyList<ScheduledToastNotification> scheduled = notifier.GetScheduledToastNotifications();
                List<ToastKey> current = scheduled
                    .Where(t => !string.IsNullOrEmpty(t.Group) && !string.IsNullOrEmpty(t.Tag))
                    .Select(t => new ToastKey(t.Group, t.Tag))
                    .ToList();

                ScheduledToastPlan plan = ToastSchedulePlanner.Plan(tasksProvider(), now(), current);

                foreach (ToastKey remove in plan.ToRemove)
                {
                    ScheduledToastNotification? toast = scheduled.FirstOrDefault(
                        t => t.Group == remove.Group && t.Tag == remove.Tag);
                    if (toast != null)
                    {
                        notifier.RemoveFromSchedule(toast);
                    }
                }

                foreach (ToastAdd add in plan.ToAdd)
                {
                    ToastContent content = new ToastContentBuilder()
                        .AddText(add.Title)
                        .AddText(add.Body)
                        .AddButton(new ToastButton("Snooze 9 min",
                            $"action=snooze&taskId={add.TaskId}&minutes=9"))
                        .AddButton(new ToastButtonDismiss())
                        .Content;
                    ScheduledToastNotification notification = new(content.GetXml(), new DateTimeOffset(add.FireTime))
                    {
                        Tag = add.Key.Tag,
                        Group = add.Key.Group,
                    };
                    notifier.AddToSchedule(notification);
                }
            }
            catch (Exception ex)
            {
                Log("Reconcile failed: " + ex);
            }
        }

        /// <summary>Removes every scheduled toast for one task — called the instant the
        /// in-app alarm fires, so the OS toast and the React overlay never both appear.</summary>
        public void RemoveGroup(string taskId)
        {
            try
            {
                var notifier = ToastNotificationManagerCompat.CreateToastNotifier();
                foreach (ScheduledToastNotification toast in notifier.GetScheduledToastNotifications()
                             .Where(t => t.Group == taskId))
                {
                    notifier.RemoveFromSchedule(toast);
                }
            }
            catch (Exception ex)
            {
                Log("RemoveGroup failed: " + ex);
            }
        }

        // ---- Background activation (toast clicked while the app is closed) ----

        /// <summary>Installs the toast-activation router. Call once at process start,
        /// before the main form exists — Windows starts the process just to deliver the
        /// activation when a scheduled toast's Snooze is clicked.</summary>
        public static void InstallActivationRouter()
        {
            ToastNotificationManagerCompat.OnActivated += OnToastActivated;
        }

        private static void OnToastActivated(ToastNotificationActivatedEventArgsCompat args)
        {
            Dictionary<string, string> parsed = ParseArguments(args.Argument);
            if (!string.Equals(parsed.GetValueOrDefault("action"), "snooze", StringComparison.OrdinalIgnoreCase))
            {
                return; // Dismiss needs no work — the toast is gone either way.
            }

            string? taskId = parsed.GetValueOrDefault("taskId");
            if (string.IsNullOrEmpty(taskId) || !int.TryParse(parsed.GetValueOrDefault("minutes"), out int minutes))
            {
                return;
            }

            // Apply the snooze on disk (works whether or not a window is live yet).
            DateTime? newFireTime = null;
            lock (TaskFileLock)
            {
                List<TaskItem> tasks = TaskStorageService.Load();
                TaskItem? task = tasks.FirstOrDefault(t => t.Id == taskId);
                if (task != null && TrySnoozeOnDisk(task, TimeSpan.FromMinutes(minutes)))
                {
                    TaskStorageService.Save(tasks);
                    newFireTime = AlarmEngine.GetScheduledDateTime(task);
                }
            }

            if (newFireTime != null)
            {
                // Re-arm: the new occurrence gets its toast, the answered one is removed.
                new ToastSchedulerService(() => TaskStorageService.Load(), () => DateTime.Now).Reconcile();
            }

            // If the app is already running, bring its in-memory list onto the new truth
            // (the file changed behind its back). During startup the ctor loads under the
            // same lock, so nothing diverges.
            MainForm? form = Application.OpenForms.OfType<MainForm>().FirstOrDefault();
            form?.BeginInvoke(new Action(form.ReloadTasksFromDisk));
        }

        /// <summary>Minimal disk-form of SchedulerService.Snooze: push the fire time out,
        /// return the task to a re-fireable state.</summary>
        private static bool TrySnoozeOnDisk(TaskItem task, TimeSpan span)
        {
            DateTime? scheduled = AlarmEngine.GetScheduledDateTime(task);
            if (scheduled == null)
            {
                return false;
            }

            DateTime pushed = scheduled.Value.Add(span);
            task.ScheduledDate = pushed.ToString("yyyy-MM-dd");
            task.RemindTime = pushed.ToString("HH:mm");
            task.SetState(TaskState.Scheduled);
            return true;
        }

        private static Dictionary<string, string> ParseArguments(string argument)
        {
            var parsed = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            foreach (string pair in argument.Split('&', StringSplitOptions.RemoveEmptyEntries))
            {
                int split = pair.IndexOf('=');
                if (split > 0)
                {
                    parsed[pair[..split]] = pair[(split + 1)..];
                }
            }
            return parsed;
        }

        /// <summary>Best-effort diagnostics — toast failures never break firing, but
        /// they must not be silent either.</summary>
        private static void Log(string message)
        {
            try
            {
                File.AppendAllText(
                    Path.Combine(AppPaths.DataRoot, "toast-diagnostics.log"),
                    $"[{DateTime.Now:yyyy-MM-dd HH:mm:ss}] {message}{Environment.NewLine}");
            }
            catch (Exception)
            {
                // Logging must never throw.
            }
        }
    }
}
