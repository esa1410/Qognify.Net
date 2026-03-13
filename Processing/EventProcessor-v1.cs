using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Threading;

namespace Qognify.Processing
{
    public class EventProcessor
    {
        private readonly ConcurrentQueue<string> _queue;
        private readonly TimeSpan _interval;
        private readonly CancellationToken _ct;

        public EventProcessor(
            ConcurrentQueue<string> queue,
            TimeSpan interval,
            CancellationToken ct)
        {
            _queue = queue;
            _interval = interval;
            _ct = ct;
        }

        public void Start()
        {
            ThreadPool.QueueUserWorkItem(_ => Loop());
        }

        private void Loop()
        {
            var last = DateTime.UtcNow;

            while (!_ct.IsCancellationRequested)
            {
                var now = DateTime.UtcNow;

                if ((now - last) >= _interval)
                {
                    var batch = DequeueAll();

                    if (batch.Count > 0)
                    {
                        // TODO: parse_fixed_width, build_to_send, log_tosend
                    }

                    last = now;
                }

                Thread.Sleep(100);
            }
        }

        private List<string> DequeueAll()
        {
            var list = new List<string>();
            while (_queue.TryDequeue(out var item))
                list.Add(item);
            return list;
        }
    }
}
