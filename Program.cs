using System.Windows.Forms;
using VibeAlarm.Services;

namespace VibeAlarm
{
    internal static class Program
    {
        /// <summary>
        /// The main entry point. The WinForms process is the host/shell: it owns the
        /// window, tray, and business logic; all presentation lives in the React SPA
        /// hosted by MainForm's WebView2 control.
        /// </summary>
        [STAThread]
        static void Main()
        {
            // To customize application configuration such as set high DPI settings or default font,
            // see https://aka.ms/applicationconfiguration.
            ApplicationConfiguration.Initialize();

            // Before anything else: a Snooze clicked on an OS toast while the app was
            // closed may have started this very process — route it before the window
            // exists so the snooze lands deterministically.
            ToastSchedulerService.InstallActivationRouter();

            Application.Run(new MainForm());
        }
    }
}
