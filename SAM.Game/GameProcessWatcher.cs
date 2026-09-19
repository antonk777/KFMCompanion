using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Threading;
using System.Threading.Tasks;

namespace SAM.Game
{
    internal static class GameProcessWatcher
    {
        public static string NormalizeProcessName(string name)
        {
            if (string.IsNullOrWhiteSpace(name) == true)
            {
                return name;
            }

            name = name.Trim();
            name = Path.GetFileName(name);
            if (name.EndsWith(".exe", StringComparison.OrdinalIgnoreCase) == true)
            {
                name = name.Substring(0, name.Length - 4);
            }

            return name;
        }

        public static HashSet<int> GetProcessIds(string processName)
        {
            var ids = new HashSet<int>();
            foreach (var process in GetProcesses(processName))
            {
                try
                {
                    ids.Add(process.Id);
                }
                catch (InvalidOperationException)
                {
                }
                finally
                {
                    process.Dispose();
                }
            }

            return ids;
        }

        public static async Task<Process> WaitForNewProcessAsync(
            string processName,
            HashSet<int> existingIds,
            TimeSpan timeout,
            CancellationToken cancellationToken)
        {
            var deadline = DateTime.UtcNow + timeout;
            while (DateTime.UtcNow < deadline)
            {
                cancellationToken.ThrowIfCancellationRequested();
                foreach (var process in GetProcesses(processName))
                {
                    try
                    {
                        if (existingIds == null || existingIds.Contains(process.Id) == false)
                        {
                            return process;
                        }
                    }
                    catch (InvalidOperationException)
                    {
                        process.Dispose();
                        continue;
                    }

                    process.Dispose();
                }

                await Task.Delay(500, cancellationToken).ConfigureAwait(false);
            }

            throw new TimeoutException("Game process '" + processName + "' did not start.");
        }

        public static async Task WaitForExitAsync(Process process, CancellationToken cancellationToken)
        {
            if (process == null)
            {
                throw new ArgumentNullException(nameof(process));
            }

            while (true)
            {
                cancellationToken.ThrowIfCancellationRequested();
                try
                {
                    if (process.HasExited == true)
                    {
                        return;
                    }
                }
                catch (InvalidOperationException)
                {
                    return;
                }

                await Task.Delay(500, cancellationToken).ConfigureAwait(false);
            }
        }

        public static async Task WaitUntilNoneRemainAsync(
            string processName,
            TimeSpan grace,
            CancellationToken cancellationToken)
        {
            var graceDeadline = DateTime.UtcNow + grace;
            while (true)
            {
                cancellationToken.ThrowIfCancellationRequested();
                if (GetProcessIds(processName).Count == 0)
                {
                    if (DateTime.UtcNow >= graceDeadline)
                    {
                        return;
                    }
                }
                else
                {
                    graceDeadline = DateTime.UtcNow + grace;
                }

                await Task.Delay(500, cancellationToken).ConfigureAwait(false);
            }
        }

        public static void LaunchViaSteam(long appId)
        {
            var start = new ProcessStartInfo
            {
                FileName = "steam://run/" + appId.ToString(System.Globalization.CultureInfo.InvariantCulture),
                UseShellExecute = true,
            };
            Process.Start(start)?.Dispose();
        }

        private static Process[] GetProcesses(string processName)
        {
            try
            {
                return Process.GetProcessesByName(processName);
            }
            catch (InvalidOperationException)
            {
                return Array.Empty<Process>();
            }
        }
    }
}
