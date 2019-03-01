using Shark.Data;
using System;
using System.Collections.Generic;
using System.Text;

namespace Shark.Options
{
    public class ProxyRemoteOptions
    {
        public HostData Remote { set; get; }
        public int MaxClientCount { set; get; }
    }
}
