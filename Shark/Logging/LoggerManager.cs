using Microsoft.Extensions.Logging;

namespace Shark.Logging
{
    public static class LoggerManager
    {
        public static ILoggerFactory LoggerFactory { get; private set; }
        
        static LoggerManager()
        {
            LoggerFactory = new LoggerFactory();
        }
    }
}
