using System;
using System.Windows.Forms;
using SAM.API;

namespace SAM.Game
{
    internal static class Program
    {
        [STAThread]
        public static void Main()
        {
            Application.EnableVisualStyles();
            Application.SetCompatibleTextRenderingDefault(false);

            if (Steam.GetInstallPath() == Application.StartupPath)
            {
                MessageBox.Show(
                    "This tool declines to being run from the Steam directory.",
                    "KFM Launcher",
                    MessageBoxButtons.OK,
                    MessageBoxIcon.Error);
                return;
            }

            AppLog.Write("Log file: " + AppLog.FilePath);
            Application.Run(new TrayApplicationContext());
        }
    }
}
