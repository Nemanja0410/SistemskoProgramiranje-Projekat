using System.Collections.Generic;
using System.Net;
using System.Threading;

namespace ImageServer
{
    public class RequestQueue
    {
        private readonly Queue<HttpListenerContext> _queue = new Queue<HttpListenerContext>();
        private readonly object _lock = new object();

        public void Enqueue(HttpListenerContext context)
        {
            lock (_lock)
            {
                _queue.Enqueue(context);
                
                Monitor.Pulse(_lock);
            }
        }

        public HttpListenerContext Dequeue()
        {
            lock (_lock)
            {
                
                while (_queue.Count == 0)
                {
                    Monitor.Wait(_lock);
                }
                return _queue.Dequeue();
            }
        }
    }
}