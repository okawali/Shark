using System.Net;

namespace Shark.Options
{
    public class BindingOptions
    {
        public IPEndPoint EndPoint { set; get; }
        public int Backlog { set; get; }
    }
}
