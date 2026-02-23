using System;
using System.Collections.Concurrent;
using System.Net;
using System.Net.Sockets;
using System.Text;
using System.Threading;

namespace Qognify.Networking
{
    public class TcpEventServer : IDisposable
    {
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
                Console.Error.WriteLine("TCP Server error: " + ex);
            }
        }

        private void HandleClient(TcpClient client)
        {
            using (client)
            {
                client.ReceiveTimeout = (int)_timeout.TotalMilliseconds;
                var buffer = new byte[1024];

                try
                {
                    using (var stream = client.GetStream())
                    {
                        int read;
                        while ((read = stream.Read(buffer, 0, buffer.Length)) > 0)
                        {
                            var data = Encoding.UTF8.GetString(buffer, 0, read);

                            if (data.StartsWith("\r\n"))
                                data = data.Substring(2);

                            var line = data.Trim();
                            if (!string.IsNullOrEmpty(line))
                                _queue.Enqueue(line);

                            if (_ct.IsCancellationRequested)
                                break;
                        }
                    }
                }
                catch (Exception ex)
                {
                    Console.Error.WriteLine("Client error: " + ex);
                }
            }
        }

        public void Dispose()
        {
            _listener.Stop();
        }
    }
}
