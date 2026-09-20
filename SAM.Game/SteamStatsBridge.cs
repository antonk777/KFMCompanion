using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Reflection;
using System.Text;
using System.Threading;
using SAM.API;
using SAM.API.Callbacks;

namespace SAM.Game
{
    internal sealed class FlushResult
    {
        public bool Success;
        public string Message;
        public int AchievementsApplied;
        public int StatsApplied;
    }

    internal static class SteamStatsBridge
    {
        private const int ResultOk = 1;
        private const string FlushArgument = "--flush";
        private static readonly TimeSpan HelperTimeout = TimeSpan.FromSeconds(60);

        public static FlushResult Flush(long appId, SessionBuffer buffer)
        {
            if (buffer.HasWork == false)
            {
                AppLog.Write("Flush skipped: buffer is empty");
                return new FlushResult
                {
                    Success = true,
                    Message = "No stats to send",
                };
            }

            buffer.Snapshot(out var intStats, out var floatStats, out var achievements);
            AppLog.Write(
                "Flush start: " + achievements.Count + " achievement(s), " +
                intStats.Count + " int stat(s), " + floatStats.Count + " float stat(s)");

            var jobPath = Path.Combine(
                Path.GetTempPath(),
                "kfm-flush-" + Guid.NewGuid().ToString("N") + ".job");
            var resultPath = jobPath + ".result";
            try
            {
                WriteJob(jobPath, appId, intStats, floatStats, achievements);
                return RunHelper(jobPath, resultPath);
            }
            finally
            {
                TryDelete(jobPath);
                TryDelete(resultPath);
            }
        }

        public static int RunWorker(string jobPath)
        {
            var resultPath = jobPath + ".result";
            FlushResult result;
            try
            {
                ReadJob(jobPath, out var appId, out var intStats, out var floatStats, out var achievements);
                result = FlushInProcess(appId, intStats, floatStats, achievements);
            }
            catch (Exception e)
            {
                AppLog.Write("Flush worker exception: " + e.Message);
                result = Fail("flush worker exception: " + e.Message);
            }

            WriteResult(resultPath, result);
            return result.Success ? 0 : 1;
        }

        private static FlushResult RunHelper(string jobPath, string resultPath)
        {
            var exe = GetExecutablePath();
            if (string.IsNullOrEmpty(exe) == true || File.Exists(exe) == false)
            {
                return Fail("could not find companion executable");
            }

            AppLog.Write("Starting Steam flush helper so Steam can drop the game session");
            var start = new ProcessStartInfo
            {
                FileName = exe,
                Arguments = FlushArgument + " \"" + jobPath + "\"",
                UseShellExecute = false,
                CreateNoWindow = true,
                WorkingDirectory = Path.GetDirectoryName(exe) ?? AppDomain.CurrentDomain.BaseDirectory,
            };

            using (var process = Process.Start(start))
            {
                if (process == null)
                {
                    return Fail("failed to start Steam flush helper");
                }

                AppLog.Write("Flush helper started (PID " + process.Id + ")");
                if (process.WaitForExit((int)HelperTimeout.TotalMilliseconds) == false)
                {
                    try
                    {
                        process.Kill();
                    }
                    catch (InvalidOperationException)
                    {
                    }

                    return Fail("Steam flush helper timed out");
                }

                AppLog.Write("Flush helper exited (" + process.ExitCode + ")");
            }

            if (File.Exists(resultPath) == false)
            {
                return Fail("Steam flush helper did not report a result");
            }

            return ReadResult(resultPath);
        }

        private static string GetExecutablePath()
        {
            try
            {
                var module = Process.GetCurrentProcess().MainModule;
                if (module != null && string.IsNullOrEmpty(module.FileName) == false)
                {
                    return module.FileName;
                }
            }
            catch (Exception)
            {
            }

            var location = Assembly.GetEntryAssembly()?.Location;
            if (string.IsNullOrEmpty(location) == false)
            {
                return location;
            }

            return Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "KFM Companion.exe");
        }

