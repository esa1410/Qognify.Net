using NLog;
using System;
using System.Collections.Generic;

namespace Qognify.Processing
{
    public static class FixedWidthParser
    {
        private static readonly Logger log = Logging.LoggerFactory.GetLogger<EventProcessor>();
        public static List<Dictionary<string, string>> Parse(
            List<string> lines,
            List<Tuple<string, int>> fields)
        {
            var records = new List<Dictionary<string, string>>();

            foreach (var line in lines)
            {
                var record = new Dictionary<string, string>();
                int pos = 0;
                log.Debug($"FixedWidthParser 01 : {line}");
                //ddm try catch sur la ligne courante et ne pas négliger le reste du buffer
                try
                {
                    foreach (var field in fields)
                    {
                        string name = field.Item1;
                        int width = field.Item2;

                        string raw = (pos + width <= line.Length)
                            ? line.Substring(pos, width)
                            : line.Substring(pos);

                        record[name] = raw.Trim();
                        pos += width;
                    }
                    records.Add(record);

                }
                catch (Exception ex)
                {

                    log.Error($"Error during filter parser line: {line} message: {ex.Message}");
                }


            }

            return records;
        }
    }
}
