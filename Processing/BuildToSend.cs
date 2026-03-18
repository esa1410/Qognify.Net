using NLog;
using System;
using System.Collections.Generic;
using System.IO;


namespace Qognify.Processing
{
    public static class BuildToSend
    {
        //ddm declaration pour variable global
        private static Dictionary<string, List<Dictionary<string, string>>> filterMap;
        private static readonly Logger log = Logging.LoggerFactory.GetLogger<EventProcessor>();

        public static Dictionary<string, List<Dictionary<string, string>>> Build(
            List<Dictionary<string, string>> events,
            Dictionary<string, double> lastSentTimes,
            string csvListKeynameActionPath,
            string baseDir)
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
                        CopyFilefromWebIntoActive(CSVFilesPathWeb);
                    }
                }
                //todo créer une copie des fichiers

                filterMap = FilterLoader.LoadFilterCsv(csvListKeynameActionPath);

            }


            //ddm create alarmTypeCache dictinnary with ingnore case 
            var alarmTypeCache = new Dictionary<string, HashSet<string>>(StringComparer.OrdinalIgnoreCase);

            //ddm create tosend dictionary with all field 
            var toSend = new Dictionary<string, List<Dictionary<string, string>>>(StringComparer.OrdinalIgnoreCase);


            foreach (var rec in events)
            {
                string key = rec["Keyname"];
                string eventdate = rec["DateTime"];
                string alarmType = rec["AlarmType"];

                log.Info($"STEP 01 : ***New Event***  :  {eventdate} - {key} - {alarmType} ");
                if (!filterMap.ContainsKey(key))
                {
                    log.Debug($"STEP 01 : Skip {key} Not in List");
                    continue;
                }
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
                        string port = param["PORT-TCP"];
                        int delay = int.Parse(param["DELAY-RESEND"]);
                        string csvAlm = param["CSVFILEALM"];

                        log.Debug($"STEP 02 : Find a combinaison for {key} with {alarmNumber}, {port}, {csvAlm} and delay {delay} from List-Keyname-Action");

                        string uniqueKey = $"{key}:{alarmNumber}:{port}";
                        log.Debug($"STEP 03 : Création de Unique key {uniqueKey} pour gérer les rate-limit en fonction du paramètre délai + ignorer les key en doublons");

                        log.Debug($"STEP 04 : Check des doublons et si '{key}' est déjà dans ToSend on passe au suivant");
                        if (toSend.ContainsKey(key))
                        {
                            foreach (var existing in toSend[key])
                            {
                                string existingKey = $"{key}:{existing["ALARM-NUMBER"]}:{existing["PORT-TCP"]}";
                                if (existingKey.Equals(uniqueKey, StringComparison.OrdinalIgnoreCase))
                                {
                                    log.Debug($"STEP 04 : Check Duplicate : Unique Key {uniqueKey} already in ToSend : Skipped");
                                    goto SkipCombination;
                                }
                            }
                        }

                        //to do 
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

                        log.Debug($"STEP 07 : {key} accepté pour envoie et on rajoute le PORT et le MSG à envoyer");
                        var merged = new Dictionary<string, string>(rec);
                        foreach (var kv in param)
                            merged[kv.Key] = kv.Value;

                        if (!toSend.ContainsKey(key))
                            toSend[key] = new List<Dictionary<string, string>>();

                        log.Debug($"STEP 07 : allowed for Send {key} with type {alarmType}");
                        toSend[key].Add(merged);

                        SkipCombination:
                        continue;
                    }
                }
            }

            return toSend;
        }

        private static void CopyFilefromWebIntoActive(string sourceDir)
        {
            // Récupère tous les chemins de fichiers du répertoire source
            string[] files = Directory.GetFiles(sourceDir);
            string DestinationCsvFile = Properties.Settings.Default.CSVFilesPath;
            
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
    }
}
