using NLog;
using NLog.LayoutRenderers;
using System;
using System.Collections;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;

namespace Qognify.Processing
{
    public static class BuildToSend
    {
        //ddm declaration pour variable global
        private static Dictionary<string, List<Dictionary<string, string>>> filterMap;
        private static readonly Logger log = Logging.LoggerFactory.GetLogger<EventProcessor>();

        public static void Build(
            List<Dictionary<string, string>> events,
            Dictionary<string, double> lastSentTimes,
            ConcurrentQueue<OutgoingEvent> sendQueue)
        {

            //ddm load filter reference into filtermap
            //todo check if flag exist 
            string CSVFilesPathWeb = Properties.Settings.Default.CSVFilesPathWeb;
            string FlagFile = CSVFilesPathWeb + @"\flag";
            bool exists = File.Exists(FlagFile);
            string baseDir = Program.baseDir;
            string csvListKeynameActionPath = Path.Combine(Program.baseDir, Properties.Settings.Default.CsvListKeynameAction);

            //if flag exist then reload filter
            if (filterMap == null || exists)
            {
                //Logging.LogToSend.Dump()
                log.Info($"Keyname reload because change occurs");
                if (exists)
                {
                    if (File.Exists(FlagFile))
                    {
                        File.Delete(FlagFile);
                        CopyFilefromWebIntoActive(CSVFilesPathWeb, baseDir);
                    }
                }
                //todo créer une copie des fichiers

                filterMap = FilterLoader.LoadFilterCsv(csvListKeynameActionPath);
            }


            //ddm create alarmTypeCache dictinnary with ingnore case 
            var alarmTypeCache = new Dictionary<string, HashSet<string>>(StringComparer.OrdinalIgnoreCase);

            foreach (var rec in events) //Pour chaque Event
            {
                string key = rec["Keyname"];
                string eventdate = rec["DateTime"];
                string alarmType = rec["Value"];

                log.Info($"BuildToSend 01 : ***New Event***  :  {eventdate} - {key} *** {alarmType} ***");
                if (!filterMap.ContainsKey(key))
                {
                    log.Debug($"STEP 01 : Skip {key} Not in List");
                    continue;
                }
                //# build_to_send:STEP 02 : Si la source existe dans "List-Keyname-Action.csv", alors on charge les informations pour cette Key.
                //# Pour une Key, il peut y avoir plusieurs combinaison PORT-TCP et Type Alm.
                //# On parcoure la liste des combinaison pour ce Keyname : càd PORT+ALm Type
                //# Et ensuite, on teste si le type Alm est autorisé pour cette combinaison.

                //ddm : Ignore Event with ACK and OK
                if (rec["Ack"].Trim().Length == 0)
                {
                    foreach (var param in filterMap[key]) //Pour chaque combinaison
                    {
                        string alarmNumber = param["ALARM-NUMBER"];
                        string portStr = param["PORT-TCP"];
                        int delay = int.Parse(param["DELAY-RESEND"]);
                        string csvAlm = param["CSVFILEALM"];

                        int port;
                        if (!int.TryParse(portStr, out port))
                        {
                            log.Error($"BuildToSend 02 : PORT-TCP invalide pour {key} : {portStr}");
                            goto SkipCombination;
                        }

                        log.Debug($"BuildToSend 02 : Find a combinaison for {key} with {alarmNumber}, {portStr}, {csvAlm} and delay {delay} from List-Keyname-Action");

                        string uniqueKey = $"{key}:{alarmNumber}:{portStr}";
                        log.Debug($"BuildToSend 03 : Création de Unique key {uniqueKey} : Gestion delay limit");

                        //Gestion Doublons
                        log.Debug($"BuildToSend 04 : Check des doublons et si '{uniqueKey}' est déjà dans ToSend");
                        foreach (var evt in sendQueue)
                        {
                            string SendQueueExistingKey = $"{evt.Keyname}:{evt.AlarmNumber}:{evt.Port}";
                            if (SendQueueExistingKey.Equals(uniqueKey, StringComparison.OrdinalIgnoreCase))
                            //if (evt.Keyname == key && evt.AlarmNumber == alarmNumber && evt.Port == port)
                            {
                                log.Debug($"BuildToSend 04 : (-) Check Duplicate : Unique Key {uniqueKey} already in ToSend : Skipped");
                                goto SkipCombination;
                            }
                        }

                        log.Debug($"BuildToSend 05 : Load allowed AlarmTypes for {csvAlm}");
                        if (!alarmTypeCache.ContainsKey(csvAlm))
                        {
                            string path = Path.Combine(baseDir, csvAlm + ".csv");
                            alarmTypeCache[csvAlm] = FilterLoader.LoadAlarmTypeCsv(path);
                        }


                        //if (!alarmTypeCache[csvAlm].Any(filter =>alarmType.Contains(filter, StringComparison.OrdinalIgnoreCase))) //Pas valide pour .Net4.8
                        if (alarmTypeCache[csvAlm].Any(filter => alarmType != null && filter != null && alarmType.IndexOf(filter, StringComparison.OrdinalIgnoreCase) >= 0))
                        {
                            log.Debug($"BuildToSend 06 : Check si délai {delay} est écoulé pour {key}.");
                            if (delay > 0)
                            {
                                double now = DateTimeOffset.UtcNow.ToUnixTimeSeconds();
                                double last = lastSentTimes.ContainsKey(uniqueKey) ? lastSentTimes[uniqueKey] : 0;
                                double elapsed = now - last;

                                if (elapsed < delay)
                                {
                                    log.Debug($"BuildToSend 06 : (-) Délai {delay} pas encore écoulé : {elapsed} pour {uniqueKey}");
                                    goto SkipCombination;
                                }
                                //Délai écoulé → mise à jour du timestamp
                                lastSentTimes[uniqueKey] = now;
                            }

                            log.Debug($"BuildToSend 07 : (+) {uniqueKey} est accepté pour envoie → ajout dans la file d'envoi");
                            //todo ddm gestion de l'erreur si la date de l'événement n'est pas correct
                            sendQueue.Enqueue(new OutgoingEvent
                            {
                                Keyname = key,
                                AlarmNumber = alarmNumber,
                                Port = port,
                                EventDatetime = ConvertDate(eventdate)
                            });

                            goto GotoNextEvent;
                        }
                        else
                        {
                            log.Debug($"BuildToSend 05 : (-) Alm Type {alarmType} n'est pas accepté {key} in {csvAlm} -> check in file List-Keyname-Action if other type are allowed for {key}");
                        }
                    SkipCombination:
                        continue;
                    }
                }
                else
                {
                    log.Debug($"BuildToSend 01 : (-) RTN / ACK for source {key}");
                }
            GotoNextEvent:
                continue;
            }
        }