        private static FlushResult FlushInProcess(
            long appId,
            Dictionary<string, int> intStats,
            Dictionary<string, float> floatStats,
            List<string> achievements)
        {
            try
            {
                using (var client = new Client())
                {
                    AppLog.Write("Initializing Steam API for AppId " + appId);
                    client.Initialize(appId);
                    AppLog.Write("Steam API initialized");

                    AppLog.Write("Requesting current Steam stats");
                    if (WaitForUserStats(client, appId) == false)
                    {
                        AppLog.Write("Failed to retrieve current Steam stats");
                        return Fail("failed to retrieve current Steam stats");
                    }

                    AppLog.Write("Current Steam stats received");

                    var appliedAchievements = 0;
                    var appliedStats = 0;

                    foreach (var id in achievements)
                    {
                        if (client.SteamUserStats.GetAchievement(id, out var unlocked) == false)
                        {
                            AppLog.Write("Skip achievement '" + id + "': could not read current value");
                            continue;
                        }

                        if (unlocked == true)
                        {
                            AppLog.Write("Skip achievement '" + id + "': already unlocked");
                            continue;
                        }

                        if (client.SteamUserStats.SetAchievement(id, true) == false)
                        {
                            AppLog.Write("Failed to set achievement '" + id + "'");
                            return Fail("failed to set achievement '" + id + "'");
                        }

                        AppLog.Write("Unlock achievement '" + id + "'");
                        appliedAchievements++;
                    }

                    foreach (var pair in intStats)
                    {
                        if (client.SteamUserStats.GetStatValue(pair.Key, out int current) == false)
                        {
                            AppLog.Write("Skip int stat '" + pair.Key + "': could not read current value");
                            continue;
                        }

                        if (pair.Value <= current)
                        {
                            AppLog.Write("Skip int stat '" + pair.Key + "': buffered " + pair.Value + " <= Steam " + current);
                            continue;
                        }

                        if (client.SteamUserStats.SetStatValue(pair.Key, pair.Value) == false)
                        {
                            AppLog.Write("Failed to set int stat '" + pair.Key + "'");
                            return Fail("failed to set int stat '" + pair.Key + "'");
                        }

                        AppLog.Write("Set int stat '" + pair.Key + "': " + current + " -> " + pair.Value);
                        appliedStats++;
                    }

                    foreach (var pair in floatStats)
                    {
                        if (client.SteamUserStats.GetStatValue(pair.Key, out float current) == false)
                        {
                            AppLog.Write("Skip float stat '" + pair.Key + "': could not read current value");
                            continue;
                        }

                        if (pair.Value <= current)
                        {
                            AppLog.Write("Skip float stat '" + pair.Key + "': buffered " + pair.Value + " <= Steam " + current);
                            continue;
                        }

                        if (client.SteamUserStats.SetStatValue(pair.Key, pair.Value) == false)
                        {
                            AppLog.Write("Failed to set float stat '" + pair.Key + "'");
                            return Fail("failed to set float stat '" + pair.Key + "'");
                        }

                        AppLog.Write("Set float stat '" + pair.Key + "': " + current + " -> " + pair.Value);
                        appliedStats++;
                    }

                    if (appliedAchievements > 0 || appliedStats > 0)
                    {
                        AppLog.Write("Calling StoreStats (" + appliedAchievements + " ach, " + appliedStats + " stats)");
                        if (client.SteamUserStats.StoreStats() == false)
                        {
                            AppLog.Write("StoreStats failed");
                            return Fail("StoreStats failed");
                        }

                        PumpCallbacks(client, TimeSpan.FromSeconds(1));
                        AppLog.Write("StoreStats completed");
                    }
                    else
                    {
                        AppLog.Write("Nothing new to store in Steam");
                    }

                    return new FlushResult
                    {
                        Success = true,
                        Message = "Stats sent to Steam successfully",
                        AchievementsApplied = appliedAchievements,
                        StatsApplied = appliedStats,
                    };
                }
            }
            finally
            {
                AppLog.Write("Steam API disconnected");
            }
        }

        private static void WriteJob(
            string path,
            long appId,
            Dictionary<string, int> intStats,
            Dictionary<string, float> floatStats,
            List<string> achievements)
        {
            var lines = new List<string>
            {
                "APP\t" + appId.ToString(CultureInfo.InvariantCulture),
            };
            foreach (var pair in intStats)
            {
                lines.Add("I\t" + pair.Key + "\t" + pair.Value.ToString(CultureInfo.InvariantCulture));
            }

            foreach (var pair in floatStats)
            {
                lines.Add("F\t" + pair.Key + "\t" + pair.Value.ToString("G9", CultureInfo.InvariantCulture));
            }

            foreach (var id in achievements)
            {
                lines.Add("A\t" + id);
            }

            File.WriteAllLines(path, lines, new UTF8Encoding(false));
        }

