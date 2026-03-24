using System;
using System.Net.Sockets;
using System.Text;
using NLog;

namespace Qognify.Networking
{
    public static class QognifySender
    {
        private static readonly Logger log = LogManager.GetCurrentClassLogger();

        public static bool Send(string ip, int port, string message)
        {
            int Send2QognifyTimeOutSec = Properties.Settings.Default.Send2QognifyTimeOutSec;
            bool _Send = false;
            try
            {

                using (var client = new TcpClient())
                {
                    //client.Connect(ip, port);
                    // Timeout court
                    var connectTask = client.ConnectAsync(ip, port);

                    if (!connectTask.Wait(TimeSpan.FromSeconds(Send2QognifyTimeOutSec)))
                    {
                        log.Warn($"Timeout de connexion vers {ip}:{port}");
                        return _Send;
                    }

                    var stream = client.GetStream();
                    var data = Encoding.ASCII.GetBytes(message);

                    stream.Write(data, 0, data.Length);
                    stream.Flush();

                    log.Info($"Message envoyé à {ip}:{port} → {message}");
                    _Send = true;
                }
            }
            catch (Exception ex)
            {
                log.Error($"Erreur lors de l'envoi vers {ip}:{port} message={ex.Message}");
                //ESA : SendQueueSystem msg SYSTEM (PORT 40000 + MSG "connexion : elements perdu") + variable LifeCheck.
                //throw;
            }
            return _Send;
        }
    }
}