        private static DateTime ConvertDate(string eventdate)
        {
            DateTime result;
            string format = "dd-MMM-yy  HH:mm:ss";
            //int PurgeEventGapSec = Properties.Settings.Default.PurgeEventGapSec;

            if (DateTime.TryParseExact(eventdate, format, CultureInfo.InvariantCulture, DateTimeStyles.None, out result))
            {
                //todo add for expiry time and log into no send
                //compare delay 
                // Console.WriteLine($"Date convertie : {result:dd/MM/yyyy HH:mm:ss}");
                TimeSpan difference = DateTime.UtcNow - result;

                //Console.WriteLine($"Difference {difference.Seconds }");

            }
            else
            {
                //Console.WriteLine("Échec de la conversion.");
            }

            return result;
        }

        private static void CopyFilefromWebIntoActive(string sourceDir, string DestinationCsvFile)
        {
            // Récupère tous les chemins de fichiers du répertoire source
            string[] files = Directory.GetFiles(sourceDir);
            //string DestinationCsvFile = Properties.Settings.Default.CSVFilesPath;

            foreach (string file in files)
            {
                // Extrait uniquement le nom du fichier (ex: "photo.jpg")
                string fileName = Path.GetFileName(file);
                // Combine la destination avec le nom du fichier
                string destPath = Path.Combine(DestinationCsvFile, fileName);


                // Copie le fichier (true pour écraser si déjà présent)
                File.Copy(file, destPath, true);
            }
        }

    }
}
