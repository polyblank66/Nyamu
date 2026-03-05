using System;
using System.Collections.Concurrent;
using System.Linq;
using System.Net;
using System.Net.Sockets;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

namespace Nyamu.Http
{
    // Minimal HTTP/1.1 server over TcpListener with SO_REUSEADDR.
    // Each accepted connection is handled on a ThreadPool thread; one request
    // per connection (Connection: close). Designed for local dev-tool use only.
    internal sealed class TcpHttpServer
    {
        private readonly int _port;
        private readonly Func<TcpHttpRequest, TcpHttpResponse, string> _requestHandler;

        private TcpListener _listener;
        private CancellationTokenSource _cts;
        private Task _acceptTask;
        private readonly ConcurrentDictionary<Task, byte> _activeHandlers = new();

        internal TcpHttpServer(int port, Func<TcpHttpRequest, TcpHttpResponse, string> requestHandler)
        {
            _port = port;
            _requestHandler = requestHandler;
        }

        // Binds the port and starts the accept loop. Throws on failure so the
        // caller can implement retry/deferred-recovery logic.
        internal void Start()
        {
            var listener = new TcpListener(IPAddress.Loopback, _port);
            listener.Server.SetSocketOption(SocketOptionLevel.Socket, SocketOptionName.ReuseAddress, true);
            listener.Start();
            _listener = listener;

            _cts = new CancellationTokenSource();
            _acceptTask = AcceptLoopAsync(_cts.Token);
            NyamuLogger.LogDebug($"[Nyamu][TcpHttpServer] Listening on port {_port}");
        }

        // Stops the accept loop, releases the port, and waits for in-flight
        // request handlers to finish (up to handlerTimeoutMs).
        internal void Stop(int handlerTimeoutMs = 2000)
        {
            try { _cts?.Cancel(); } catch (ObjectDisposedException) { }

            try
            {
                NyamuLogger.LogDebug("[Nyamu][TcpHttpServer] Stopping listener");
                _listener?.Stop();
                _listener = null;
                NyamuLogger.LogDebug("[Nyamu][TcpHttpServer] Listener stopped");
            }
            catch (Exception ex)
            {
                NyamuLogger.LogWarning($"[Nyamu][TcpHttpServer] Exception stopping listener: [{ex.GetType().Name}] {ex.Message}");
            }

            if (_acceptTask != null)
            {
                try
                {
                    if (_acceptTask.Wait(1000))
                        NyamuLogger.LogDebug("[Nyamu][TcpHttpServer] Accept loop exited cleanly");
                    else
                        NyamuLogger.LogDebug("[Nyamu][TcpHttpServer] Accept loop still running after 1s");
                }
                catch
                {
                    // ignored
                }
                finally { _acceptTask = null; }
            }

            var handlers = _activeHandlers.Keys.ToArray();
            if (handlers.Length > 0)
            {
                try
                {
                    if (!Task.WaitAll(handlers, handlerTimeoutMs))
                        NyamuLogger.LogWarning($"[Nyamu][TcpHttpServer] {handlers.Count(t => !t.IsCompleted)} handler(s) still running after {handlerTimeoutMs}ms, forcing cleanup");
                }
                catch
                {
                    // ignored
                }
                finally { _activeHandlers.Clear(); }
            }

            try { _cts?.Dispose(); }
            catch
            {
                // ignored
            }

            _cts = null;
        }

        private async Task AcceptLoopAsync(CancellationToken token)
        {
            while (!token.IsCancellationRequested)
            {
                try
                {
                    var clientTask = _listener.AcceptTcpClientAsync();
                    var completed = await Task.WhenAny(clientTask, Task.Delay(-1, token));
                    if (completed != clientTask) break;

                    var client = await clientTask;
                    var handlerTask = Task.Run(() =>
                    {
                        try { ProcessRequest(client); }
                        catch (Exception ex) { HandleException(ex); }
                    }, token);

                    _activeHandlers.TryAdd(handlerTask, 0);
                    _ = handlerTask.ContinueWith(t => _activeHandlers.TryRemove(t, out _), TaskScheduler.Default);
                }
                catch (ObjectDisposedException) { break; }
                catch (SocketException) { break; }
                catch (Exception ex) { HandleException(ex); }
            }
        }

