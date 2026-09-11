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
            // MainFormwwwwwwwwwwwww
            // 
            AutoScaleDimensions = new SizeF(7F, 15F);
            AutoScaleMode = AutoScaleMode.Font;
            // §25/§26: default 1280×760, never smaller than 1000×650 (runtime MinimumSize is
            // set in BuildDesktopInterface — kept together with the layout code).
            ClientSize = new Size(1280, 760);
            MinimumSize = new Size(1000, 650);
            Name = "MainForm";
            StartPosition = FormStartPosition.CenterScreen;
            Text = "VibeAlarm";
            Load += MainForm_Load;
            ResumeLayout(false);
        }
    }
}
