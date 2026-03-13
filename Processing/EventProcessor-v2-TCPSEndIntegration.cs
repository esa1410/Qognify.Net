using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Threading;
using Qognify.Config;
using Qognify.Logging;
using Qognify.Networking;

namespace Qognify.Processing
{
    public class EventProcessor
    {
        private readonly ConcurrentQueue<string> _queue;
        private readonly TimeSpan _interval;
        private readonly CancellationToken _ct;
        private readonly QognifySettings _settings;

        private readonly Dictionary<string, double> _lastSentTimes = new Dictionary<string, double>();
        private readonly Dictionary<string, TcpClientSender> _senders =
            new Dictionary<string, TcpClientSender>(StringComparer.OrdinalIgnoreCase);

        private readonly List<Tuple<string, int>> _fields = new List<Tuple<string, int>>
        {
            Tuple.Create("DateTime", 20),
            Tuple.Create("Keyname", 40),
            Tuple.Create("AlarmType", 20),
            Tuple.Create("Ack", 12),
            Tuple.Create("Level", 4),
            Tuple.Create("Status", 20),
            Tuple.Create("Description", 86)
        };

        public EventProcessor(
            ConcurrentQueue<string> queue,
            TimeSpan interval,
            CancellationToken ct,
            QognifySettings settings)
        {
            _queue = queue;
            _interval = interval;
            _ct = ct;
            _settings = settings;
        }

        public void Start()
        {
            ThreadPool.QueueUserWorkItem(_ => Loop());
        }

        private TcpClientSender GetSender(string host, int port)
        {
            string key = $"{host}:{port}";

            if (_senders.ContainsKey(key))
                return _senders[key];

            var sender = new TcpClientSender(host, port, TimeSpan.FromSeconds(5));
            _senders[key] = sender;
            return sender;
        }

        private void Loop()
        {
            var last = DateTime.UtcNow;

            while (!_ct.IsCancellationRequested)
            {
                var now = DateTime.UtcNow;

                if ((now - last) >= _interval)
                {
                    var batch = DequeueAll();

                    if (batch.Count > 0)
                    {
                        Console.WriteLine("STEP 01 : Transform LIST_EVENTSHARED to Dictionary DICT_Events");

                        var dictEvents = FixedWidthParser.Parse(batch, _fields);

                        Console.WriteLine("STEP 02 : Traitement des données dans DICT_Events");

                        string csvPath = System.IO.Path.Combine(
                            _settings.Files.BaseDir,
                            _settings.Files.CsvListKeynameAction
                        );

                        var toSend = BuildToSend.Build(
                            dictEvents,
                            _lastSentTimes,
                            csvPath,
                            _settings.Files.BaseDir
                        );

                        LogToSend.Dump(toSend);

                        Console.WriteLine("STEP 03 : Envoi TCP vers Qognify");

                        foreach (var kv in toSend)
                        {
                            foreach (var rec in kv.Value)
                            {
                                string host = _settings.TcpClient.ServerSendHost;
                                int port = int.Parse(rec["PORT-TCP"]);

                                var sender = GetSender(host, port);

                                string payload =
                                    $"{rec["Keyname"]}|{rec["ALARM-NUMBER"]}|{rec["AlarmType"]}|{rec["Status"]}";

                                bool ok = sender.Send(payload);

                                if (ok)
                                    Console.WriteLine($"Sent to {host}:{port} -> {payload}");
                                else
                                    Console.WriteLine($"FAILED sending to {host}:{port}");
                            }
                        }
                    }

                    last = now;
                }

                Thread.Sleep(100);
            }
        }

        private List<string> DequeueAll()
        {
            var list = new List<string>();
            while (_queue.TryDequeue(out var item))
                list.Add(item);
            return list;
        }
    }
}
