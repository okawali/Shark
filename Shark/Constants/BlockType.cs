﻿namespace Shark.Constants
{
    public static class BlockType
    {
        public const byte HAND_SHAKE = 0;
        public const byte HAND_SHAKE_RESPONSE = 1;
        public const byte HAND_SHAKE_FINAL = 2;
        public const byte CONNECT = 3;
        public const byte CONNECTED = 4;
        public const byte REQUEST_RESEND = 5;
        public const byte DATA = 6;
        public const byte DISCONNECT = 7;
        public const byte FAST_CONNECT = 0xA0;
        public const byte CONNECT_FAILED = 0xF0;
        public const byte INVALID = 0xFF;
    }
}
