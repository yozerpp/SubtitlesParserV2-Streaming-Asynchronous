using Microsoft.Extensions.Logging;
using SubtitlesParserV2.Formats.Parsers;
using SubtitlesParserV2.Helpers;
using SubtitlesParserV2.Logger;
using SubtitlesParserV2.Models;
using System;
using System.Collections.Generic;
using System.IO;
using System.Text;

namespace SubtitlesParserV2
{
    /// <summary>
    /// This class implement the public exposed methods to parse a subtitle file
    /// </summary>
    public static class SubtitleParser
	{
		private static readonly Type CurrentType = typeof(SubtitleParser);
		// Alternative for static class, create a logger with the full namespace name
		private static readonly ILogger _logger = LoggerManager.GetLogger(CurrentType.FullName ?? CurrentType.Name);

		/// <summary>
		/// Try to parse a subtitle stream with all of the available parsers with the default configuration (<see cref="ISubtitlesParser"/>).
		/// </summary>
		/// <remarks>
		/// All parsers will use the default configuration (of type <see cref="ISubtitlesParser"/>), some parsers also allow
		/// you, when you use their instance directly, to use the type <see cref="ISubtitlesParser{TConfig}"/> and have your own
		/// configuration. You can get a specific instance by using <see cref="SubtitleFormat.GetFormat(SubtitleFormatType)"/>.
		/// </remarks>
		/// <param name="stream">The subtitle stream</param>
		/// <returns>The corresponding list of SubtitleItem, null if parsing failed</returns>
		/// <exception cref="ArgumentException"></exception>
		public static SubtitleParserResultModel? ParseStream(Stream stream)
		{
			// we default encoding to UTF-8
			return ParseStream(stream, Encoding.UTF8);
		}

		/// <summary>
		/// Try to parse a subtitle stream with all of the available parsers unless a specific <see cref="SubtitleFormatType"/> is specified.
		/// </summary>
		/// <remarks>
		/// All parsers will use the default configuration (of type <see cref="ISubtitlesParser"/>), some parsers also allow
		/// you, when you use their instance directly, to use the type <see cref="ISubtitlesParser{TConfig}"/> and have your own
		/// configuration. You can get a specific instance by using <see cref="SubtitleFormat.GetFormat(SubtitleFormatType)"/>.
		/// </remarks>
		/// <param name="stream">The stream</param>
		/// <param name="encoding">The encoding</param>
		/// <param name="selectedFormat">If specified, will only try the selected parsers.</param>
		/// <param name="ignoreException">If true (default), will ignore parsers exceptions and continue to the next one. If false, exception will be thrown,</param>
		/// <returns>The corresponding list of SubtitleItem, null if parsing failed</returns>
		/// <exception cref="ArgumentException"></exception>
		public static SubtitleParserResultModel? ParseStream(Stream stream, Encoding encoding, SubtitleFormatType selectedFormat, bool? ignoreException = true)
		{
			return ParseStream(stream, encoding, new SubtitleFormatType[] { selectedFormat }, ignoreException ?? true);
		}

		/// <summary>
		/// Try to parse a subtitle stream with all of the available parsers unless a specific <see cref="SubtitleFormatType"/> is specified.
		/// </summary>
		/// <remarks>
		/// All parsers will use the default configuration (of type <see cref="ISubtitlesParser"/>), some parsers also allow
		/// you, when you use their instance directly, to use the type <see cref="ISubtitlesParser{TConfig}"/> and have your own
		/// configuration. You can get a specific instance by using <see cref="SubtitleFormat.GetFormat(SubtitleFormatType)"/>.
		/// </remarks>
		/// <param name="stream">The stream</param>
		/// <param name="encoding">The encoding</param>
		/// <param name="selectedFormats">If specified, will only try the selected parsers.</param>
		/// <param name="ignoreException">If true (default), will ignore parsers exceptions and continue to the next one. If false, exception will be thrown,</param>
		/// <returns>The corresponding list of SubtitleItem, null if parsing failed</returns>
		/// <exception cref="ArgumentException"></exception>
		public static SubtitleParserResultModel? ParseStream(Stream stream, Encoding encoding, IEnumerable<SubtitleFormatType>? selectedFormats = null, bool ignoreException = true)
		{
			// test if stream if readable
			if (!stream.CanRead)
			{
				throw new ArgumentException("Cannot parse a non-readable stream");
			}

			Stream seekableStream = stream;
			bool wasStreamCopied = false;
			if (!stream.CanSeek) // Copy the stream if not seekable
			{
				seekableStream = StreamHelper.CopyStream(stream);
				seekableStream.Seek(0, SeekOrigin.Begin);
				wasStreamCopied = true;
			}

			// By default, we run all of the available formats
			IEnumerable<SubtitleFormat> SubtitleFormatToRun;
			// If a specific format was specified, we only use the specified format
			if (selectedFormats != null)
			{
				SubtitleFormatToRun = SubtitleFormat.GetFormat(selectedFormats);
			}
			else SubtitleFormatToRun = SubtitleFormat.AllFormats; // Set all available formats

			foreach (SubtitleFormat subtitleFormat in SubtitleFormatToRun)
			{
				try
				{
					ISubtitlesParser parser = subtitleFormat.ParserInstance;
					_logger.LogDebug("Parsing with : {name}", Enum.GetName(typeof(SubtitleFormatType), subtitleFormat.FormatType));
					List<SubtitleModel> items = parser.ParseStream(seekableStream, encoding);
					// Pass this point, if a error wasn't thrown, the right parser was used
					if (wasStreamCopied)
					{
						// Ensure the stream copy is disposed
						seekableStream?.Dispose();
					}
					return new SubtitleParserResultModel(subtitleFormat.FormatType,items); // end method execution
				}
				catch when (ignoreException) // If ignoreException is true, we ignore it and try the next parser
				{
					continue; // Let's try the next parser...
				}
			}

			// We only reach this part of the code if all parsers failed
			if (wasStreamCopied)
			{
				// Ensure the stream copy is disposed
				seekableStream?.Dispose();
			}
			return null;
		}
	}
}
