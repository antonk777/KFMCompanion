using System;
using System.Collections.Generic;
using System.IO;
using System.Text;

namespace SAM.Game
{
    internal static class AppLog
    {
        private const int MaxLines = 2000;
        private static readonly object Lock = new();
        private static readonly List<string> Lines = new();
        private static StreamWriter _file;

        public static readonly string FilePath = Path.Combine(
            AppDomain.CurrentDomain.BaseDirectory,
            "KFM Launcher.log");

        public static event Action<string> LineAdded;

        public static void Write(string message)
        {
            var line = DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss") + "  " + (message ?? string.Empty);
            lock (Lock)
            {
                Lines.Add(line);
                if (Lines.Count > MaxLines)
                {
                    Lines.RemoveRange(0, Lines.Count - MaxLines);
                }

                WriteToFile(line);
            }

            LineAdded?.Invoke(line);
        }

        public static string Snapshot()
        {
            lock (Lock)
            {
                return string.Join(Environment.NewLine, Lines);
            }
        }

        private static void WriteToFile(string line)
        {
            try
            {
                if (_file == null)
                {
                    _file = new StreamWriter(FilePath, true, new UTF8Encoding(false))
                    {
                        AutoFlush = true,
                    };
                }

                _file.WriteLine(line);
            }
            catch (IOException)
            {
            }
            catch (UnauthorizedAccessException)
            {
            }
        }
    }
}
