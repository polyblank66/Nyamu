using System;
using System.Net;
using System.Net.Sockets;
using System.Text;
using System.Threading;
using NUnit.Framework;
using Nyamu.Http;

namespace Nyamu.EditorTests
{
    // Guards the threading of TcpHttpServer's accept loop. Start() runs on the Unity main thread,
    // where SynchronizationContext.Current is UnitySynchronizationContext. While the loop awaited
    // on that context its continuations were posted back to the main thread, so a playing,
    // unfocused Editor with "Run In Background" off - which suspends the main loop - stopped
    // serving every endpoint, editor_status included: clients hung on a connection the kernel had
    // completed into the backlog that nobody ever accepted.
    [TestFixture]
    public class TcpHttpServerMainThreadTests
    {
        [Test]
        public void ServesRequestsWhileTheMainThreadIsBlocked()
        {
            var port = FreePort();
            var server = new TcpHttpServer(port, (request, response) => "{\"ok\":true}");
            server.Start();

            try
            {
                string body = null;
                Exception failure = null;
                var caller = new Thread(() =>
                {
                    try { body = Get(port); }
                    catch (Exception ex) { failure = ex; }
                }) { IsBackground = true };
                caller.Start();

                // NUnit runs EditMode tests on the main thread, and a synchronous [Test] holds it
                // for its whole duration - so this blocks exactly what an unfocused Play Mode
                // session blocks, UnitySynchronizationContext included. Sleep then Join keeps the
                // main thread unavailable across the entire request.
                Thread.Sleep(1000);
                var finished = caller.Join(2000);

                Assert.That(failure, Is.Null, $"The HTTP call threw: {failure}");
                Assert.That(finished, Is.True,
                    "The request never completed - the accept loop needs the blocked main thread.");
                Assert.That(body, Is.EqualTo("{\"ok\":true}"));
            }
            finally
            {
                server.Stop();
            }
        }

        // Bind-then-release on port 0: lets the OS pick a port nothing else holds, so the test
        // never collides with the Editor's own running server.
        static int FreePort()
        {
            var probe = new TcpListener(IPAddress.Loopback, 0);
            probe.Start();
            var port = ((IPEndPoint)probe.LocalEndpoint).Port;
            probe.Stop();
            return port;
        }

        // Raw socket rather than a web client: nothing here may touch a Unity API or the thread
        // pool in a way that could itself depend on the main thread.
        static string Get(int port)
        {
            using (var client = new TcpClient())
            {
                client.Connect(IPAddress.Loopback, port);
                var stream = client.GetStream();
                stream.ReadTimeout = 3000;

                var request = Encoding.ASCII.GetBytes("GET /editor-status HTTP/1.1\r\nHost: localhost\r\n\r\n");
                stream.Write(request, 0, request.Length);

                var sb = new StringBuilder();
                var buf = new byte[512];
                int read;
                while ((read = stream.Read(buf, 0, buf.Length)) > 0)
                    sb.Append(Encoding.UTF8.GetString(buf, 0, read));

                var text = sb.ToString();
                var bodyStart = text.IndexOf("\r\n\r\n", StringComparison.Ordinal);
                return bodyStart < 0 ? text : text.Substring(bodyStart + 4);
            }
        }
    }
}
