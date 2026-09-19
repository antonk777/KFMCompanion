using System;
using System.Drawing;
using System.Windows.Forms;

namespace SAM.Game
{
    internal sealed class LogWindow : Form
    {
        private readonly TextBox _text;
        private bool _allowClose;

        public LogWindow()
        {
            this.Text = "KFM Launcher";
            this.Width = 760;
            this.Height = 480;
            this.StartPosition = FormStartPosition.CenterScreen;
            this.MinimizeBox = true;
            this.MaximizeBox = true;
            this.ShowInTaskbar = true;
            this.Icon = AppIcon.Load();

            this._text = new TextBox
            {
                Multiline = true,
                ReadOnly = true,
                ScrollBars = ScrollBars.Both,
                Dock = DockStyle.Fill,
                WordWrap = false,
                Font = new Font("Consolas", 9f),
                BackColor = Color.White,
                HideSelection = false,
            };

            this.Controls.Add(this._text);

            var snapshot = AppLog.Snapshot();
            if (string.IsNullOrEmpty(snapshot) == false)
            {
                this._text.Text = snapshot + Environment.NewLine;
                this._text.SelectionStart = this._text.TextLength;
                this._text.ScrollToCaret();
            }

            AppLog.LineAdded += this.OnLineAdded;
        }

        public void AllowClose()
        {
            this._allowClose = true;
        }

        private void OnLineAdded(string line)
        {
            if (this.IsDisposed == true)
            {
                return;
            }

            if (this.InvokeRequired == true)
            {
                try
                {
                    this.BeginInvoke(new Action<string>(this.AppendLine), line);
                }
                catch (ObjectDisposedException)
                {
                }

                return;
            }

            this.AppendLine(line);
        }

        private void AppendLine(string line)
        {
            this._text.AppendText(line + Environment.NewLine);
        }

        protected override void OnFormClosing(FormClosingEventArgs e)
        {
            if (this._allowClose == false && e.CloseReason == CloseReason.UserClosing)
            {
                e.Cancel = true;
                this.Hide();
                return;
            }

            base.OnFormClosing(e);
        }

        protected override void Dispose(bool disposing)
        {
            if (disposing == true)
            {
                AppLog.LineAdded -= this.OnLineAdded;
            }

            base.Dispose(disposing);
        }
    }
}
