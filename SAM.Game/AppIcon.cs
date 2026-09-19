using System.Drawing;
using System.Windows.Forms;

namespace SAM.Game
{
    internal static class AppIcon
    {
        public static Icon Load()
        {
            var fromExe = Icon.ExtractAssociatedIcon(Application.ExecutablePath);
            if (fromExe != null)
            {
                return fromExe;
            }

            return (Icon)SystemIcons.Application.Clone();
        }
    }
}
