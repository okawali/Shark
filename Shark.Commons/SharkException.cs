using System;

namespace Shark
{
    [Serializable]
    public class SharkException : Exception
    {
        public SharkException() { }
        public SharkException(string message) : base(message) { }
        public SharkException(string message, Exception inner) : base(message, inner) { }
    }
}
