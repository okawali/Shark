namespace Shark.Data
{
    public class HostData
    {
        public string Address { set; get; }
        public ushort Port { set; get; }

        public override string ToString() => $"{Address}:{Port}";
    }
}
