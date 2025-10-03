using Microsoft.Extensions.Logging;
using SubtitlesParserV2.Formats.Parsers;
using SubtitlesParserV2.Helpers;
using SubtitlesParserV2.Logger;
using SubtitlesParserV2.Models;
using System;
using System.Collections.Generic;
using System.IO;
using System.Runtime.ExceptionServices;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

namespace SubtitlesParserV2
{
    /// <summary>
    /// This class implement the public exposed methods to parse a subtitle file and interact with the parsers instances.
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
		/// you, when you use their instance directly, to use the type <see cref="ISubtitlesParserWithConfig{TConfig}"/> and have your own
		/// configuration. You can get a specific instance by using <see cref="SubtitleFormat.GetFormat(SubtitleFormatType)"/>.
		/// </remarks>
		/// <param name="stream">The subtitle stream</param>
		/// <returns>The corresponding list of SubtitleItem, null if parsing failed</returns>
		/// <exception cref="ArgumentException"></exception>
		public static SubtitleParserResultModel ParseStream(Stream stream)
		{
			// we default encoding to UTF-8
			return ParseStream(stream, Encoding.UTF8);
		}

		/// <summary>
		/// Try to parse a subtitle stream with all of the available parsers unless a specific <see cref="SubtitleFormatType"/> is specified.
		/// </summary>
		/// <remarks>
		/// All parsers will use the default configuration (of type <see cref="ISubtitlesParser"/>), some parsers also allow
		/// you, when you use their instance directly, to use the type <see cref="ISubtitlesParserWithConfig{TConfig}"/> and have your own
		/// configuration. You can get a specific instance by using <see cref="SubtitleFormat.GetFormat(SubtitleFormatType)"/>.
		/// </remarks>
		/// <param name="stream">The stream</param>
		/// <param name="encoding">The encoding</param>
		/// <param name="selectedFormat">If specified, will only try the selected parsers.</param>
		/// <returns>The corresponding list of SubtitleItem, null if parsing failed</returns>
		/// <exception cref="ArgumentException"></exception>
		public static SubtitleParserResultModel ParseStream(Stream stream, Encoding encoding, SubtitleFormatType selectedFormat)
		{
			return ParseStream(stream, encoding, new SubtitleFormatType[] { selectedFormat });
		}

