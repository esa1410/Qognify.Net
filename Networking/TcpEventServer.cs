using NLog;
using Qognify.Logging;
using Qognify.Processing;
using System;
using System.Collections.Concurrent;
using System.IO;
using System.Net;
using System.Net.Sockets;
using System.Text;
using System.Threading;

namespace Qognify.Networking
{
    public class TcpEventServer : IDisposable
    {
        private static readonly Logger log = LoggerFactory.GetLogger<EventProcessor>(); 
        private readonly TcpListener _listener;
        private readonly ConcurrentQueue<string> _queue;
        private readonly TimeSpan _timeout;
        private readonly CancellationToken _ct;

        public TcpEventServer(
            IPAddress ip,
            int port,
            ConcurrentQueue<string> queue,
            TimeSpan timeout,
            CancellationToken ct)
        {
            _queue = queue;
            _timeout = timeout;
            _ct = ct;
            _listener = new TcpListener(ip, port);
        }

        public void Start()
        {
            _listener.Start();
            ThreadPool.QueueUserWorkItem(_ => AcceptLoop());
        }

        private void AcceptLoop()
        {
            try
            {
                while (!_ct.IsCancellationRequested)
                {
                    if (!_listener.Pending())
                    {
                        Thread.Sleep(100);
                        continue;
                    }

                    var client = _listener.AcceptTcpClient();
                    ThreadPool.QueueUserWorkItem(_ => HandleClient(client));
                }
            }
            catch (Exception ex)
            {
                log.Error("TCP Server error: " + ex);
            }
        }

        private void HandleClient(TcpClient client)
        {
            using (client)
            {
                client.ReceiveTimeout = (int)_timeout.TotalMilliseconds;
                var buffer = new byte[1024];//1024
                var sb = new StringBuilder();   // buffer cumulatif

                try
                {
                    using (var stream = client.GetStream())
                    {
                        int read;
                        while ((read = stream.Read(buffer, 0, buffer.Length)) > 0)
                        {
                            string blocmultiline = Encoding.UTF8.GetString(buffer, 0, read);
                            log.Debug($"[TCP SERVER] Received: {blocmultiline}");

                            sb.Append(blocmultiline);

                            string data = sb.ToString();
                            int idx;
                            // Tant qu’on trouve une ligne complète
                            while ((idx = data.IndexOf("\r\n")) >= 0)
                            {
                                string line = data.Substring(0, idx).Trim();
                                log.Debug($"[TCP SERVER] Ligne complète, Enqueue {line}");
                                if (line.Length > 0)
                                    _queue.Enqueue(line);

                                data = data.Substring(idx + 2);
                            }

                            // Garder uniquement la fin incomplète
                            sb.Clear();
                            sb.Append(data);

                            if (_ct.IsCancellationRequested)
                                break;
                        }
                    }
                }
                catch (IOException ioEx) when (ioEx.InnerException is SocketException sockEx &&
                              sockEx.SocketErrorCode == SocketError.TimedOut)
                {
                    log.Error($"[TCP SERVER] Receive timeout after {_timeout.TotalSeconds} seconds — no data received.");
                }
                catch (Exception ex)
                {
                    log.Error("Client error: " + ex);
                }
            }
        }

        public void Dispose()
        {
            _listener.Stop();
        }
    }
}
