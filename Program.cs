using System;
using System.Collections.Concurrent;
using System.IO;
using System.Net;
using System.Threading;
using Qognify.Config;
using Qognify.Networking;
using Qognify.Processing;
using NLog;
using Qognify.Logging;

namespace Qognify
{
    internal static class Program
    {
        public static readonly Logger log = LoggerFactory.GetLogger<EventProcessor>();
        //dynamic BaseDir path of the Application
        public static string baseDir = AppDomain.CurrentDomain.BaseDirectory;

        static void Main()
        {

            //Path Pour LogN
            var logDir = Path.Combine(baseDir, "logs");
            var logArchive = Path.Combine(logDir, "archive");
            Directory.CreateDirectory(logDir);
            Directory.CreateDirectory(logArchive);
            // Injecte la variable NLog
            //NLog.LogManager.Configuration.Variables["logDir"] = logDir;
            var config = NLog.LogManager.Configuration;
            config.Variables["logDir"] = logDir;
            //NLog.LogManager.ReconfigExistingLoggers();

            /// Création d'un Jeton pour arrêter le serveur
            var cts = new CancellationTokenSource();

            /// Creation d'un buffer In/Out - Alimentation/Consommateur
            var incomingQueue = new ConcurrentQueue<string>();

            // Création des queues d’envoi (normal + système)
            var sendQueueEvent = new ConcurrentQueue<OutgoingEvent>();
            var sendQueueSystem = new ConcurrentQueue<OutgoingEvent>();

            log.Warn("********** START  Qognify **********");
            SystemEvent.EnqueueEvent(sendQueueSystem, "SYSTEM.REBOOT", "SYS002- EBI LINK Restart");


            // création du serveur TCP en écoute PORT TCP
            var server = new TcpEventServer(
                IPAddress.Parse(Properties.Settings.Default.TCPServerHost),
                Properties.Settings.Default.TCPServerPort,
                incomingQueue,
                TimeSpan.FromSeconds(Properties.Settings.Default.ServerTimeoutSeconds),
                cts.Token);

            //Création du processeur d'événements pour traiter les événements reçus et préparer les événements à envoyer
            var processor = new EventProcessor(
                incomingQueue,
                TimeSpan.FromSeconds(Properties.Settings.Default.ProcessIntervalSeconds),
                cts.Token,
                //settings,           // ← 4th argument added
                sendQueueEvent,          // injection
                sendQueueSystem     // injection
            );

            //Création du thread d'envoi pour envoyer les événements préparés par le processeur
            var sender = new SenderThread(
                sendQueueSystem,
                sendQueueEvent,
                TimeSpan.FromMilliseconds(Properties.Settings.Default.SendIntervalMilliSeconds),
                cts.Token
            );

            Console.WriteLine("Qognify .NET server started. Press Ctrl+C to exit.");

            Console.CancelKeyPress += (s, e) =>
            {
                e.Cancel = true;
                cts.Cancel();
            };

            server.Start();
            processor.Start();
            sender.Start();

            while (!cts.IsCancellationRequested)
                Thread.Sleep(200);

            server.Dispose();
            Console.WriteLine("Stopped.");
        }
    }
}
