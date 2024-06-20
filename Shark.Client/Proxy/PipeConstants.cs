using System.IO.Pipelines;

namespace Shark.Client.Proxy
{
    internal static class PipeConstants
    {
        public const int BUFFER_SIZE = 1024 * 1024 * 4;
        public const int PAUSE_WRITER_SIZE = 64 * 1024 * 1024; // 16M;
        public const int RESUME_WRITER_SIZE = 64 * 1024 * 1024; // 8M;

        public static PipeOptions DefaultPipeOptions =>
            new PipeOptions(minimumSegmentSize: BUFFER_SIZE, pauseWriterThreshold: PAUSE_WRITER_SIZE, resumeWriterThreshold: RESUME_WRITER_SIZE);
    }
}
