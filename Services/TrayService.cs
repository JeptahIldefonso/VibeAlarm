using System;
using System.Drawing;
using System.Windows.Forms;

namespace VibeAlarm.Services
{
    /// <summary>
    /// Owns the system-tray NotifyIcon and its context menu. Provides hide-to-tray / restore
    /// behavior, a toast notification surface for alarms that fire while the window is hidden,
    /// and exposes an event raised when the user picks "Open" (so the UI can restore + resume).
    /// </summary>
    public sealed class TrayService : IDisposable
    {
        private readonly NotifyIcon notifyIcon;
        private readonly ContextMenuStrip menu;
        private readonly ToolStripMenuItem btnOpen;
        private readonly ToolStripMenuItem btnExit;
        private ToolStripMenuItem? btnDismiss;
        private ToolStripMenuItem? btnSnooze;
        private ToolStripSeparator? sepAlarm;

        /// <summary>Raised when the user chooses "Open" (tray icon double-click or menu item).</summary>
        public event Action? OpenRequested;

        /// <summary>Raised when the user chooses "Quit" from the tray menu.</summary>
        public event Action? ExitRequested;

        /// <summary>True while the app window is hidden to the tray (alarms can still fire).</summary>
        public bool IsHidden { get; private set; }

        public TrayService(Icon? icon, string tooltip = "VibeAlarm")
        {
            btnOpen = new ToolStripMenuItem("Open", null, (_, _) => OnOpen());
            btnExit = new ToolStripMenuItem("Quit", null, (_, _) => OnExit());

            menu = new ContextMenuStrip();
            menu.Items.Add(btnOpen);
            menu.Items.Add(new ToolStripSeparator());
            menu.Items.Add(btnExit);

            notifyIcon = new NotifyIcon
            {
                Icon = icon ?? SystemIcons.Application,
                Text = TruncateTooltip(tooltip),
                ContextMenuStrip = menu,
                Visible = false
            };
            notifyIcon.DoubleClick += (_, _) => OnOpen();
        }

        /// <summary>Updates the tray icon tooltip with the next upcoming alarm.</summary>
        public void UpdateTooltip(string? nextUpText)
        {
            string text = string.IsNullOrWhiteSpace(nextUpText)
                ? "VibeAlarm\nNo upcoming alarms"
                : $"VibeAlarm\nNext: {nextUpText}";
            notifyIcon.Text = TruncateTooltip(text);
        }

        private static string TruncateTooltip(string text)
        {
            // Windows NotifyIcon Text has a 63-character limit on older APIs / 127 on Win10+;
            // clamp to 63 to guarantee it never throws ArgumentException.
            return text.Length > 63 ? text.Substring(0, 60) + "..." : text;
        }

        /// <summary>
        /// Adds prominent Dismiss and Snooze items to the top of the tray menu while an alarm is ringing.
        /// Ensures alarms can always be silenced even if the window/WebView2 is closed or loading.
        /// </summary>
        public void SetAlarmRinging(string title, Action onDismiss, Action onSnooze)
        {
            ClearAlarmRinging();
            string label = $"Dismiss: {title}";
            if (label.Length > 30) label = label.Substring(0, 27) + "...";

            btnDismiss = new ToolStripMenuItem(label, null, (_, _) =>
            {
                ClearAlarmRinging();
                onDismiss();
            })
            {
                Font = new Font(menu.Font, FontStyle.Bold)
            };

            btnSnooze = new ToolStripMenuItem("Snooze 9 min", null, (_, _) =>
            {
                ClearAlarmRinging();
                onSnooze();
            });

            sepAlarm = new ToolStripSeparator();

            menu.Items.Insert(0, btnDismiss);
            menu.Items.Insert(1, btnSnooze);
            menu.Items.Insert(2, sepAlarm);
        }

        /// <summary>Removes any alarm ringing actions from the tray menu.</summary>
        public void ClearAlarmRinging()
        {
            if (btnDismiss != null)
            {
                menu.Items.Remove(btnDismiss);
                btnDismiss.Dispose();
                btnDismiss = null;
            }
            if (btnSnooze != null)
            {
                menu.Items.Remove(btnSnooze);
                btnSnooze.Dispose();
                btnSnooze = null;
            }
            if (sepAlarm != null)
            {
                menu.Items.Remove(sepAlarm);
                sepAlarm.Dispose();
                sepAlarm = null;
            }
        }

        public void ShowTray()
        {
            notifyIcon.Visible = true;
        }

        public void HideTray()
        {
            notifyIcon.Visible = false;
        }

        /// <summary>Records that the main window has been hidden to the tray.</summary>
        public void MarkHidden()
        {
            IsHidden = true;
            ShowTray();
            btnOpen.Enabled = true;
        }

        /// <summary>Records that the main window is visible; keeps the tray icon available.</summary>
        public void MarkVisible()
        {
            IsHidden = false;
            ShowTray();
        }

        /// <summary>Shows a balloon toast. Safe to call while the window is hidden.</summary>
        public void ShowToast(string title, string message, ToolTipIcon icon = ToolTipIcon.Info)
        {
            if (!notifyIcon.Visible)
            {
                ShowTray();
            }
            notifyIcon.ShowBalloonTip(4000, title, message, icon);
        }

        private void OnOpen()
        {
            OpenRequested?.Invoke();
        }

        private void OnExit()
        {
            ExitRequested?.Invoke();
        }

        public void Dispose()
        {
            ClearAlarmRinging();
            notifyIcon.Visible = false;
            notifyIcon.Dispose();
            btnOpen.Dispose();
            btnExit.Dispose();
            menu.Dispose();
        }
    }
}