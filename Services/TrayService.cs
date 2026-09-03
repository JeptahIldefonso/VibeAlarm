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
        private readonly ToolStripMenuItem btnOpen;
        private readonly ToolStripMenuItem btnExit;

        /// <summary>Raised when the user chooses "Open" (tray icon double-click or menu item).</summary>
        public event Action? OpenRequested;

        /// <summary>Raised when the user chooses "Exit" from the tray menu.</summary>
        public event Action? ExitRequested;

        /// <summary>True while the app window is hidden to the tray (alarms can still fire).</summary>
        public bool IsHidden { get; private set; }

        public TrayService(Icon? icon, string tooltip = "VibeAlarm")
        {
            btnOpen = new ToolStripMenuItem("Open", null, (_, _) => OnOpen());
            btnExit = new ToolStripMenuItem("Exit", null, (_, _) => OnExit());

            var menu = new ContextMenuStrip();
            menu.Items.Add(btnOpen);
            menu.Items.Add(new ToolStripSeparator());
            menu.Items.Add(btnExit);

            notifyIcon = new NotifyIcon
            {
                Icon = icon ?? SystemIcons.Application,
                Text = tooltip,
                ContextMenuStrip = menu,
                Visible = false
            };
            notifyIcon.DoubleClick += (_, _) => OnOpen();
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
            notifyIcon.Visible = false;
            notifyIcon.Dispose();
            btnOpen.Dispose();
            btnExit.Dispose();
        }
    }
}