using System.IO;

namespace SubtitlesParserV2.Helpers
{
    static class StreamHelper
    {
        /// <summary>
        /// Copies a stream to another stream.
        /// This method is useful in particular when the inputStream is not seekable.
        /// </summary>
        /// <param name="inputStream">The stream to copy</param>
        /// <returns>A copy of the input Stream</returns>
        public static Stream CopyStream(Stream inputStream)
        {
            MemoryStream outputStream = new MemoryStream();
            int count;
            // Create a buffer to read by chunks
            byte[] buffer = new byte[1024];
            do
            {
                // Read a chunk from the stream
                count = inputStream.Read(buffer, 0, buffer.Length);
                if (count > 0)
                {
                    outputStream.Write(buffer, 0, count);
                }
            } while (count > 0);  // Continue until end of stream

            // Reset the position of the MemoryStream to the start
            outputStream.Position = 0;

            return outputStream;
        }
    }
}
