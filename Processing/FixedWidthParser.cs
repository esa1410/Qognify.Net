using System;
using System.Collections.Generic;

namespace Qognify.Processing
{
    public static class FixedWidthParser
    {
        public static List<Dictionary<string, string>> Parse(
            List<string> lines,
            List<Tuple<string, int>> fields)
        {
            var records = new List<Dictionary<string, string>>();

            foreach (var line in lines)
            {
                var record = new Dictionary<string, string>();
                int pos = 0;

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

            return records;
        }
    }
}
