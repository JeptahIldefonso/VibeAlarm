using System.Threading;
using System.Windows.Forms;
using VibeAlarm.Services;

namespace VibeAlarm
{
    internal static class Program
    {
        // Held for the process lifetime so a second VibeAlarm (e.g. the installed
        // copy launched while a dev build is running, or vice versa) sees the mutex
        // as taken. Static so it is never GC-released mid-run. Local\ = per-session,
        // so two Windows user accounts can still each run their own instance.
        private static Mutex? singleInstanceMutex;
        private static EventWaitHandle? showEvent;
        private static RegisteredWaitHandle? registeredWait;

        /// <summary>
        /// The main entry point. The WinForms process is the host/shell: it owns the
        /// window, tray, and business logic; all presentation lives in the React SPA
        /// hosted by MainForm's WebView2 control.
        /// </summary>
        [STAThread]
        static void Main(string[] args)
        {
            bool startInTray = args.Any(a => string.Equals(a, "--tray", StringComparison.OrdinalIgnoreCase) ||
                                             string.Equals(a, "-tray", StringComparison.OrdinalIgnoreCase) ||
                                             string.Equals(a, "--minimized", StringComparison.OrdinalIgnoreCase));

            // Single instance: the dev build and the installed exe share the same
            // tasks.json (AppPaths.DataRoot) and the same toast AUMID — two live
            // schedulers would double-fire alarms and clobber each other's writes.
            // Whichever copy starts second exits here, before touching any state.
            singleInstanceMutex = new Mutex(initiallyOwned: true, @"Local\VibeAlarm.SingleInstance", out bool createdNew);
            if (!createdNew)
            {
                // Second instance launched: if user opened app from Start/Desktop (not --tray),
                // signal the running first instance to bring its window to the foreground.
                if (!startInTray)
                {
                    try
                    {
                        if (EventWaitHandle.TryOpenExisting(@"Local\VibeAlarm.ShowEvent", out var existingHandle))
                        {
                            existingHandle.Set();
                            existingHandle.Dispose();
                        }
                    }
                    catch { }
                }
                return;
            }

            // Create wake signal for subsequent launches
            showEvent = new EventWaitHandle(false, EventResetMode.AutoReset, @"Local\VibeAlarm.ShowEvent");

            // To customize application configuration such as set high DPI settings or default font,
            // see https://aka.ms/applicationconfiguration.
            ApplicationConfiguration.Initialize();

            // Before anything else: a Snooze clicked on an OS toast while the app was
            // closed may have started this very process — route it before the window
            // exists so the snooze lands deterministically.
            ToastSchedulerService.InstallActivationRouter();

            var mainForm = new MainForm(startInTray);

            registeredWait = ThreadPool.RegisterWaitForSingleObject(
                showEvent,
                (_, _) =>
                {
                    try
                    {
                        if (!mainForm.IsDisposed && mainForm.IsHandleCreated)
                        {
                            mainForm.BeginInvoke(new Action(mainForm.RestoreFromTray));
                        }
                    }
                    catch { }
                },
                null,
                -1,
                false);

            try
            {
                Application.Run(mainForm);
            }
            finally
            {
                registeredWait?.Unregister(null);
                showEvent?.Dispose();
                singleInstanceMutex?.Dispose();
            }
        }
    }
}
