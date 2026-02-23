using System;
using System.Collections.Generic;
using System.IO;

namespace Qognify.Processing
{
    public static class BuildToSend
    {
        public static Dictionary<string, List<Dictionary<string, string>>> Build(
            List<Dictionary<string, string>> events,
            Dictionary<string, double> lastSentTimes,
            string csvListKeynameActionPath,
            string baseDir)
        {
            var filterMap = FilterLoader.LoadFilterCsv(csvListKeynameActionPath);
            var alarmTypeCache = new Dictionary<string, HashSet<string>>(StringComparer.OrdinalIgnoreCase);

            var toSend = new Dictionary<string, List<Dictionary<string, string>>>(StringComparer.OrdinalIgnoreCase);

            foreach (var rec in events)
            {
                string key = rec["Keyname"];

                if (!filterMap.ContainsKey(key))
                    continue;

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

            return toSend;
        }
    }
}
