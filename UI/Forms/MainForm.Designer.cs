namespace VibeAlarm.UI.Forms
{
    partial class MainForm
    {
        private System.ComponentModel.IContainer components = null;

        protected override void Dispose(bool disposing)
        {
            if (disposing && (components != null))
            {
                components.Dispose();
            }
            base.Dispose(disposing);
        }

        private void InitializeComponent()
        {
            SuspendLayout();
            // 
            // MainForm
            // 
            AutoScaleDimensions = new SizeF(7F, 15F);
            AutoScaleMode = AutoScaleMode.Font;
            ClientSize = new Size(463, 670);
            Name = "MainForm";
            StartPosition = FormStartPosition.CenterScreen;
            Text = "Workspace Logging Engine";
            Load += MainForm_Load;
            ResumeLayout(false);
        }
    }
}
