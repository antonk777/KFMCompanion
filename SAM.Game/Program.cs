using System;
using System.Windows.Forms;
using SAM.API;

namespace SAM.Game
{
    internal static class Program
    {
        [STAThread]
        public static void Main(string[] args)
        {
            if (args != null &&
                args.Length >= 2 &&
                string.Equals(args[0], "--flush", StringComparison.OrdinalIgnoreCase) == true)
            {
                try
                {
                    Environment.ExitCode = SteamStatsBridge.RunWorker(args[1]);
                }
                catch (Exception e)
                {
                    AppLog.Write("Flush worker exception: " + e.Message);
                    Environment.ExitCode = 1;
                }

                return;
            }

            Application.EnableVisualStyles();
            Application.SetCompatibleTextRenderingDefault(false);

            try
            {
                using (var instance = SingleInstance.TryAcquire())
                {
                    if (instance == null)
                    {
                        return;
                    }

                    Run(instance);
                }
            }
            catch (Exception e)
            {
                MessageBox.Show(
                    e.ToString(),
                    "KFM Companion",
                    MessageBoxButtons.OK,
                    MessageBoxIcon.Error);
            }
        }

        private static void Run(SingleInstance instance)
        {
            if (Steam.GetInstallPath() == Application.StartupPath)
            {
                MessageBox.Show(
                    "This tool declines to being run from the Steam directory.",
                    "KFM Companion",
                    MessageBoxButtons.OK,
                    MessageBoxIcon.Error);
                return;
            }

            if (UserConsent.HasAccepted() == false)
            {
                using (var agreement = new FirstRunAgreementForm())
                {
                    if (agreement.ShowDialog() != DialogResult.OK)
                    {
                        return;
                    }
                }

                UserConsent.SaveAccepted();
            }

            AppLog.Write("Log file: " + AppLog.FilePath);
            Application.Run(new TrayApplicationContext(instance.Activate));
        }
    }
}
