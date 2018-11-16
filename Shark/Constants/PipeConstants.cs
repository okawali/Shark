using System.IO.Pipelines;

namespace Shark.Constants
{
    internal static class PipeConstants
    {
        public const int BUFFER_SIZE = 8192;
        public const int PAUSE_WRITER_SIZE = 16 * 1024 * 1024; // 16M;
        public const int RESUME_WRITER_SIZE = 8 * 1024 * 1024; // 8M;

        public static PipeOptions DefaultPipeOptions =>
            new PipeOptions(minimumSegmentSize: BUFFER_SIZE, pauseWriterThreshold: PAUSE_WRITER_SIZE, resumeWriterThreshold: RESUME_WRITER_SIZE);
    }
}
