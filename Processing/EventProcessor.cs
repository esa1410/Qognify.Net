using NLog;
using Qognify.Config;
using Qognify.Logging;
using Qognify.Processing;
using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Threading;


namespace Qognify.Processing
{
    public class EventProcessor
    {
        private static readonly Logger log = LoggerFactory.GetLogger<EventProcessor>(); 
        
        private readonly ConcurrentQueue<string> _queue;
        private readonly TimeSpan _interval;
        private readonly CancellationToken _ct;

        // Champs nécessaires pour build_to_send
        private readonly Dictionary<string, double> _lastSentTimes = new Dictionary<string, double>();
        private readonly QognifySettings _settings;

        // Définition des champs FIXED-WIDTH (équivalent Python FIELDS)
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
                        log.Info("STEP 01 : Transform LIST_EVENTSHARED to Dictionary DICT_Events");

                        // 1) parse_fixed_width
                        var dictEvents = FixedWidthParser.Parse(batch, _fields);

                        // 2) build_to_send
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

                        // 3) log_tosend
                        LogToSend.Dump(toSend);

                        // 4) TODO : envoyer vers VMS Qognify
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
