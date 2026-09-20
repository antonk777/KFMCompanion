using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Drawing;
using System.Threading;
using System.Threading.Tasks;
using System.Windows.Forms;
using SAM.API;

namespace SAM.Game
{
    internal sealed class TrayApplicationContext : ApplicationContext
    {
        private const string AppName = "KFM Launcher";
        private const long AppId = 1250;
        private const int Port = 27250;
        private const string GameProcessName = "KillingFloor";
        private static readonly TimeSpan GameStartTimeout = TimeSpan.FromSeconds(120);
        private static readonly TimeSpan SteamReleaseDelay = TimeSpan.FromSeconds(2);
        private static readonly TimeSpan ProcessGoneGrace = TimeSpan.FromSeconds(3);

        private readonly SessionBuffer _buffer = new();
        private readonly NotifyIcon _notifyIcon;
        private readonly ToolStripMenuItem _statusItem;
        private readonly ToolStripMenuItem _launchItem;
        private readonly Control _invoker;
        private readonly CancellationTokenSource _cts = new();
        private LogWindow _logWindow;
        private TcpCommandServer _server;
        private bool _exiting;
        private volatile bool _gameRunning;
        private volatile bool _sessionBusy;

        public TrayApplicationContext()
        {
            this._invoker = new Control();
            _ = this._invoker.Handle;

            this._statusItem = new ToolStripMenuItem("Status: Starting…")
            {
                Enabled = false,
            };

            this._launchItem = new ToolStripMenuItem("Launch game", null, this.OnLaunchClicked)
            {
                Enabled = false,
            };

            var menu = new ContextMenuStrip();
            menu.Items.Add(this._statusItem);
            menu.Items.Add(this._launchItem);
            menu.Items.Add(new ToolStripSeparator());
            menu.Items.Add(new ToolStripMenuItem("Exit", null, this.OnExitClicked));

            this._notifyIcon = new NotifyIcon
            {
                Icon = AppIcon.Load(),
                Visible = true,
                ContextMenuStrip = menu,
                Text = AppName,
            };
            this._notifyIcon.MouseClick += this.OnTrayMouseClick;

            this._buffer.Changed += this.OnBufferChanged;
            AppLog.Write("KFM Launcher started");
            AppLog.Write("AppId=" + AppId + ", Port=" + Port + ", GameProcessName=" + GameProcessName);
            this.SetStatus("Status: Starting…", AppName + " — starting");
            Task.Run(() => this.RunSessionAsync(this._cts.Token));
        }

