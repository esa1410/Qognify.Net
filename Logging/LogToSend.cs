using NLog;
using System;
using System.Collections.Generic;


namespace Qognify.Logging
{
    public class LogToSend
    {
        private static readonly Logger log = LoggerFactory.GetLogger<LogToSend>();
        public static void Dump(Dictionary<string, List<Dictionary<string, string>>> toSend)
        {
            if (toSend == null || toSend.Count == 0)
            {
#if debug
                Console.WriteLine("ToSend is empty â€” no alarms to send.");
# endif
                //todo envoyer vers nlog
                //log.Info("ToSend is empty â€” no alarms to send.");
                return;
            }

            foreach (var kv in toSend)
            {
                string keyname = kv.Key;
                var records = kv.Value;

                Console.WriteLine($"Keyname: {keyname} -> {records.Count} record(s)");
                log.Info($"Keyname: {keyname} -> {records.Count} record(s)");


                int idx = 1;
                foreach (var rec in records)
                {
                    Console.WriteLine($"  [{idx}] DT={rec["DateTime"]}, ALARM={rec["ALARM-NUMBER"]}, PORT={rec["PORT-TCP"]}, DELAY={rec["DELAY-RESEND"]}, CSVFILEALM={rec["CSVFILEALM"]}");
                    log.Info($"  [{idx}] ALARM={rec["ALARM-NUMBER"]}, PORT={rec["PORT-TCP"]}, DELAY={rec["DELAY-RESEND"]}, CSVFILEALM={rec["CSVFILEALM"]}");
                    idx++;
                }
            }
        }
    }
}
