using NLog;
using Qognify.Config;
using Qognify.Logging;
using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Threading;

namespace Qognify.Processing
{
    public class OutgoingEvent
    {
        public string Keyname { get; set; }
        public string AlarmNumber { get; set; }
        public int Port { get; set; }
    }

    public class EventProcessor
    {
        private static readonly Logger log = LoggerFactory.GetLogger<EventProcessor>();

        private readonly ConcurrentQueue<string> _queue;
        private readonly TimeSpan _interval;   // interval de traitement
        private readonly TimeSpan _sendInterval; // interval d'envoi
        private readonly CancellationToken _ct;

        // Champs nécessaires pour build_to_send
        private readonly Dictionary<string, double> _lastSentTimes = new Dictionary<string, double>();
        private readonly QognifySettings _settings;
        public string CSVFilesPathWeb = Properties.Settings.Default.CSVFilesPathWeb;
        public string BaseDirCSV = AppDomain.CurrentDomain.BaseDirectory;
      

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

        // File d'envoi interne (un seul thread → Queue simple)
        private readonly Queue<OutgoingEvent> _sendQueue = new Queue<OutgoingEvent>();

        public EventProcessor(
            ConcurrentQueue<string> queue,
            TimeSpan interval,
            CancellationToken ct,
            QognifySettings settings)
        {
            _queue = queue;
            _interval = interval;
            _sendInterval = TimeSpan.FromSeconds(Properties.Settings.Default.SendIntervalSeconds); // nouveau param dans settings
            _ct = ct;
            _settings = settings;
        }

        public void Start()
        {
            ThreadPool.QueueUserWorkItem(_ => Loop());
        }
        /// <summary>
        /// Loop for treatment buffer
        /// </summary>
        private void Loop()
        {
            var lastProcess = DateTime.UtcNow;
            var lastSend = DateTime.UtcNow;
            var ExpiryNoSendSec = Properties.Settings.Default.ExpiryNoSendSec;

            while (!_ct.IsCancellationRequested)
            {
                var now = DateTime.UtcNow;

                // Traitement des événements (parse + build)
                if ((now - lastProcess) >= _interval)
                {
                    var batch = DequeueAll();

                    if (batch.Count > 0)
                    {
                        log.Info("STEP 01 : Transform LIST_EVENTSHARED Fixed Width to Dictionary DICT_Events");

                        // 1) Découpage du contenu batch (Queue) fixed_width en Dictioaniare
                        var dictEvents = FixedWidthParser.Parse(batch, _fields);

                        // 2) build_to_send
                        //Console.WriteLine("STEP 02 : Traitement des données dans DICT_Events");
                        log.Info("STEP 02 : Traitement des données dans DICT_Events");

                        //string csvPath = System.IO.Path.Combine(
                        //    _settings.Files.BaseDir,
                        //    _settings.Files.CsvListKeynameAction
                        string csvPath = System.IO.Path.Combine(
                            BaseDirCSV,
                            _settings.Files.CsvListKeynameAction
                        );

                        BuildToSend.Build(
                            dictEvents,
                            _lastSentTimes,
                            csvPath,
                            BaseDirCSV,
                            _sendQueue
                        );

                        log.Info("STEP 03 : BuildToSend terminé, éléments ajoutés dans la file d'envoi");
                    }

                    lastProcess = now;
                }

                //todo check datetime event receive for expiry


                // Envoi vers Qognify (un élément à la fois)
                if ((now - lastSend) >= _sendInterval)
                {
                    TrySendOne();
                    lastSend = now;
                }

                Thread.Sleep(100);
            }
        }

        private List<string> DequeueAll()
        {
            var list = new List<string>();
            string item;
            while (_queue.TryDequeue(out item))
                list.Add(item);
            return list;
        }

        private void TrySendOne()
        {
            if (_sendQueue.Count == 0)
                return;

            var evt = _sendQueue.Dequeue();

            string qognifyIp = Properties.Settings.Default.qognifyIp;

            log.Info($"STEP SEND : Envoi vers Qognify → IP={qognifyIp}, PORT={evt.Port}, MSG={evt.AlarmNumber}, KEY={evt.Keyname}");

            try
            {
                Qognify.Networking.QognifySender.Send(qognifyIp, evt.Port, evt.AlarmNumber);
            }
            catch (Exception ex)
            {
                log.Error(ex, $"Erreur lors de l'envoi de l'événement KEY={evt.Keyname}");
            }
        }

        
    }
}