        private async Task RunSessionAsync(CancellationToken cancellationToken)
        {
            if (this._sessionBusy == true || this._exiting == true)
            {
                return;
            }

            this._sessionBusy = true;
            this.SetLaunchEnabled(false);
            this._buffer.Clear();

            try
            {
                try
                {
                    this._server = new TcpCommandServer(Port, this._buffer);
                    this._server.Start();
                    AppLog.Write("TCP listening on 127.0.0.1:" + Port);
                }
                catch (Exception e)
                {
                    AppLog.Write("TCP bind failed: " + e.Message);
                    this.FailAndStay("TCP bind failed", e.Message);
                    return;
                }

                var existing = GameProcessWatcher.GetProcessIds(GameProcessName);
                AppLog.Write("Existing '" + GameProcessName + "' processes: " + existing.Count);

                Process gameProcess = GameProcessWatcher.TryGetRunningProcess(GameProcessName);
                if (gameProcess != null)
                {
                    AppLog.Write("Game already running (PID " + gameProcess.Id + "), not launching");
                }
                else
                {
                    this.SetStatus("Status: Waiting for game…", AppName + " — waiting for game");
                    AppLog.Write("Waiting for game process '" + GameProcessName + "'");

                    try
                    {
                        GameProcessWatcher.LaunchViaSteam(AppId);
                        AppLog.Write("Launched steam://run/" + AppId);
                    }
                    catch (Exception e)
                    {
                        AppLog.Write("Failed to launch game: " + e.Message);
                        this.FailAndStay("failed to launch game", e.Message);
                        return;
                    }
                }

                try
                {
                    using (var process = gameProcess ?? await this.WaitForGameProcessAsync(existing, cancellationToken).ConfigureAwait(false))
                    {
                        AppLog.Write("Watching game process: " + GameProcessName + " (PID " + process.Id + ")");
                        this._gameRunning = true;
                        this.UpdateRunningStatus();
                        await GameProcessWatcher.WaitForExitAsync(process, cancellationToken).ConfigureAwait(false);
                        AppLog.Write("Game process exited (PID " + process.Id + ")");
                    }

                    AppLog.Write("Waiting until no '" + GameProcessName + "' processes remain");
                    await GameProcessWatcher.WaitUntilNoneRemainAsync(
                        GameProcessName,
                        ProcessGoneGrace,
                        cancellationToken).ConfigureAwait(false);
                    this._gameRunning = false;
                    AppLog.Write("Game process is gone");
                }
                catch (OperationCanceledException)
                {
                    AppLog.Write("Session canceled");
                    return;
                }
                catch (TimeoutException e)
                {
                    AppLog.Write("Game process not found: " + e.Message);
                    this.FailAndStay("process not found", e.Message);
                    return;
                }

                this._server?.Dispose();
                this._server = null;
                AppLog.Write("TCP server stopped");

                this.SetStatus("Status: Flushing to Steam…", AppName + " — flushing");
                AppLog.Write("Waiting " + SteamReleaseDelay.TotalSeconds + "s for Steam to release the game session");
                await Task.Delay(SteamReleaseDelay, cancellationToken).ConfigureAwait(false);

                FlushResult result;
                try
                {
                    result = SteamStatsBridge.Flush(AppId, this._buffer);
                }
                catch (ClientInitializeException e)
                {
                    var reason = e.Failure == ClientInitializeFailure.ConnectToGlobalUser
                        ? "Steam not running"
                        : (string.IsNullOrEmpty(e.Message) ? e.Failure.ToString() : e.Message);
                    AppLog.Write("Steam init failed: " + reason);
                    result = new FlushResult
                    {
                        Success = false,
                        Message = "Failed to send stats to Steam: " + reason,
                    };
                }
                catch (Exception e)
                {
                    AppLog.Write("Flush exception: " + e.Message);
                    result = new FlushResult
                    {
                        Success = false,
                        Message = "Failed to send stats to Steam: " + e.Message,
                    };
                }

                AppLog.Write(result.Success
                    ? "Flush finished: " + result.Message +
                      " (applied " + result.AchievementsApplied + " ach, " + result.StatsApplied + " stats)"
                    : "Flush finished with error: " + result.Message);
                if (result.Success == true)
                {
                    this.SetStatus("Status: OK — ready to launch", AppName + " — ready");
                }
                else
                {
                    this.SetStatus("Status: Error — flush failed", AppName + " — flush failed");
                    this.ShowBalloon(ToolTipIcon.Error, AppName, result.Message);
                }

                AppLog.Write("Idle. Use tray menu to launch the game again.");
            }
            catch (OperationCanceledException)
            {
                AppLog.Write("Session canceled");
            }
            catch (Exception e)
            {
                AppLog.Write("Unexpected error: " + e);
                this.FailAndStay("unexpected error", e.Message);
            }
            finally
            {
                this._gameRunning = false;
                this._server?.Dispose();
                this._server = null;
                this._sessionBusy = false;
                if (this._exiting == false)
                {
                    this.SetLaunchEnabled(true);
                }
            }
        }

        private async Task<Process> WaitForGameProcessAsync(
            HashSet<int> existing,
            CancellationToken cancellationToken)
        {
            try
            {
                return await GameProcessWatcher.WaitForNewProcessAsync(
                    GameProcessName,
                    existing,
                    GameStartTimeout,
                    cancellationToken).ConfigureAwait(false);
            }
            catch (TimeoutException)
            {
                foreach (var id in GameProcessWatcher.GetProcessIds(GameProcessName))
                {
                    try
                    {
                        AppLog.Write("Attaching to already running process PID " + id);
                        return Process.GetProcessById(id);
                    }
                    catch (ArgumentException)
                    {
                    }
                    catch (InvalidOperationException)
                    {
                    }
                }

                throw;
            }
        }

