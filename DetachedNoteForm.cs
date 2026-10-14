using System;
using System.Drawing;
using System.IO;
using System.Windows.Forms;
using Microsoft.Web.WebView2.Core;
using Microsoft.Web.WebView2.WinForms;
using VibeAlarm.Services;

namespace VibeAlarm
{
    /// <summary>
    /// A lightweight standalone window that hosts a single detached note editor
    /// (Windows Notepad mode).
    ///
    /// Shares the same <see cref="IpcBridge"/> as the main window so all edits autosave
    /// directly to NoteStorageService and instantly broadcast to all open windows.
    /// </summary>
    public sealed class DetachedNoteForm : Form
    {
        private const string VirtualHost = "vibealarm.app";

        private readonly IpcBridge bridge;
        private readonly string noteId;
        private readonly WebView2 webView;

        public DetachedNoteForm(IpcBridge bridge, string noteId, string initialTitle)
        {
            this.bridge = bridge;
            this.noteId = noteId;

            Text = string.IsNullOrWhiteSpace(initialTitle) ? "Note - VibeAlarm" : $"{initialTitle} - VibeAlarm";
            StartPosition = FormStartPosition.CenterParent;
            ClientSize = new Size(740, 600);
            MinimumSize = new Size(420, 320);
            Icon = System.Drawing.Icon.ExtractAssociatedIcon(Application.ExecutablePath);
            BackColor = Color.FromArgb(0x18, 0x18, 0x1B); // dark ground matching the app

            webView = new WebView2 { Dock = DockStyle.Fill };
            Controls.Add(webView);
            webView.CoreWebView2InitializationCompleted += OnWebViewInitialized;
            _ = webView.EnsureCoreWebView2Async();
        }

        private void OnWebViewInitialized(object? sender, CoreWebView2InitializationCompletedEventArgs e)
        {
            if (!e.IsSuccess || webView.CoreWebView2 == null)
            {
                Controls.Add(new Label
                {
                    Dock = DockStyle.Fill,
                    ForeColor = Color.White,
                    TextAlign = ContentAlignment.MiddleCenter,
                    Text = $"WebView2 failed to initialize:\n\n{e.InitializationException?.Message ?? "Unknown error"}",
                });
                return;
            }

            CoreWebView2 core = webView.CoreWebView2;

            core.SetVirtualHostNameToFolderMapping(
                VirtualHost,
                Path.Combine(AppContext.BaseDirectory, "ui"),
                CoreWebView2HostResourceAccessKind.Allow);

            bridge.Attach(core);

#if DEBUG
            string devServer = Environment.GetEnvironmentVariable("VIBEALARM_DEV_SERVER") ?? "http://localhost:5173";
            if (devServer.Length > 0 && MainForm.CanReach(devServer))
            {
                core.Navigate($"{devServer}?detached=true&id={Uri.EscapeDataString(noteId)}");
            }
            else
            {
                core.Navigate($"https://{VirtualHost}/index.html?detached=true&id={Uri.EscapeDataString(noteId)}");
            }
#else
            core.Navigate($"https://{VirtualHost}/index.html?detached=true&id={Uri.EscapeDataString(noteId)}");
#endif
        }

        protected override void OnFormClosing(FormClosingEventArgs e)
        {
            base.OnFormClosing(e);
            if (webView.CoreWebView2 != null)
            {
                bridge.Detach(webView.CoreWebView2);
            }
        }
    }
}
