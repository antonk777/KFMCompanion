using System;
using System.Drawing;
using System.IO;
using System.Reflection;
using System.Windows.Forms;

namespace SAM.Game
{
    internal static class AppIcon
    {
        private static MemoryStream _iconData;
        private static Icon _source;

        private static Icon Source()
        {
            if (_source != null)
            {
                return _source;
            }

            var assembly = Assembly.GetExecutingAssembly();
            using (var stream = OpenIconStream(assembly))
            {
                if (stream != null)
                {
                    _iconData = new MemoryStream();
                    stream.CopyTo(_iconData);
                    _iconData.Position = 0;
                    _source = new Icon(_iconData);
                    return _source;
                }
            }

            var fromExe = Icon.ExtractAssociatedIcon(Application.ExecutablePath);
            _source = fromExe ?? (Icon)SystemIcons.Application.Clone();
            return _source;
        }

        public static Icon Load()
        {
            return (Icon)Source().Clone();
        }

        private static Stream OpenIconStream(Assembly assembly)
        {
            foreach (var name in assembly.GetManifestResourceNames())
            {
                if (name.EndsWith("kf.ico", StringComparison.OrdinalIgnoreCase) == true)
                {
                    return assembly.GetManifestResourceStream(name);
                }
            }

            return null;
        }
    }
}