        private void OnBufferChanged()
        {
            if (this._gameRunning == false)
            {
                return;
            }

            this.UpdateRunningStatus();
        }

        private void UpdateRunningStatus()
        {
            var achievements = this._buffer.AchievementCount;
            var stats = this._buffer.StatCount;
            if (achievements > 0 || stats > 0)
            {
                this.SetStatus(
                    "Status: OK — buffering (" + achievements + " ach, " + stats + " stats)",
                    AppName + " — buffering");
            }
            else
            {
                this.SetStatus("Status: OK — game running", AppName + " — game running");
            }
        }

        private void FailAndStay(string shortReason, string detail)
        {
            AppLog.Write("Error: " + shortReason + (string.IsNullOrEmpty(detail) ? "" : " — " + detail));
            this.SetStatus("Status: Error — " + shortReason, AppName + " — error");
            this.ShowBalloon(ToolTipIcon.Error, AppName, detail ?? shortReason);
        }

        private void OnTrayMouseClick(object sender, MouseEventArgs e)
        {
            if (e.Button != MouseButtons.Left)
            {
                return;
            }

            this.ShowLogWindow();
        }

        private void ShowLogWindow()
        {
            if (this._logWindow == null || this._logWindow.IsDisposed == true)
            {
                this._logWindow = new LogWindow();
            }

            this._logWindow.Show();
            if (this._logWindow.WindowState == FormWindowState.Minimized)
            {
                this._logWindow.WindowState = FormWindowState.Normal;
            }

            this._logWindow.Activate();
        }

        private void SetStatus(string menuText, string tooltip)
        {
            this.OnUi(() =>
            {
                this._statusItem.Text = menuText;
                if (string.IsNullOrEmpty(tooltip) == true)
                {
                    tooltip = menuText;
                }

                if (tooltip.Length > 63)
                {
                    tooltip = tooltip.Substring(0, 63);
                }

                this._notifyIcon.Text = tooltip;
            });
        }

        private void ShowBalloon(ToolTipIcon icon, string title, string text)
        {
            this.OnUi(() =>
            {
                this._notifyIcon.BalloonTipIcon = icon;
                this._notifyIcon.BalloonTipTitle = title;
                this._notifyIcon.BalloonTipText = string.IsNullOrEmpty(text) ? title : text;
                this._notifyIcon.ShowBalloonTip(4000);
            });
        }

        private void SetLaunchEnabled(bool enabled)
        {
            this.OnUi(() =>
            {
                if (this._launchItem.IsDisposed == false)
                {
                    this._launchItem.Enabled = enabled;
                }
            });
        }

        private void OnLaunchClicked(object sender, EventArgs e)
        {
            if (this._sessionBusy == true || this._exiting == true)
            {
                return;
            }

            AppLog.Write("Launch game requested from tray");
            Task.Run(() => this.RunSessionAsync(this._cts.Token));
        }

        private void OnExitClicked(object sender, EventArgs e)
        {
            this.ExitFromBackground();
        }

        private void ExitFromBackground()
        {
            this.OnUi(this.Shutdown);
        }

        private void Shutdown()
        {
            if (this._exiting == true)
            {
                return;
            }

            this._exiting = true;
            AppLog.Write("Exiting");
            this._cts.Cancel();
            this._server?.Dispose();
            if (this._logWindow != null && this._logWindow.IsDisposed == false)
            {
                this._logWindow.AllowClose();
                this._logWindow.Close();
            }
            this._notifyIcon.Visible = false;
            this.ExitThread();
        }

        private void OnUi(Action action)
        {
            if (this._invoker.IsDisposed == true)
            {
                return;
            }

            if (this._invoker.InvokeRequired == true)
            {
                try
                {
                    this._invoker.BeginInvoke(action);
                }
                catch (ObjectDisposedException)
                {
                }

                return;
            }

            action();
        }

        protected override void Dispose(bool disposing)
        {
            if (disposing == true)
            {
                this._cts.Cancel();
                this._server?.Dispose();
                this._notifyIcon.Visible = false;
                this._notifyIcon.Icon?.Dispose();
                this._notifyIcon.Dispose();
                this._invoker.Dispose();
                this._cts.Dispose();
            }

            base.Dispose(disposing);
        }
    }
}
