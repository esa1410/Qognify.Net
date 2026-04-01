using NLog;
using Qognify.Config;
using Qognify.Logging;
using System;
using System.IO;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Diagnostics.Eventing.Reader;
using System.Threading;

namespace Qognify.Processing
{
    public class OutgoingEvent
    {
        public string Keyname { get; set; }
        public string AlarmNumber { get; set; }
        public int Port { get; set; }
        public DateTime EventDatetime { get; set; } //append for date time event
    }

    public class EventProcessor
    {
        private static readonly Logger log = LoggerFactory.GetLogger<EventProcessor>();

        private readonly ConcurrentQueue<string> _queue;
        private readonly TimeSpan _interval;   // interval de traitement
        //private readonly TimeSpan _sendInterval; // interval d'envoi
        private readonly CancellationToken _ct;

        // Variable pour suivre le dernier événement reçu (pour la logique d'expiration)
        private DateTime _lastEventReceived = DateTime.UtcNow;

        // Champs nécessaires pour build_to_send
        private readonly Dictionary<string, double> _lastSentTimes = new Dictionary<string, double>();
        //private readonly QognifySettings _settings;
        public string CSVFilesPathWeb = Properties.Settings.Default.CSVFilesPathWeb;
        //public string BaseDirCSV = AppDomain.CurrentDomain.BaseDirectory;


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

        private readonly ConcurrentQueue<OutgoingEvent> _sendQueue = new ConcurrentQueue<OutgoingEvent>();
        private readonly ConcurrentQueue<OutgoingEvent> _sendQueueSystem = new ConcurrentQueue<OutgoingEvent>();

        public EventProcessor(
            ConcurrentQueue<string> queue,
            TimeSpan interval,
            CancellationToken ct,
            ConcurrentQueue<OutgoingEvent> sendQueue,
            ConcurrentQueue<OutgoingEvent> sendQueueSystem)
        {
            _queue = queue;
            _interval = interval;
            _ct = ct;
            _sendQueue = sendQueue;
            _sendQueueSystem = sendQueueSystem;
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
            //var lastSend = DateTime.UtcNow;
            var ServerTimeoutSeconds = Properties.Settings.Default.ServerTimeoutSeconds;

            while (!_ct.IsCancellationRequested)
            {
                var now = DateTime.UtcNow;

                // Vérification TimeOut de EBI.
                if ((now - _lastEventReceived).TotalSeconds > ServerTimeoutSeconds)
                {
                    log.Warn($"EventProcessor : Aucun événement reçu depuis {ServerTimeoutSeconds} sec -> ALM SYSTEM Generated");
                   //todo ddm neutralize alarme system qui perturbe les test  SystemEvent.EnqueueEvent(_sendQueueSystem,"SYSTEM.NO_EVENT","SYS001-NO_EVENT-FromEBI");
                    _lastEventReceived = now;
                }

                // Traitement des événements (parse + build)
                if ((now - lastProcess) >= _interval)
                {
                    var batch = DequeueAll();

                    if (batch.Count > 0)
                    {
                        log.Info("EventProcessor 01 : Transform LIST_EVENTSHARED Fixed Width to Dictionary DICT_Events");

                        // 1) Découpage du contenu batch (Queue) fixed_width en Dictioaniare
                        var dictEvents = FixedWidthParser.Parse(batch, _fields);

                        // 2) build_to_send
                        //Console.WriteLine("EventProcessor 02 : Traitement des données dans DICT_Events");
                        log.Info("EventProcessor 02 : Traitement des données dans DICT_Events");

                        //string csvPath = System.IO.Path.Combine(
                        //    _settings.Files.BaseDir,
                        //    _settings.Files.CsvListKeynameAction
                        //string csvPath = Path.Combine(Program.baseDir,Properties.Settings.Default.CsvListKeynameAction);

                        BuildToSend.Build(
                            dictEvents,
                            _lastSentTimes,
                            //csvPath,
                            //Program.baseDir,
                            _sendQueue
                        );

                        log.Debug("EventProcessor 03 : BuildToSend terminé, éléments ajoutés dans la file d'envoi");
                    }

                    lastProcess = now;
                }

                Thread.Sleep(100);
            }
        }
        
        /// <summary>
        /// Removes and returns all items from the queue as a list of strings.
        /// </summary>
        /// <returns>A list containing all dequeued strings from the queue.</returns>
        private List<string> DequeueAll()
        {
            var list = new List<string>();
            string item;
            while (_queue.TryDequeue(out item))
            {
                list.Add(item);
                _lastEventReceived = DateTime.UtcNow;// Mise à jour du timestamp du dernier événement reçu
            }
            return list;
        }
    }
}