		/// <summary>
		/// Try to parse a subtitle stream with all of the available parsers unless a specific <see cref="SubtitleFormatType"/> is specified.
		/// </summary>
		/// <remarks>
		/// All parsers will use the default configuration (of type <see cref="ISubtitlesParser"/>), some parsers also allow
		/// you, when you use their instance directly, to use the type <see cref="ISubtitlesParserWithConfig{TConfig}"/> and have your own
		/// configuration. You can get a specific instance by using <see cref="SubtitleFormat.GetFormat(SubtitleFormatType)"/>.
		/// </remarks>
		/// <param name="stream">The stream</param>
		/// <param name="encoding">The encoding</param>
		/// <param name="selectedFormats">If specified, will only try the selected parsers.</param>
		/// <returns>The corresponding list of SubtitleItem, null if parsing failed</returns>
		/// <exception cref="ArgumentException"></exception>
		public static SubtitleParserResultModel ParseStream(Stream stream, Encoding encoding, IEnumerable<SubtitleFormatType>? selectedFormats = null)
		{
			// test if stream if readable
			var seekableStream = PrepareStream(stream, out var wasStreamCopied);

			IEnumerable<SubtitleFormat> subtitleFormatToRun = GetFormatsToRun(selectedFormats);
			//replace ignoreException with delayed throwing.
			ExceptionDispatchInfo lastException = null;
			foreach (SubtitleFormat subtitleFormat in subtitleFormatToRun)
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
						seekableStream.Dispose();
					}
					return new SubtitleParserResultModel(subtitleFormat.FormatType,items); // end method execution
				}
				catch (Exception e) // If ignoreException is true, we ignore it and try the next parser
				{
					lastException = ExceptionDispatchInfo.Capture(e);
				}
			}

			// We only reach this part of the code if all parsers failed
			if (wasStreamCopied)
			{
				// Ensure the stream copy is disposed
				seekableStream.Dispose();
			}
			lastException?.Throw();
			throw null;
		}



		/// <summary>
		/// Try each subtitle format to parse the subtitle, read the stream with UTF-8 decoding.
		/// </summary>
		/// <param name="stream">Subtitle content to parse</param>
		/// <param name="cancellationToken">Cancellation token to cancel the operation.</param>
		/// <returns>Parsed model, or <code>null</code> if the <code>ignoreException</code> parameter was <code>true</code></returns>
		/// <exception cref="ArgumentException">Stream cannot be read</exception>
		/// <exception cref="FormatException">Subtitle content is in invalid format.</exception>
		public static Task<SubtitleParserResultModel> ParseStreamAsync(Stream stream, CancellationToken cancellationToken = default) =>
			ParseStreamAsync(stream, Encoding.UTF8, cancellationToken: cancellationToken);

		/// <summary>
		/// Parse the subtitle with the <code>encoding</code> and <code>selectedFormat</code>.
		/// </summary>
		/// <param name="stream">Subtitle content to parse</param>
		/// <param name="encoding">encoding to read the <code>stream</code></param>
		/// <param name="selectedFormat">format to parse</param>
		/// <param name="cancellationToken">Cancellation token to cancel the operation.</param>
		/// <returns>Parsed model, or <code>null</code> if the <code>ignoreException</code> parameter was <code>true</code></returns>
		/// <exception cref="ArgumentException">Stream cannot be read</exception>
		/// <exception cref="FormatException">Subtitle content is in invalid format.</exception>
		public static Task<SubtitleParserResultModel> ParseStreamAsync(Stream stream, Encoding encoding, SubtitleFormatType selectedFormat, CancellationToken cancellationToken = default) =>
			ParseStreamAsync(stream, encoding, new[] { selectedFormat },  cancellationToken);

		/// <summary>
		/// Parse the subtitle content by trying each format, with the specified <code>encoding</code>.
		/// </summary>
		/// <param name="stream">Subtitle content to parse</param>
		/// <param name="encoding">encoding to read the <code>stream</code></param>
		/// <param name="selectedFormats">formats to parse</param>
		/// <param name="cancellationToken">Cancellation token to cancel the operation.</param>
		/// <returns>Parsed model, or <code>null</code> if the <code>ignoreException</code> parameter was <code>true</code></returns>
		/// <exception cref="ArgumentException">Stream cannot be read</exception>
		/// <exception cref="FormatException">Subtitle content is in invalid format.</exception>
		public static async Task<SubtitleParserResultModel> ParseStreamAsync(Stream stream, Encoding encoding, IEnumerable<SubtitleFormatType>? selectedFormats = null, CancellationToken cancellationToken = default)
		{
			// test if stream if readable
			var seekableStream = PrepareStream(stream, out var wasStreamCopied);

			// By default, we run all of the available formats
			IEnumerable<SubtitleFormat> subtitleFormatToRun = GetFormatsToRun(selectedFormats);
			ExceptionDispatchInfo lastException = null;
			foreach (SubtitleFormat subtitleFormat in subtitleFormatToRun)
			{
				try
				{
					ISubtitlesParser parser = subtitleFormat.ParserInstance;
					_logger.LogDebug("Parsing with : {name}", Enum.GetName(typeof(SubtitleFormatType), subtitleFormat.FormatType));
					List<SubtitleModel> items = await parser.ParseStreamAsync(seekableStream, encoding, cancellationToken);
					// Pass this point, if a error wasn't thrown, the right parser was used
					if (wasStreamCopied)
					{
						// Ensure the stream copy is disposed
						await seekableStream.DisposeAsync();
					}
					return new SubtitleParserResultModel(subtitleFormat.FormatType,items); // end method execution
				}
				catch (Exception e) // If ignoreException is true, we ignore it and try the next parser
				{
					lastException = ExceptionDispatchInfo.Capture(e);
				}
			}

			// We only reach this part of the code if all parsers failed
			if (wasStreamCopied)
			{
				// Ensure the stream copy is disposed
				await seekableStream.DisposeAsync();
			}

			lastException?.Throw();
			throw null;
		}
		/// <summary>
		/// Get a consuming parser from the subtitle content, to read and process the subtitle items in a single iteration. Use UTF-8 encoding and try to parse with all subtitle formats.
		/// </summary>
		/// <remarks>It is recommended to use the overload with <c>selectedFormats</c> parameter as this overload tries every format to parse with. You should not use the <c>stream</c> while this enumerable is being consumed.</remarks>
		/// <param name="stream">Subtitle content</param>
		/// <returns>Consuming enumerable to read subtitle items.</returns>
		/// <exception cref="FormatException">Content cannot be parsed.</exception>
		public static IEnumerable<SubtitleModel> GetConsumingParser(Stream stream) =>
			GetConsumingParser(stream, Encoding.UTF8);
		/// <summary>
		/// Get a consuming parser from the subtitle content, to read and process the subtitle items in a single iteration. Use UTF-8 encoding and try to parse with all subtitle formats.
		/// </summary>
		/// <remarks>You should not use the <c>stream</c> while this enumerable is being consumed.</remarks>
		/// <param name="stream">Subtitle content</param>
		/// <param name="encoding">Encoding to decode the <c>stream</c></param>
		/// <param name="selectedFormats">Formats to parse with.</param>
		/// <returns>Consuming enumerable to read subtitle items.</returns>
		/// <exception cref="FormatException">Content cannot be parsed.</exception>
		public static IEnumerable<SubtitleModel> GetConsumingParser(Stream stream, Encoding encoding,
			IEnumerable<SubtitleFormatType>? selectedFormats = null)
		{
			stream = PrepareStream(stream, out bool streamCopied);
			var formatsToRun = GetFormatsToRun(selectedFormats);
			ExceptionDispatchInfo lastException = null;
			foreach (var subtitleFormat in formatsToRun)
			{
				try
				{
					ISubtitlesParser parser = subtitleFormat.ParserInstance;
					_logger.LogDebug("Parsing with : {name}", Enum.GetName(typeof(SubtitleFormatType), subtitleFormat.FormatType));
					var result = parser.ParseStreamConsuming(stream, encoding);
					// Pass this point, if a error wasn't thrown, the right parser was used
					if (streamCopied)
					{
						// Ensure the stream copy is disposed
						stream.Dispose();
					}
					return result;
				}
				catch (Exception e)
				{
					lastException = ExceptionDispatchInfo.Capture(e);
				}
			}

			if (streamCopied)
			{
				stream.Dispose();
			}
			lastException.Throw();
			throw null;
		}
		/// <summary>
		/// Get a asynchronous consuming parser from the subtitle content, to read and process the subtitle items in a single iteration asynchronously. Use UTF-8 encoding and try to parse with all subtitle formats.
		/// </summary>
		/// <remarks>It is recommended to use the overload with <c>selectedFormats</c> parameter as this overload tries every format to parse with. You should not use the <c>stream</c> while this enumerable is being consumed.</remarks>
		/// <param name="stream">Subtitle content</param>
		/// <param name="encoding">Encoding to decode the <c>stream</c></param>
		/// <param name="selectedFormats">Formats to parse with.</param>
		/// <param name="cancellationToken">Token to cancel the operation.</param>
		/// <returns>Consuming enumerable to read subtitle items.</returns>
		/// <exception cref="FormatException">Content cannot be parsed.</exception>
		public static IAsyncEnumerable<SubtitleModel> GetAsyncConsumingParser(Stream stream, CancellationToken cancellationToken = default)=>
		GetAsyncConsumingParser(stream, Encoding.UTF8, cancellationToken:cancellationToken);

		/// <summary>
		/// Get an asynchronous consuming parser from the subtitle content, to read and process the subtitle items in a single iteration asynchronously. Use UTF-8 encoding and try to parse with all subtitle formats.
		/// </summary>
		/// <remarks>You should not use the <c>stream</c> while this enumerable is being consumed.</remarks>
		/// <param name="stream">Subtitle content</param>
		/// <param name="encoding">Encoding to decode the <c>stream</c></param>
		/// <param name="selectedFormats">Formats to parse with.</param>
		/// <param name="cancellationToken">Token to cancel the operation.</param>
		/// <returns>Consuming enumerable to read subtitle items.</returns>
		/// <exception cref="FormatException">Content cannot be parsed.</exception>
		public static IAsyncEnumerable<SubtitleModel> GetAsyncConsumingParser(Stream stream, Encoding encoding,
			IEnumerable<SubtitleFormatType>? selectedFormats = null, CancellationToken cancellationToken = default)
		{
			stream =  PrepareStream(stream, out bool streamCopied);
			var formatsToRun = GetFormatsToRun(selectedFormats);
			ExceptionDispatchInfo lastException = null;
			foreach (var subtitleFormat in formatsToRun)
			{
				try
				{
					ISubtitlesParser parser = subtitleFormat.ParserInstance;
					_logger.LogDebug("Parsing with : {name}", Enum.GetName(typeof(SubtitleFormatType), subtitleFormat.FormatType));
					var result = parser.ParseStreamConsumingAsync(stream, encoding, cancellationToken);
					// Pass this point, if a error wasn't thrown, the right parser was used

					if (streamCopied)
					{
						// Ensure the stream copy is disposed
						stream.Dispose();
					}
					return result;
				}
				catch (Exception e)
				{
					lastException = ExceptionDispatchInfo.Capture(e);
				}
			}
			if(streamCopied)
				stream.Dispose();
			lastException.Throw();
			throw null;
		}
		private static IEnumerable<SubtitleFormat> GetFormatsToRun(IEnumerable<SubtitleFormatType>? selectedFormats)
		{
			// By default, we run all of the available formats
			IEnumerable<SubtitleFormat> SubtitleFormatToRun;
			// If a specific format was specified, we only use the specified format
			if (selectedFormats != null)
			{
				SubtitleFormatToRun = SubtitleFormat.GetFormat(selectedFormats);
			}
			else SubtitleFormatToRun = SubtitleFormat.AllFormats; // Set all available formats

			return SubtitleFormatToRun;
		}
		private static Stream PrepareStream(Stream stream, out bool wasStreamCopied)
		{
			if (!stream.CanRead)
			{
				throw new ArgumentException("Cannot parse a non-readable stream");
			}
			Stream seekableStream = stream;
			wasStreamCopied = false;
			if (!stream.CanSeek) // Copy the stream if not seekable
			{
				seekableStream = StreamHelper.CopyStream(stream);
				seekableStream.Seek(0, SeekOrigin.Begin);
				wasStreamCopied = true;
			}

			return seekableStream;
		}
	}
}
