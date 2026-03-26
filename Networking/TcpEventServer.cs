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
using static System.Net.Mime.MediaTypeNames;

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

        //Attendre une connexion d'un client TCP
        private void AcceptLoop()
        {
            try
            {
                while (!_ct.IsCancellationRequested)
                {
                    //_listener.Pending() signifie : “Est ce qu’un client tente de se connecter ?”
                    // C'est une boucle qui tourne en permanence et relance un Handle si nécessaire.
                    if (!_listener.Pending())
                    {
                        Thread.Sleep(100);//Nml : 100
                        //log.Debug("TcpEventServer : Check de connexion d'un client");
                        continue;
                    }
                    log.Debug("TcpEventServer : Connexion acceptée et démarrage HandleClient ");
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
            var buffer = new byte[1024];//1024
            var sb = new StringBuilder();   // buffer cumulatif

            try
            {
                using (client)
                using (var stream = client.GetStream())
                {
                    int read;
                    // Petit timeout tentative de lecture chaque x ms
                    client.ReceiveTimeout = 200;
                    DateTime lastReceive = DateTime.UtcNow;

                    while (!_ct.IsCancellationRequested)
                    {
                        // Check si réception dans le délai "ServerTimeoutSeconds". Attention, uniquement si Client n'a pas coupé la connexion.
                        if ((DateTime.UtcNow - lastReceive) > _timeout)
                        {
                            log.Warn($"TcpEventServer : No data received for {_timeout.TotalSeconds} seconds — timeout");
                            lastReceive = DateTime.UtcNow; // reset du watchdog
                            continue;
                        }
                        try
                        {
                            read = stream.Read(buffer, 0, buffer.Length);
                            lastReceive = DateTime.UtcNow; // reset du watchdog

                            string blocmultiline = Encoding.UTF8.GetString(buffer, 0, read);
                            log.Debug($"TcpEventServer : Received: {blocmultiline}");

                            sb.Append(blocmultiline);

                            string data = sb.ToString();
                            int idx;
                            // Tant qu’on trouve une ligne complète
                            while ((idx = data.IndexOf("\r\n")) >= 0)
                            {
                                string line = data.Substring(0, idx).Trim();
                                log.Debug($"TcpEventServer : Ligne complète, Enqueue {line}");
                                if (line.Length > 0)
                                    _queue.Enqueue(line);

                                data = data.Substring(idx + 2);
                            }

                            // Garder uniquement la fin incomplète
                            sb.Clear();
                            sb.Append(data);
                        }
                        catch (IOException)
                        {
                            // Read() a expiré → pas de réception. L'erreur doit être capturée pour ne pas planter le Handle. On continue.
                            //log.Debug($"TcpEventServer : Read expiré");
                            continue;
                        }

                        if (read == 0)
                        {
                            log.Debug($"[TCP SERVER] client close the connexion - disconnected");
                            break; // client disconnected
                        }

                    }
                }
            }
            catch (Exception ex)
            {
                log.Error("[TCP SERVER] Unhandled exception in HandleClient: " + ex);
            }

        }

        public void Dispose()
        {
            _listener.Stop();
        }
    }
}
