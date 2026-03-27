using System.Collections.Concurrent;

namespace Qognify.Processing
{
    public static class SystemEvent
    {
        public static void EnqueueEvent(
            ConcurrentQueue<OutgoingEvent> queue,
            string keyname,
            string alarmNumber,
            int port= -1)
        {
            if (port == -1)
                port = Properties.Settings.Default.TcpPortSystem; 
            
            foreach (var evt in queue)
            {
                if (evt.Keyname == keyname &&
                    evt.AlarmNumber == alarmNumber &&
                    evt.Port == port)
                {
                    return;
                }
            }

            queue.Enqueue(new OutgoingEvent
            {
                Keyname = keyname,
                AlarmNumber = alarmNumber,
                Port = port
            });
        }
    }
}