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
            string FlagFile = baseDir + @"\flag";
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

                if (!filterMap.ContainsKey(key))
                    continue;
                //EventProcessor.log.Info()


                //ddm ignore receive with ACK and OK
                if (rec["Ack"].Trim().Length == 0)
                {



                    foreach (var param in filterMap[key])
                    {
                        string alarmNumber = param["ALARM-NUMBER"];
                        string port = param["PORT-TCP"];
                        int delay = int.Parse(param["DELAY-RESEND"]);
                        string csvAlm = param["CSVFILEALM"];
                        string alarmType = rec["AlarmType"];

                        string uniqueKey = $"{key}:{alarmNumber}:{port}";

                        if (toSend.ContainsKey(key))
                        {
                            foreach (var existing in toSend[key])
                            {
                                string existingKey = $"{key}:{existing["ALARM-NUMBER"]}:{existing["PORT-TCP"]}";
                                if (existingKey.Equals(uniqueKey, StringComparison.OrdinalIgnoreCase))
                                    goto SkipCombination;
                            }
                        }

                        //to do 
                        if (!alarmTypeCache.ContainsKey(csvAlm))
                        {
                            string path = Path.Combine(baseDir, csvAlm + ".csv");
                            alarmTypeCache[csvAlm] = FilterLoader.LoadAlarmTypeCsv(path);
                        }

                        if (!alarmTypeCache[csvAlm].Contains(alarmType))
                            goto SkipCombination;

                        if (delay > 0)
                        {
                            double now = DateTimeOffset.UtcNow.ToUnixTimeSeconds();
                            double last = lastSentTimes.ContainsKey(uniqueKey) ? lastSentTimes[uniqueKey] : 0;
                            double elapsed = now - last;

                            if (elapsed < delay)
                                goto SkipCombination;

                            lastSentTimes[uniqueKey] = now;
                        }

                        var merged = new Dictionary<string, string>(rec);
                        foreach (var kv in param)
                            merged[kv.Key] = kv.Value;

                        if (!toSend.ContainsKey(key))
                            toSend[key] = new List<Dictionary<string, string>>();

                        toSend[key].Add(merged);

                        SkipCombination:
                        continue;
                    }
                }
            }

            return toSend;
        }
    }
}
