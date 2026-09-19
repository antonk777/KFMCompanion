using System;
using System.Globalization;
using System.IO;
using System.Net;
using System.Net.Sockets;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

namespace SAM.Game
{
    internal sealed class TcpCommandServer : IDisposable
    {
        private readonly TcpListener _listener;
        private readonly SessionBuffer _buffer;
        private readonly CancellationTokenSource _cts = new();
        private bool _disposed;

        public TcpCommandServer(int port, SessionBuffer buffer)
        {
            this._buffer = buffer ?? throw new ArgumentNullException(nameof(buffer));
            this._listener = new TcpListener(IPAddress.Loopback, port);
        }

        public void Start()
        {
            this._listener.Start();
            AppLog.Write("TCP server started");
            Task.Run(this.AcceptLoopAsync);
        }

        private async Task AcceptLoopAsync()
        {
            while (this._cts.IsCancellationRequested == false)
            {
                TcpClient client;
                try
                {
                    client = await this._listener.AcceptTcpClientAsync().ConfigureAwait(false);
                    AppLog.Write("TCP client connected");
                }
                catch (ObjectDisposedException)
                {
                    return;
                }
                catch (SocketException)
                {
                    if (this._cts.IsCancellationRequested == true)
                    {
                        return;
                    }

                    continue;
                }

                _ = Task.Run(() => this.HandleClientAsync(client));
            }
        }

        private async Task HandleClientAsync(TcpClient client)
        {
            using (client)
            {
                try
                {
                    using (var stream = client.GetStream())
                    using (var reader = new StreamReader(stream, Encoding.UTF8, false, 1024, true))
                    using (var writer = new StreamWriter(stream, new UTF8Encoding(false), 1024, true)
                    {
                        NewLine = "\n",
                        AutoFlush = true,
                    })
                    {
                        while (this._cts.IsCancellationRequested == false)
                        {
                            string line;
                            try
                            {
                                line = await reader.ReadLineAsync().ConfigureAwait(false);
                            }
                            catch (IOException)
                            {
                                AppLog.Write("TCP client disconnected");
                                return;
                            }

                            if (line == null)
                            {
                                AppLog.Write("TCP client disconnected");
                                return;
                            }

                    var response = HandleLine(this._buffer, line);
                    AppLog.Write("TCP << " + line.Trim());
                    AppLog.Write("TCP >> " + response);
                    await writer.WriteLineAsync(response).ConfigureAwait(false);
                        }
                    }
                }
                catch (IOException)
                {
                }
                catch (ObjectDisposedException)
                {
                }
            }
        }

        internal static string HandleLine(SessionBuffer buffer, string line)
        {
            if (line == null)
            {
                return "ERR empty command";
            }

            line = line.Trim();
            if (line.Length == 0)
            {
                return "ERR empty command";
            }

            var parts = line.Split((char[])null, StringSplitOptions.RemoveEmptyEntries);
            var command = parts[0].ToUpperInvariant();

            switch (command)
            {
                case "PING":
                    return "PONG";

                case "QUIT":
                    return "OK";

                case "STORE":
                    return "OK BUFFERED";

                case "ACHIEVEMENT":
                {
                    if (parts.Length < 3)
                    {
                        return "ERR usage: ACHIEVEMENT <id> <0|1>";
                    }

                    if (int.TryParse(parts[parts.Length - 1], NumberStyles.Integer, CultureInfo.InvariantCulture, out var flag) == false)
                    {
                        return "ERR invalid achievement flag";
                    }

                    var id = JoinId(parts, 1, parts.Length - 1);
                    if (flag == 0)
                    {
                        return "OK BUFFERED";
                    }

                    if (flag != 1)
                    {
                        return "ERR achievement flag must be 0 or 1";
                    }

                    buffer.UnlockAchievement(id);
                    return "OK BUFFERED";
                }

                case "STAT_INT":
                {
                    if (parts.Length < 3)
                    {
                        return "ERR usage: STAT_INT <id> <value>";
                    }

                    if (int.TryParse(parts[parts.Length - 1], NumberStyles.Integer, CultureInfo.InvariantCulture, out var value) == false)
                    {
                        return "ERR invalid int value";
                    }

                    buffer.SetIntStat(JoinId(parts, 1, parts.Length - 1), value);
                    return "OK BUFFERED";
                }

                case "STAT_FLOAT":
                {
                    if (parts.Length < 3)
                    {
                        return "ERR usage: STAT_FLOAT <id> <value>";
                    }

                    if (float.TryParse(parts[parts.Length - 1], NumberStyles.Float, CultureInfo.InvariantCulture, out var value) == false)
                    {
                        return "ERR invalid float value";
                    }

                    buffer.SetFloatStat(JoinId(parts, 1, parts.Length - 1), value);
                    return "OK BUFFERED";
                }

                default:
                    return "ERR unknown command";
            }
        }

        private static string JoinId(string[] parts, int start, int endExclusive)
        {
            if (endExclusive - start == 1)
            {
                return parts[start];
            }

            return string.Join(" ", parts, start, endExclusive - start);
        }

        public void Dispose()
        {
            if (this._disposed == true)
            {
                return;
            }

            this._disposed = true;
            AppLog.Write("TCP server stopping");
            this._cts.Cancel();
            try
            {
                this._listener.Stop();
            }
            catch (SocketException)
            {
            }

            this._cts.Dispose();
        }
    }
}
