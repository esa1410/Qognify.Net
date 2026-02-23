using System;
using System.Collections.Concurrent;
using System.IO;
using System.Net;
using System.Threading;
using Qognify.Config;
using Qognify.Networking;
using Qognify.Processing;

namespace Qognify
{
    internal static class Program
    {
        static void Main()
        {
            /// Load Json Setting
            var baseDir = AppDomain.CurrentDomain.BaseDirectory;
            var settingsPath = Path.Combine(baseDir, "appsettings.json");
            var settings = AppSettingsLoader.Load(settingsPath);

            /// Création d'un Jeton pour arrêter le serveur
            var cts = new CancellationTokenSource();
            /// Creation d'un buffer In/Out - Alimentation/Consommateur
            var queue = new ConcurrentQueue<string>();

            // création du serveur TCP en écoute PORT TCP
            var server = new TcpEventServer(
                IPAddress.Parse(settings.TcpServer.Host),
                settings.TcpServer.Port,
                queue,
                TimeSpan.FromSeconds(settings.TcpServer.ServerTimeoutSeconds),
                cts.Token);

            var processor = new EventProcessor(
                queue,
                TimeSpan.FromSeconds(settings.TcpServer.ProcessIntervalSeconds),
                cts.Token,
                settings   // ← 4th argument added
            );

            Console.WriteLine("Qognify .NET server started. Press Ctrl+C to exit.");

            Console.CancelKeyPress += (s, e) =>
            {
                e.Cancel = true;
                cts.Cancel();
            };

            server.Start();
            processor.Start();

            while (!cts.IsCancellationRequested)
                Thread.Sleep(200);

            server.Dispose();
            Console.WriteLine("Stopped.");
        }
    }
}
