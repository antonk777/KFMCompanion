using System.Drawing;
using System.Windows.Forms;

namespace SAM.Game
{
    internal sealed class FirstRunAgreementForm : Form
    {
        private const string AgreementText =
            "I understand by modifying the values of stats, I may screw things up " +
            "and can't blame anyone but myself.\r\n\r\n" +
            "Use of this application is at your own risk.";

        public FirstRunAgreementForm()
        {
            this.Text = "KFM Companion";
            this.FormBorderStyle = FormBorderStyle.FixedDialog;
            this.StartPosition = FormStartPosition.CenterScreen;
            this.MinimizeBox = false;
            this.MaximizeBox = false;
            this.ShowInTaskbar = true;
            this.ClientSize = new Size(520, 240);
            this.Icon = AppIcon.Load();

            var buttons = new FlowLayoutPanel
            {
                Dock = DockStyle.Bottom,
                FlowDirection = FlowDirection.RightToLeft,
                Height = 64,
                Padding = new Padding(0, 12, 12, 12),
                WrapContents = false,
            };

            var decline = new Button
            {
                Text = "Decline",
                DialogResult = DialogResult.Cancel,
                AutoSize = false,
                Width = 140,
                Height = 40,
            };

            var accept = new Button
            {
                Text = "Accept",
                DialogResult = DialogResult.OK,
                AutoSize = false,
                Width = 140,
                Height = 40,
            };

            buttons.Controls.Add(decline);
            buttons.Controls.Add(accept);

            var message = new Label
            {
                AutoSize = false,
                Dock = DockStyle.Fill,
                Text = AgreementText,
                Padding = new Padding(16, 16, 16, 8),
            };

            this.Controls.Add(message);
            this.Controls.Add(buttons);
            this.AcceptButton = accept;
            this.CancelButton = decline;
        }
    }
}
