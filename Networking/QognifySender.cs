using System;
using System.Net.Sockets;
using System.Text;
using NLog;

namespace Qognify.Networking
{
    public static class QognifySender
    {
        private static readonly Logger log = LogManager.GetCurrentClassLogger();

        public static void Send(string ip, int port, string message)
        {
            try
            {
                using (var client = new TcpClient())
                {
                    client.Connect(ip, port);

                    var stream = client.GetStream();
                    var data = Encoding.ASCII.GetBytes(message);

                    stream.Write(data, 0, data.Length);
                    stream.Flush();

                    log.Info($"Message envoyé à {ip}:{port} → {message}");
                }
            }
            catch (Exception ex)
            {
                log.Error(ex, $"Erreur lors de l'envoi vers {ip}:{port}");
                throw;
            }
        }
    }
}