        private static void ReadJob(
            string path,
            out long appId,
            out Dictionary<string, int> intStats,
            out Dictionary<string, float> floatStats,
            out List<string> achievements)
        {
            appId = 0;
            intStats = new Dictionary<string, int>(StringComparer.Ordinal);
            floatStats = new Dictionary<string, float>(StringComparer.Ordinal);
            achievements = new List<string>();

            foreach (var line in File.ReadAllLines(path, new UTF8Encoding(false)))
            {
                if (string.IsNullOrWhiteSpace(line) == true)
                {
                    continue;
                }

                var parts = line.Split('\t');
                if (parts.Length < 2)
                {
                    continue;
                }

                var kind = parts[0];
                if (kind == "APP")
                {
                    appId = long.Parse(parts[1], CultureInfo.InvariantCulture);
                }
                else if (kind == "I" && parts.Length >= 3)
                {
                    intStats[parts[1]] = int.Parse(parts[2], CultureInfo.InvariantCulture);
                }
                else if (kind == "F" && parts.Length >= 3)
                {
                    floatStats[parts[1]] = float.Parse(parts[2], CultureInfo.InvariantCulture);
                }
                else if (kind == "A")
                {
                    achievements.Add(parts[1]);
                }
            }

            if (appId <= 0)
            {
                throw new InvalidDataException("flush job is missing AppId");
            }
        }

        private static void WriteResult(string path, FlushResult result)
        {
            File.WriteAllLines(
                path,
                new[]
                {
                    result.Success ? "OK" : "ERR",
                    result.Message ?? string.Empty,
                    result.AchievementsApplied.ToString(CultureInfo.InvariantCulture),
                    result.StatsApplied.ToString(CultureInfo.InvariantCulture),
                },
                new UTF8Encoding(false));
        }

        private static FlushResult ReadResult(string path)
        {
            var lines = File.ReadAllLines(path, new UTF8Encoding(false));
            if (lines.Length < 1)
            {
                return Fail("Steam flush helper returned an empty result");
            }

            var success = lines[0] == "OK";
            var message = lines.Length > 1 ? lines[1] : string.Empty;
            var achievements = 0;
            var stats = 0;
            if (lines.Length > 2)
            {
                int.TryParse(lines[2], NumberStyles.Integer, CultureInfo.InvariantCulture, out achievements);
            }

            if (lines.Length > 3)
            {
                int.TryParse(lines[3], NumberStyles.Integer, CultureInfo.InvariantCulture, out stats);
            }

            return new FlushResult
            {
                Success = success,
                Message = message,
                AchievementsApplied = achievements,
                StatsApplied = stats,
            };
        }

        private static void TryDelete(string path)
        {
            try
            {
                if (File.Exists(path) == true)
                {
                    File.Delete(path);
                }
            }
            catch (IOException)
            {
            }
            catch (UnauthorizedAccessException)
            {
            }
        }

        private static FlushResult Fail(string reason)
        {
            AppLog.Write("Flush failed: " + reason);
            return new FlushResult
            {
                Success = false,
                Message = "Failed to send stats to Steam: " + reason,
            };
        }

        private static bool WaitForUserStats(Client client, long appId)
        {
            var received = new ManualResetEventSlim(false);
            var ok = false;
            var callback = client.CreateAndRegisterCallback<UserStatsReceived>();
            callback.OnRun += param =>
            {
                var callbackAppId = (uint)(param.GameId & 0xFFFFFF);
                if (callbackAppId != 0 && callbackAppId != (uint)appId)
                {
                    return;
                }

                ok = param.Result == ResultOk;
                received.Set();
            };

            var handle = client.SteamUserStats.RequestUserStats(client.SteamUser.GetSteamId());
            if (handle == CallHandle.Invalid)
            {
                AppLog.Write("RequestUserStats returned an invalid handle");
                return false;
            }

            var deadline = DateTime.UtcNow + TimeSpan.FromSeconds(15);
            while (received.IsSet == false && DateTime.UtcNow < deadline)
            {
                client.RunCallbacks(false);
                Thread.Sleep(50);
            }

            return received.IsSet == true && ok == true;
        }

        private static void PumpCallbacks(Client client, TimeSpan duration)
        {
            var deadline = DateTime.UtcNow + duration;
            while (DateTime.UtcNow < deadline)
            {
                client.RunCallbacks(false);
                Thread.Sleep(50);
            }
        }
    }
}
