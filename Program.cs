using System.Windows.Forms;
using VibeAlarm.UI.Forms;
using VibeAlarm.UI.Theming;

namespace VibeAlarm
{
    internal static class Program
    {
        /// <summary>
        ///  The main entry point for the application.
        /// </summary>
        [STAThread]
        static void Main()
        {
            // To customize application configuration such as set high DPI settings or default font,
            // see https://aka.ms/applicationconfiguration.
            ApplicationConfiguration.Initialize();

            // Register the embedded typefaces with GDI+ before any control is constructed —
            // a Font created against an unregistered family silently falls back to a default.
            FontRegistry.Initialize();

            Application.Run(new MainForm());
        }
    }
}
