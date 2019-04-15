namespace Shark.Constants
{
    public static class BlockType
    {
        public const byte HAND_SHAKE = 0;
        public const byte HAND_SHAKE_RESPONSE = 1;
        //public const byte HAND_SHAKE_FINAL = 2; deprecated!
        public const byte CONNECT = 2;
        public const byte CONNECTED = 3;
        //public const byte REQUEST_RESEND = 4; deprecated!
        public const byte DATA = 4;
        public const byte DISCONNECT = 5;
        public const byte FAST_CONNECT = 0xA0;
        public const byte CONNECT_FAILED = 0xF0;
        public const byte INVALID = 0xFF;
    }
}
