using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;

namespace Qognify.Processing
{
    public static class FilterLoader
    {
        public static Dictionary<string, List<Dictionary<string, string>>> LoadFilterCsv(string path)
        {
            var map = new Dictionary<string, List<Dictionary<string, string>>>(StringComparer.OrdinalIgnoreCase);

            var lines = File.ReadAllLines(path);
            if (lines.Length <= 1)
                return map;

            var headers = lines[0].Split(',');

            foreach (var line in lines.Skip(1))
            {
                if (string.IsNullOrWhiteSpace(line))
                    continue;

                var cols = line.Split(',');
                var row = new Dictionary<string, string>();

                for (int i = 0; i < headers.Length; i++)
                    row[headers[i].Trim()] = cols[i].Trim();

                string key = row["KEYNAME"];

                if (!map.ContainsKey(key))
                    map[key] = new List<Dictionary<string, string>>();

                map[key].Add(new Dictionary<string, string>
                {
                    { "ALARM-NUMBER", row["ALARM-NUMBER"] },
                    { "PORT-TCP", row["PORT-TCP"] },
                    { "DELAY-RESEND", row["DELAY-RESEND"] },
                    { "CSVFILEALM", row["CSVFILEALM"] }
                });
            }

            return map;
        }

        public static HashSet<string> LoadAlarmTypeCsv(string path)
        {
            var set = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

            var lines = File.ReadAllLines(path);
            foreach (var line in lines.Skip(1))
            {
                if (!string.IsNullOrWhiteSpace(line))
                {
                    var cols = line.Split(',');
                    set.Add(cols[0].Trim());
                }
            }

            return set;
        }
    }
}
