using System;
using System.Collections.Concurrent;
using System.Threading;
using NLog;
using Qognify.Logging;

namespace Qognify.Processing
{
    public class SenderThread
    {
        private readonly ConcurrentQueue<OutgoingEvent> _queueSystem;
        private readonly ConcurrentQueue<OutgoingEvent> _queueNormal;
        private readonly TimeSpan _interval;
        private readonly CancellationToken _ct;

        private static readonly Logger log = LoggerFactory.GetLogger<SenderThread>();

        public SenderThread(
            ConcurrentQueue<OutgoingEvent> queueSystem,
            ConcurrentQueue<OutgoingEvent> queueNormal,
            TimeSpan interval,
            CancellationToken ct)
        {
            _queueSystem = queueSystem;
            _queueNormal = queueNormal;
            _interval = interval;
            _ct = ct;
        }

        public void Start()
        {
            Thread sender = new Thread(Run)
            {
                IsBackground = true,
                Name = "SenderThread"
            };
            sender.Start();
        }

        private void Run()
        {
            log.Info("SenderThread started.");

            while (!_ct.IsCancellationRequested)
            {
                try
                {
                    ProcessOne();
                }
                catch (Exception ex)
                {
                    log.Error($"SenderThread exception: {ex.Message}");
                }

                Thread.Sleep(_interval);
            }

            log.Info("SenderThread stopped.");
        }

        private void ProcessOne()
        {
            string qognifyIp = Properties.Settings.Default.qognifyIp;
            bool Send2Qognify = Properties.Settings.Default.Send2Qognify;
            int PurgeEventGapSec = Properties.Settings.Default.PurgeEventGapSec;
            bool currentEventNormal = false;

            OutgoingEvent evt;

            // Priorité aux événements système ==> à changer ? et mettre un delay pour envoyer les event system chaque x minutes.
            if (!_queueNormal.TryDequeue(out evt))
            {
                if (!_queueSystem.TryDequeue(out evt))
                {
                    return; // rien à envoyer
                }
            }
            else currentEventNormal = true;
            //log only if something to send
            log.Info($"SenderThread : Tentative d'envoi vers Qognify - Files d'envoi : System={_queueSystem.Count}, Normal={_queueNormal.Count}");

            //todo vérifier si le délai est expiré avant de l'enlever de la liste faire cette action si un échec à l'envoi est constaté

            TimeSpan difference = DateTime.Now - evt.EventDatetime;

            //check elapsedtime between 2 date for expiry
            if (difference.Seconds > PurgeEventGapSec)
            {
                //élément est oublié
                log.Info($"SenderThread : Échec envoi KEY={evt.Keyname} délai dépassé {evt.EventDatetime }");
                return;
            }





            //DDM : *** Rajouter le PURGE event si date de purge dépassée ?****

            //à ce stade l'événement est déjà retiré de la liste d'attente

            log.Info($"SenderThread SEND → IP={qognifyIp}, PORT={evt.Port}, MSG={evt.AlarmNumber}, KEY={evt.Keyname}");

            if (!Send2Qognify)
            {
                log.Info("SenderThread : [TEST MODE] Envoi désactivé");
                return;
            }


            try
            {
                log.Info($"SenderThread : Tentative d'envoi vers Qognify - Files d'envoi : System={_queueSystem.Count}, Normal={_queueNormal.Count}");
                bool SendSuccess = Qognify.Networking.QognifySender.Send(qognifyIp, evt.Port, evt.AlarmNumber);
                if (!SendSuccess)
                {
                    log.Warn($"SenderThread : Échec envoi KEY={evt.Keyname}");
                    //todo ddm neutralize alarme system qui perturbe les test SystemEvent.EnqueueEvent(_queueSystem, "SYSTEM.CLIENT_DISCONNECTED", "9002");
                    //if faut savoir de quel liste il vient et le rajouter
                    if (currentEventNormal)
                    {
                        //ajout de l'élément tant que pas expiré et qu'il n'est pas envoyé
                        _queueNormal.Enqueue(evt);
                    }
                }
                //Thread.Sleep(100);//Utile ?
            }
            catch (Exception ex)
            {
                log.Error($"SenderThread : Erreur lors de l'envoi de l'événement KEY={evt.Keyname} , Message = {ex.Message}");
            }

        }
    }
}
