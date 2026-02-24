using NLog;
using Qognify.Logging;
using System;
using System.Net.Sockets;
using System.Text;

namespace Qognify.Networking
{
    public class TcpClientSender : IDisposable
    {
        private readonly string _host;
        private readonly int _port;
        private readonly TimeSpan _timeout;

        private TcpClient _client;
        private NetworkStream _stream;
        
        private static readonly Logger log = LoggerFactory.GetLogger<TcpClientSender>();

        public TcpClientSender(string host, int port, TimeSpan timeout)
        {
            _host = host;
            _port = port;
            _timeout = timeout;
        }

        private void EnsureConnected()
        {
            if (_client != null && _client.Connected)
                return;

            Dispose();

            _client = new TcpClient();
            _client.ReceiveTimeout = (int)_timeout.TotalMilliseconds;
            _client.SendTimeout = (int)_timeout.TotalMilliseconds;

            _client.Connect(_host, _port);
            _stream = _client.GetStream();
        }

        
        public bool Send(string message)
        {
            try
            {
                EnsureConnected();

                byte[] data = Encoding.UTF8.GetBytes(message + "\r\n");
                _stream.Write(data, 0, data.Length);
                _stream.Flush();
                log.Info($"Sent to {_host}:{_port} -> {message}");
                return true;
            }
            catch (Exception ex)
            {
                Console.Error.WriteLine($"[TcpClientSender] Error sending to {_host}:{_port} -> {ex.Message}");
                Dispose();
                return false;
            }
        }

        public void Dispose()
        {
            try { _stream?.Dispose(); } catch { }
            try { _client?.Close(); } catch { }
            _stream = null;
            _client = null;
        }
    }
}
