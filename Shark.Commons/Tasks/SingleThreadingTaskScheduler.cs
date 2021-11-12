using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

namespace Shark.Tasks
{
    public class SingleThreadingScheduler : TaskScheduler, IDisposable
    {
        private bool disposedValue;
        private Thread worker;
        private ManualResetEvent stopEvent;
        private ManualResetEvent resumeEvent;

        private LinkedList<Task> tasks;

        public SingleThreadingScheduler()
        {
            tasks = new LinkedList<Task>();
            stopEvent = new(false);
            resumeEvent = new(false);
            worker = new Thread(Runner);
            worker.Start(new WaitHandle[] { stopEvent, resumeEvent });
        }

        protected override IEnumerable<Task> GetScheduledTasks()
        {
            bool lockTaken = false;
            try
            {
                Monitor.TryEnter(tasks, ref lockTaken);
                if (lockTaken) return tasks;
                else throw new NotSupportedException();
            }
            finally
            {
                if (lockTaken) Monitor.Exit(tasks);
            }
        }

        protected override void QueueTask(Task task)
        {
            lock (tasks)
            {
                tasks.AddLast(task);
            }
            resumeEvent.Set();
        }

        protected override bool TryExecuteTaskInline(Task task, bool taskWasPreviouslyQueued)
        {
            return false;
        }

        private void Runner(object state)
        {
            var events = state as WaitHandle[];
            while (true)
            {
                var idx = WaitHandle.WaitAny(events);
                if (idx == 0)
                {
                    break;
                }
                lock (tasks)
                {
                    if (tasks.Count > 0)
                    {
                        var item = tasks.First.Value;
                        tasks.RemoveFirst();
                        TryExecuteTask(item);
                    }
                    if (tasks.Count == 0)
                    {
                        resumeEvent.Reset();
                    }
                }
            }
        }

        protected virtual void Dispose(bool disposing)
        {
            if (!disposedValue)
            {
                if (disposing)
                {
                    // TODO: dispose managed state (managed objects)
                }

                // TODO: free unmanaged resources (unmanaged objects) and override finalizer
                // TODO: set large fields to null
                stopEvent.Set();
                worker.Join();
                stopEvent.Dispose();
                resumeEvent.Dispose();

                stopEvent = null;
                resumeEvent = null;
                worker = null;
                tasks = null;

                disposedValue = true;
            }
        }

        public void Dispose()
        {
            // Do not change this code. Put cleanup code in 'Dispose(bool disposing)' method
            Dispose(disposing: true);
            GC.SuppressFinalize(this);
        }
    }
}
