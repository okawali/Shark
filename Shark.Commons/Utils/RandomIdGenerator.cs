using System;

namespace Shark.Utils
{
    public static class RandomIdGenerator
    {
        private static readonly Random _rand;

        static RandomIdGenerator()
        {
            _rand = new Random();
        }

        public static int NewId() => _rand.Next();
    }
}
