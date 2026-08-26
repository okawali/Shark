using System;

namespace Shark.Constants
{
    public static class SharkDefaults
    {
#if DEBUG
        public static readonly TimeSpan MaxWaitTime = TimeSpan.FromSeconds(10);
#else
        public static readonly TimeSpan MaxWaitTime = TimeSpan.FromSeconds(60);
#endif

        public const int MaximumBlockLength = 16 * 1024 * 1024;
    }
}
