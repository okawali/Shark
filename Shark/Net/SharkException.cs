using System;

namespace Shark.Net
{
    [Serializable]
    public class SharkException : Exception
    {
        public SharkException() { }
        public SharkException(string message) : base(message) { }
        public SharkException(string message, Exception inner) : base(message, inner) { }
        protected SharkException(
          System.Runtime.Serialization.SerializationInfo info,
          System.Runtime.Serialization.StreamingContext context) : base(info, context) { }
    }
}