        private void ProcessRequest(TcpClient client)
        {
            using (client)
            {
                var stream = client.GetStream();
                stream.ReadTimeout = 5000;
                var (request, response) = ParseRequest(stream);
                if (request == null) return;
                var body = _requestHandler(request, response);
                SendResponse(stream, response, body);
            }
        }

        private static (TcpHttpRequest, TcpHttpResponse) ParseRequest(NetworkStream stream)
        {
            // Read headers byte by byte until \r\n\r\n (fine for a local dev tool)
            var headerSb = new StringBuilder(512);
            var buf = new byte[1];
            while (headerSb.Length < 8192)
            {
                if (stream.Read(buf, 0, 1) == 0) return (null, null);
                headerSb.Append((char)buf[0]);
                var n = headerSb.Length;
                if (n >= 4 &&
                    headerSb[n - 4] == '\r' && headerSb[n - 3] == '\n' &&
                    headerSb[n - 2] == '\r' && headerSb[n - 1] == '\n')
                    break;
            }

            var lines = headerSb.ToString().Split(new[] { "\r\n" }, StringSplitOptions.None);
            if (lines.Length == 0) return (null, null);

            // Parse request line: "METHOD /path?query HTTP/1.1"
            var parts = lines[0].Split(' ');
            if (parts.Length < 2) return (null, null);
            var method = parts[0];
            var rawUri = parts[1];
            var qIdx = rawUri.IndexOf('?');
            string path, rawQuery;
            if (qIdx >= 0)
            {
                path = rawUri.Substring(0, qIdx);
                rawQuery = rawUri.Substring(qIdx + 1);
            }
            else
            {
                path = rawUri;
                rawQuery = "";
            }

            // Find Content-Length header
            var contentLength = 0;
            for (var i = 1; i < lines.Length; i++)
            {
                var colonIdx = lines[i].IndexOf(':');
                if (colonIdx <= 0) continue;
                if (string.Compare(lines[i].Substring(0, colonIdx).Trim(), "content-length",
                        StringComparison.OrdinalIgnoreCase) == 0)
                {
                    int.TryParse(lines[i].Substring(colonIdx + 1).Trim(), out contentLength);
                    break;
                }
            }

            // Read body
            var body = "";
            if (contentLength > 0)
            {
                var bodyBytes = new byte[contentLength];
                var totalRead = 0;
                while (totalRead < contentLength)
                {
                    var n = stream.Read(bodyBytes, totalRead, contentLength - totalRead);
                    if (n == 0) break;
                    totalRead += n;
                }
                body = Encoding.UTF8.GetString(bodyBytes, 0, totalRead);
            }

            return (new TcpHttpRequest(method, path, rawQuery, body), new TcpHttpResponse());
        }

        private static void SendResponse(NetworkStream stream, TcpHttpResponse response, string content)
        {
            var body = Encoding.UTF8.GetBytes(content);
            var statusText = response.StatusCode == 200 ? "OK" : "Not Found";
            var header = Encoding.UTF8.GetBytes(
                $"HTTP/1.1 {response.StatusCode} {statusText}\r\n" +
                "Content-Type: application/json\r\n" +
                $"Content-Length: {body.Length}\r\n" +
                "Connection: close\r\n" +
                "Access-Control-Allow-Origin: *\r\n" +
                "Access-Control-Allow-Methods: GET, POST, OPTIONS\r\n" +
                "Access-Control-Allow-Headers: Content-Type\r\n" +
                "\r\n");
            stream.Write(header, 0, header.Length);
            stream.Write(body, 0, body.Length);
        }

        private void HandleException(Exception ex)
        {
            if (ex is SocketException or ThreadAbortException) return;
            if (ex.Message.Contains("transport connection") ||
                ex.Message.Contains("forcibly closed") ||
                ex.Message.Contains("connection was aborted")) return;
            if (_cts == null || !_cts.IsCancellationRequested)
                NyamuLogger.LogException($"[Nyamu][TcpHttpServer] HTTP server error: {ex.Message}", ex);
        }
    }
}
