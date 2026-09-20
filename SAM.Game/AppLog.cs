using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using System.Threading;

namespace SAM.Game
{
    internal static class AppLog
    {
        private const int MaxLines = 2000;
        private static readonly object Lock = new();
        private static readonly List<string> Lines = new();
        private static readonly Mutex FileMutex = new(false, @"Local\KFM-Companion-Log");

        public static readonly string FilePath = Path.Combine(
            AppDomain.CurrentDomain.BaseDirectory,
            "KFM Companion.log");

        public static event Action<string> LineAdded;

        public static void Write(string message)
        {
            var now = DateTime.Now;
            var text = message ?? string.Empty;
            var displayLine = now.ToString("HH:mm:ss") + "  " + text;
            var fileLine = now.ToString("yyyy-MM-dd HH:mm:ss") + "  " + text;
            lock (Lock)
            {
                Lines.Add(displayLine);
                if (Lines.Count > MaxLines)
                {
                    Lines.RemoveRange(0, Lines.Count - MaxLines);
                }
            }

            WriteToFile(fileLine);
            LineAdded?.Invoke(displayLine);
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
            var acquired = false;
            try
            {
                try
                {
                    acquired = FileMutex.WaitOne();
                }
                catch (AbandonedMutexException)
                {
                    acquired = true;
                }

                File.AppendAllText(FilePath, line + Environment.NewLine, new UTF8Encoding(false));
            }
            catch (IOException)
            {
            }
            catch (UnauthorizedAccessException)
            {
            }
            finally
            {
                if (acquired == true)
                {
                    try
                    {
                        FileMutex.ReleaseMutex();
                    }
                    catch (ApplicationException)
                    {
                    }
                }
            }
        }
    }
}
