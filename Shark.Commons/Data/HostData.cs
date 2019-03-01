namespace Shark.Data
{
    public enum RemoteType : byte
    {
        Tcp = 0,
        Udp = 1
    }

    public class HostData
    {
        public string Address { set; get; }
        public ushort Port { set; get; }
        public RemoteType Type { set; get; }

        public bool IsUdp => Type == RemoteType.Udp;

        public override string ToString() => $"{Address}:{Port}/{Type}";
    }
}
