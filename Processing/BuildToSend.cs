using NLog;
using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;

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
            string csvListKeynameActionPath,
            string baseDir,
            Queue<OutgoingEvent> sendQueue)
        {

            //ddm load filter reference into filtermap
            //todo check if flag exist 
            string CSVFilesPathWeb = Properties.Settings.Default.CSVFilesPathWeb;
            string FlagFile = CSVFilesPathWeb + @"\flag";
            bool exists = File.Exists(FlagFile);

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
                        CopyFilefromWebIntoActive(CSVFilesPathWeb,baseDir);
                    }
                }
                //todo créer une copie des fichiers

                filterMap = FilterLoader.LoadFilterCsv(csvListKeynameActionPath);
            }


            //ddm create alarmTypeCache dictinnary with ingnore case 
            var alarmTypeCache = new Dictionary<string, HashSet<string>>(StringComparer.OrdinalIgnoreCase);

            foreach (var rec in events)
            {
                string key = rec["Keyname"];
                string eventdate = rec["DateTime"];
                string alarmType = rec["AlarmType"];

                string dateString = ConvertDate(eventdate);

                DateTime result;
                
                string format = "dd-MM-yyyy HH:mm:ss";

                if (DateTime.TryParseExact(dateString, format, CultureInfo.InvariantCulture, DateTimeStyles.None, out  result))
                {
                    //todo add for expiry time and log into no send
                    //compare delay 
                    Console.WriteLine($"Date convertie : {result:dd/MM/yyyy HH:mm:ss}");
                    TimeSpan difference = DateTime.Now - result;

                    Console.WriteLine($"Difference {difference.Seconds }");

                }
                else
                {
                    Console.WriteLine("Échec de la conversion.");
                }




                //log.Info($"STEP 01 : ***New Event***  :  {eventdate} - {key} - {alarmType} ");
                //if (!filterMap.ContainsKey(key))
                //{
                //    log.Debug($"STEP 01 : Skip {key} Not in List");
                //    continue;
                //}
                //# build_to_send:STEP 02 : Si oui, alors on charge les informations pour cette Key.
                //# Pour une Key, il peut y avoir plusieurs combinaison PORT-TCP et Type Alm.
                //# On parcoure la liste des combinaison pour ce Keyname : càd PORT+ALm Type
                //# Et ensuite, on teste si le type Alm est autorisé pour cette copmbinaison.

                //ddm ignore receive with ACK and OK
                if (rec["Ack"].Trim().Length == 0)
                {
                    foreach (var param in filterMap[key])
                    {
                        string alarmNumber = param["ALARM-NUMBER"];
                        string portStr = param["PORT-TCP"];
                        int delay = int.Parse(param["DELAY-RESEND"]);
                        string csvAlm = param["CSVFILEALM"];

                        log.Debug($"STEP 02 : Find a combinaison for {key} with {alarmNumber}, {portStr}, {csvAlm} and delay {delay} from List-Keyname-Action");

                        string uniqueKey = $"{key}:{alarmNumber}:{portStr}";
                        log.Debug($"STEP 03 : Création de Unique key {uniqueKey} pour gérer les rate-limit en fonction du paramètre délai + ignorer les key en doublons");

                        //ESA TODO : Refaire la gestion Doublon
                        //log.Debug($"STEP 04 : Check des doublons et si '{key}' est déjà dans ToSend on passe au suivant");
                        //if (toSend.ContainsKey(key))
                        //{
                        //    foreach (var existing in toSend[key])
                        //    {
                        //        string existingKey = $"{key}:{existing["ALARM-NUMBER"]}:{existing["PORT-TCP"]}";
                        //        if (existingKey.Equals(uniqueKey, StringComparison.OrdinalIgnoreCase))
                        //        {
                        //            log.Debug($"STEP 04 : Check Duplicate : Unique Key {uniqueKey} already in ToSend : Skipped");
                        //            goto SkipCombination;
                        //        }
                        //    }
                        //}
                        log.Debug($"STEP 05 : Load allowed AlarmTypes for {csvAlm}");
                        if (!alarmTypeCache.ContainsKey(csvAlm))
                        {
                            string path = Path.Combine(baseDir, csvAlm + ".csv");
                            alarmTypeCache[csvAlm] = FilterLoader.LoadAlarmTypeCsv(path);
                        }

                        if (!alarmTypeCache[csvAlm].Contains(alarmType))
                        {
                            log.Debug($"STEP 05 : Alm Type {alarmType} is not allowed for {key} in {csvAlm} -> check in file List-Keyname-Action if other type are allowed for {key}");
                            goto SkipCombination;
                        }

                        log.Debug($"STEP 06 : Check si {delay} écoulé pour {key}.");
                        if (delay > 0)
                        {
                            double now = DateTimeOffset.UtcNow.ToUnixTimeSeconds();
                            double last = lastSentTimes.ContainsKey(uniqueKey) ? lastSentTimes[uniqueKey] : 0;
                            double elapsed = now - last;

                            if (elapsed < delay)
                            {
                                log.Debug("Délai {delay} pas encore écoulé : {elapsed}");
                                goto SkipCombination;
                            }
                            //Délai écoulé → mise à jour du timestamp
                            lastSentTimes[uniqueKey] = now;
                        }
                        int port;
                        if (!int.TryParse(portStr, out  port))
                        {
                            log.Error($"PORT-TCP invalide pour {key} : {portStr}");
                            goto SkipCombination;
                        }

                        log.Debug($"STEP 07 : {key} accepté pour envoie → ajout dans la file d'envoi");

                        sendQueue.Enqueue(new OutgoingEvent
                        {
                            Keyname = key,
                            AlarmNumber = alarmNumber,
                            Port = port
                        });

                        SkipCombination:
                        continue;
                    }
                }
            }
        }

        private static void CopyFilefromWebIntoActive(string sourceDir, string DestinationCsvFile )
        {
            // Récupère tous les chemins de fichiers du répertoire source
            string[] files = Directory.GetFiles(sourceDir);
            //string DestinationCsvFile = Properties.Settings.Default.CSVFilesPath;

            foreach (string file in files)
            {
                // Extrait uniquement le nom du fichier (ex: "photo.jpg")
                string fileName = Path.GetFileName(file);
                // Combine la destination avec le nom du fichier
                string destPath = Path.Combine(DestinationCsvFile,fileName );


                // Copie le fichier (true pour écraser si déjà présent)
                File.Copy(file, destPath, true);
            }
        }

        public static string ConvertDate(string MyDate)
        {
            string[] MYMonth = { "Jan", "Feb", "Mar", "Apr", "May", "Jun", "Jul", "Aug", "Sep", "Oct", "Nov", "Dec" };
            string strdate = MyDate.Substring(0, 9);

            string[] ext = strdate.Split('-');
            int MYear;
            string MMonth;
            int MDay;
            string strHours = MyDate.Substring(11, 8);

            string tmp="";
            

            if (ext.Length == 3)
            {
                MDay = int.Parse(ext[0].ToString());
                MMonth = ext[1];
                MYear = int.Parse(ext[2].ToString());
                int nMonth = Array.IndexOf(MYMonth, MMonth);
                nMonth++;
                tmp = string.Format("{0:#00}-", MDay) + string.Format("{0:#00}-", nMonth) + string.Format("2{0:#000}", MYear);
                tmp = tmp + " " + strHours;
            }
            return tmp;

        }
    }
}
