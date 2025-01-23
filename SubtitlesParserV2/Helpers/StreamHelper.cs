using System;
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

        /// <summary>
        /// Method that verify if a given stream is readable and seekable.
        /// Throw a exception if it is not readable or seekable.
        /// </summary>
        /// <param name="stream">The stream to verify</param>
        /// <exception cref="ArgumentException"></exception>
        public static void ThrowIfStreamIsNotSeekableOrReadable(Stream stream) 
        {
			// test if stream if readable and seekable (just a check, should be good)
			if (!stream.CanRead || !stream.CanSeek)
			{
				throw new ArgumentException($"Stream must be seekable and readable in a subtitles parser. Operation interrupted; isSeekable: {samiStream.CanSeek} - isReadable: {samiStream.CanRead}");
			}
		}
    }
}
