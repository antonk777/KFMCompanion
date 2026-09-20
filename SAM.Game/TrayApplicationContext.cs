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
        private const string AppName = "KFM Companion";
        private const long AppId = 1250;
        private const int Port = 27250;
        private const string GameProcessName = "KillingFloor";
        private static readonly TimeSpan SteamReleaseDelay = TimeSpan.FromSeconds(2);
        private static readonly TimeSpan ProcessGoneGrace = TimeSpan.FromSeconds(3);

        private readonly SessionBuffer _buffer = new();
        private readonly SemaphoreSlim _flushLock = new(1, 1);
        private readonly NotifyIcon _notifyIcon;
        private readonly ToolStripMenuItem _statusItem;
        private readonly ToolStripMenuItem _launchItem;
        private readonly Control _invoker;
        private readonly CancellationTokenSource _cts = new();
        private readonly EventWaitHandle _activate;
        private LogWindow _logWindow;
        private TcpCommandServer _server;
        private bool _exiting;
        private volatile bool _gameRunning;
        private volatile bool _sessionBusy;
        private volatile bool _waitingForGame;
        private volatile bool _steamDirty;

        public TrayApplicationContext(EventWaitHandle activate)
        {
            this._activate = activate ?? throw new ArgumentNullException(nameof(activate));
            this._invoker = new Control();
            _ = this._invoker.Handle;

            this._statusItem = new ToolStripMenuItem("Status: Starting…")
            {
                Enabled = false,
            };

            this._launchItem = new ToolStripMenuItem("Launch game", null, this.OnLaunchClicked)
            {
                Enabled = true,
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
            AppLog.Write("KFM Companion started");
            AppLog.Write("AppId=" + AppId + ", Port=" + Port + ", GameProcessName=" + GameProcessName);
            AppLog.Write("You can start Killing Floor yourself. This companion must stay running to save stats.");
            AppLog.Write("Closing this window hides it to the tray. Use Exit in the tray menu to quit.");
            this.SetStatus("Status: Starting…", AppName + " — starting");
            this.ShowLogWindow();
            Task.Run(this.WatchActivate);
            Task.Run(() => this.RunSessionLoopAsync(this._cts.Token));
        }

        private void WatchActivate()
        {
            var handles = new WaitHandle[] { this._activate, this._cts.Token.WaitHandle };
            while (this._exiting == false)
            {
                int signaled;
                try
                {
                    signaled = WaitHandle.WaitAny(handles);
                }
                catch (ObjectDisposedException)
                {
                    return;
                }

                if (signaled != 0 || this._exiting == true)
                {
                    return;
                }

                this.OnUi(this.ShowLogWindow);
            }
        }

        private async Task RunSessionLoopAsync(CancellationToken cancellationToken)
        {
            if (this._sessionBusy == true || this._exiting == true)
            {
                return;
            }

            this._sessionBusy = true;
            try
            {
                while (this._exiting == false && cancellationToken.IsCancellationRequested == false)
                {
                    if (await this.RunOneSessionAsync(cancellationToken).ConfigureAwait(false) == false)
                    {
                        return;
                    }
                }
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
                this._waitingForGame = false;
                this._server?.Dispose();
                this._server = null;
                this._sessionBusy = false;
                if (this._exiting == false)
                {
                    this.SetLaunchEnabled(this.CanLaunchGame());
                }
            }
        }

        private async Task<bool> RunOneSessionAsync(CancellationToken cancellationToken)
        {
            this._buffer.Clear();
            this._steamDirty = false;

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
                return false;
            }

            var existing = GameProcessWatcher.GetProcessIds(GameProcessName);
            AppLog.Write("Existing '" + GameProcessName + "' processes: " + existing.Count);

            Process gameProcess = GameProcessWatcher.TryGetRunningProcess(GameProcessName);
            if (gameProcess != null)
            {
                AppLog.Write("Game already running (PID " + gameProcess.Id + "), attaching");
            }
            else
            {
                this._waitingForGame = true;
                this.SetLaunchEnabled(true);
                this.SetStatus("Status: Waiting for game…", AppName + " — waiting for game");
                AppLog.Write("Waiting for '" + GameProcessName + "'. Start it yourself or click Launch game.");
            }

            try
            {
                using (var process = gameProcess ?? await this.WaitForGameProcessAsync(existing, cancellationToken).ConfigureAwait(false))
                {
                    this._waitingForGame = false;
                    this.SetLaunchEnabled(false);
                    AppLog.Write("Watching game process: " + GameProcessName + " (PID " + process.Id + ")");
                    this._gameRunning = true;
                    this.UpdateRunningStatus();
                    this.RequestLiveFlush();
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
                return false;
            }

            this._server?.Dispose();
            this._server = null;
            AppLog.Write("TCP server stopped");

            await this.FlushToSteamAsync(false, cancellationToken).ConfigureAwait(false);

            AppLog.Write("Ready for the next session. Start the game yourself or click Launch game.");
            return true;
        }

        private void RequestLiveFlush()
        {
            if (this._gameRunning == false || this._exiting == true || this._buffer.HasWork == false)
            {
                return;
            }

            this._steamDirty = true;
            _ = this.FlushLiveIfDirtyAsync();
        }

        private async Task FlushLiveIfDirtyAsync()
        {
            try
            {
                await this._flushLock.WaitAsync(this._cts.Token).ConfigureAwait(false);
            }
            catch (ObjectDisposedException)
            {
                return;
            }
            catch (OperationCanceledException)
            {
                return;
            }

            try
            {
                while (this._steamDirty == true &&
                       this._gameRunning == true &&
                       this._exiting == false &&
                       this._cts.IsCancellationRequested == false)
                {
                    this._steamDirty = false;
                    await this.FlushToSteamCoreAsync(true, this._cts.Token).ConfigureAwait(false);
                }
            }
            catch (OperationCanceledException)
            {
            }
            catch (ObjectDisposedException)
            {
            }
            finally
            {
                this._flushLock.Release();
            }

            if (this._steamDirty == true && this._gameRunning == true && this._exiting == false)
            {
                _ = this.FlushLiveIfDirtyAsync();
            }
        }

        private async Task FlushToSteamAsync(bool live, CancellationToken cancellationToken)
        {
            await this._flushLock.WaitAsync(cancellationToken).ConfigureAwait(false);
            try
            {
                await this.FlushToSteamCoreAsync(live, cancellationToken).ConfigureAwait(false);
            }
            finally
            {
                this._flushLock.Release();
            }
        }

        private async Task FlushToSteamCoreAsync(bool live, CancellationToken cancellationToken)
        {
            if (this._buffer.HasWork == false)
            {
                return;
            }

            this.SetStatus("Status: Flushing to Steam…", AppName + " — flushing");
            if (live == true)
            {
                AppLog.Write("Flushing buffered stats to Steam");
            }
            else
            {
                AppLog.Write("Waiting " + SteamReleaseDelay.TotalSeconds + "s for Steam to release the game session");
                await Task.Delay(SteamReleaseDelay, cancellationToken).ConfigureAwait(false);
            }

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

            if (live == true)
            {
                if (result.Success == false)
                {
                    AppLog.Write("Live Steam flush failed; will retry on the next update and after the game exits");
                }

                if (this._gameRunning == true)
                {
                    this.UpdateRunningStatus();
                }

                return;
            }

            if (result.Success == true)
            {
                this.SetStatus("Status: Waiting for game…", AppName + " — waiting for game");
            }
            else
            {
                this.SetStatus("Status: Error — flush failed", AppName + " — flush failed");
                this.ShowBalloon(ToolTipIcon.Error, AppName, result.Message);
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
                    Timeout.InfiniteTimeSpan,
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
            if (this._gameRunning == true)
            {
                this.UpdateRunningStatus();
            }

            this.RequestLiveFlush();
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
                this._logWindow.LaunchClicked += this.OnLaunchClicked;
                this._logWindow.SetLaunchEnabled(this.CanLaunchGame());
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

                if (this._logWindow != null && this._logWindow.IsDisposed == false)
                {
                    this._logWindow.SetLaunchEnabled(enabled);
                }
            });
        }

        private bool CanLaunchGame()
        {
            return this._exiting == false &&
                   this._gameRunning == false &&
                   (this._waitingForGame == true || this._sessionBusy == false);
        }

        private void TryLaunchViaSteam()
        {
            try
            {
                GameProcessWatcher.LaunchViaSteam(AppId);
                AppLog.Write("Launched steam://run/" + AppId);
            }
            catch (Exception e)
            {
                AppLog.Write("Failed to launch game: " + e.Message);
            }
        }

        private void OnLaunchClicked(object sender, EventArgs e)
        {
            if (this.CanLaunchGame() == false)
            {
                return;
            }

            AppLog.Write("Launch game requested");
            if (this._waitingForGame == true)
            {
                this.TryLaunchViaSteam();
                return;
            }

            Task.Run(() => this.RunSessionLoopAsync(this._cts.Token));
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
                this._flushLock.Dispose();
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
