using Qognify.Config;
using Qognify.Networking;
using Qognify.Processing;
using System;
using System.Collections.Concurrent;
using System.IO;
using System.Net;
using System.Threading;

namespace Qognify
{
    internal static class Program
    {
        static void Main()
        {


            //var baseDir = AppDomain.CurrentDomain.BaseDirectory;
            //var fullPath = Path.Combine(baseDir, "appsettings.json");

            //Console.WriteLine("Path: " + fullPath);
            //Console.WriteLine("Exists: " + File.Exists(fullPath));
            //Console.WriteLine("Content:");
            //Console.WriteLine(File.ReadAllText(fullPath));

            var settings = AppSettingsLoader.Load("appsettings.json");

            var cts = new CancellationTokenSource();
            var queue = new ConcurrentQueue<string>();

            var server = new TcpEventServer(
                IPAddress.Parse(settings.TcpServer.Host),
                settings.TcpServer.Port,
                queue,
                TimeSpan.FromSeconds(settings.TcpServer.ServerTimeoutSeconds),
                cts.Token);

            var processor = new EventProcessor(
                queue,
                TimeSpan.FromSeconds(settings.TcpServer.ProcessIntervalSeconds),
                cts.Token);

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
