using System;
using System.Drawing;
using System.Windows.Forms;

namespace SAM.Game
{
    internal sealed class LogWindow : Form
    {
        private const string HintText =
            "You don't have to launch the game from this window. Start Killing Floor yourself if you want. " +
            "This companion must be running to save stats.\r\n\r\n" +
            "Closing this window does not quit. The companion stays in the tray — right-click the tray icon and choose Exit to quit.";

        private readonly TextBox _text;
        private readonly Button _launchButton;
        private readonly Panel _hintPanel;
        private readonly Label _hintLabel;
        private bool _allowClose;

        public event EventHandler LaunchClicked;

        public LogWindow()
        {
            this.Text = "KFM Companion";
            this.Width = 1100;
            this.Height = 580;
            this.MinimumSize = new Size(860, 420);
            this.StartPosition = FormStartPosition.CenterScreen;
            this.MinimizeBox = true;
            this.MaximizeBox = true;
            this.ShowInTaskbar = true;
            this.ShowIcon = true;
            this.Icon = AppIcon.Load();

            this._hintPanel = new Panel
            {
                Dock = DockStyle.Top,
                BackColor = SystemColors.Info,
            };

            this._hintLabel = new Label
            {
                AutoSize = false,
                Text = HintText,
                ForeColor = SystemColors.InfoText,
                BackColor = SystemColors.Info,
                Location = new Point(12, 12),
            };

            this._hintPanel.Controls.Add(this._hintLabel);

            this._text = new TextBox
            {
                Multiline = true,
                ReadOnly = true,
                ScrollBars = ScrollBars.Vertical,
                Dock = DockStyle.Fill,
                WordWrap = true,
                Font = new Font("Consolas", 8f),
                BackColor = Color.White,
                HideSelection = false,
            };

            this._launchButton = new Button
            {
                Text = "Launch game",
                AutoSize = true,
                AutoSizeMode = AutoSizeMode.GrowAndShrink,
                Padding = new Padding(12, 6, 16, 6),
                MinimumSize = new Size(160, 36),
                Left = 12,
                Top = 12,
                Anchor = AnchorStyles.Left | AnchorStyles.Top,
            };
            this._launchButton.Click += this.OnLaunchClicked;

            var bar = new Panel
            {
                Dock = DockStyle.Bottom,
                Height = 64,
            };
            bar.Controls.Add(this._launchButton);

            this.Controls.Add(this._text);
            this.Controls.Add(this._hintPanel);
            this.Controls.Add(bar);

            var snapshot = AppLog.Snapshot();
            if (string.IsNullOrEmpty(snapshot) == false)
            {
                this._text.Text = snapshot + Environment.NewLine;
                this._text.SelectionStart = this._text.TextLength;
                this._text.ScrollToCaret();
            }

            AppLog.LineAdded += this.OnLineAdded;
            this.Resize += this.OnWindowResize;
            this.LayoutHint();
        }

        public void AllowClose()
        {
            this._allowClose = true;
        }

        public void SetLaunchEnabled(bool enabled)
        {
            if (this.IsDisposed == true)
            {
                return;
            }

            if (this.InvokeRequired == true)
            {
                try
                {
                    this.BeginInvoke(new Action<bool>(this.SetLaunchEnabled), enabled);
                }
                catch (ObjectDisposedException)
                {
                }

                return;
            }

            this._launchButton.Enabled = enabled;
        }

        private void OnWindowResize(object sender, EventArgs e)
        {
            this.LayoutHint();
        }

        private void LayoutHint()
        {
            var textWidth = this.ClientSize.Width - 24;
            if (textWidth < 200)
            {
                textWidth = 200;
            }

            this._hintLabel.Width = textWidth;
            var size = TextRenderer.MeasureText(
                this._hintLabel.Text,
                this._hintLabel.Font,
                new Size(textWidth, int.MaxValue),
                TextFormatFlags.WordBreak | TextFormatFlags.TextBoxControl);
            this._hintLabel.Height = size.Height + 4;
            this._hintPanel.Height = this._hintLabel.Bottom + 12;
        }

        private void OnLaunchClicked(object sender, EventArgs e)
        {
            this.LaunchClicked?.Invoke(this, EventArgs.Empty);
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
                this.Resize -= this.OnWindowResize;
                AppLog.LineAdded -= this.OnLineAdded;
            }

            base.Dispose(disposing);
        }
    }
}
