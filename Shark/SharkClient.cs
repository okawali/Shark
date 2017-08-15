using System;
using System.Threading.Tasks;

namespace Shark.Server
{
    abstract class SharkClient : IDisposable
    {
        public Guid Id
        {
            get;
            private set;
        }

        public SharkServer Server
        {
            get;
            private set;
        }

        public bool Disposed
        {
            get;
            protected set;
        }

        public SharkClient(SharkServer server)
        {
            Id = Guid.NewGuid();
            Server = server;
        }

        public abstract Task<int> ReadAsync(byte[] buffer, int offset, int count);
        public abstract Task WriteAsync(byte[] buffer, int offset, int count);
        public abstract Task CloseAsync();
        public abstract void Dispose();
    }
}
