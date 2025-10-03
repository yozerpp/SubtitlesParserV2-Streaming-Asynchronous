using SubtitlesParserV2.Helpers;
using SubtitlesParserV2.Models;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Runtime.CompilerServices;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;

namespace SubtitlesParserV2.Formats.Parsers
{
	/// <summary>
	/// Configuration model for the Lrc parser.
	/// </summary>
	public class LrcParserConfig
	{
		/// <summary>
		/// Define the maximum number of lines the program will continue reading before exiting if it
		/// haven't found any lines in Lrc format. Default is 20, which is usally more than enought to find the first line actual subtitle line.
		/// </summary>
		public int FirstLineSearchTimeout { get; set; } = 20;
	}

	/// <summary>
	/// <para>Parser for the .lrc subtitles files.</para>
	/// <para><strong>Support</strong> : Core LRC, Enhanced LRC format (A2 extension).
	/// <strong>NOTE</strong>: Last item end time will always be -1
	/// </para>
	/// </summary>
	/// 
	/// <!--
	/// Sources:
	/// https://en.wikipedia.org/wiki/LRC_(file_format)
	/// https://docs.fileformat.com/misc/lrc/
	/// Example:
	/// [ar:Artist performing]
	/// [al: Album name]
	/// [ti: Media title]
	/// [au: Artist name]
	/// [length: 0:40]
	/// # This is a comment, line 6 uses Enhanced LRC format
	/// 
	/// [00:12.00] Line 1 lyrics
	/// [00:17.20] Line 2 lyrics
	/// [00:21.10] Line 3 lyrics
	/// [00:24.00] Line 4 lyrics
	/// [00:28.25] Line 5 lyrics
	/// [00:29.02] Line 6 <00:34.20>lyrics
	/// [00:39.00] last lyrics.
	/// -->
	internal class LrcParser : ISubtitlesParserWithConfig<LrcSubtitlePart, LrcParserConfig>
	{
		// Format : [0000:00.00] / [mm:ss.xx]
		private static readonly Regex ShortTimestampRegex = new Regex(@"\[(?<M>\d+):(?<S>\d{2})\.(?<X>\d{2})\]", RegexOptions.Compiled);
		// Format : [0000:00:00.00] / [hh:mm:ss.xx] (Not part of the official docs, but some applications decided to do it that way)
		private static readonly Regex LongTimestampRegex = new Regex(@"\[(?<H>\d+):(?<M>\d+):(?<S>\d{2})\.(?<X>\d{2})\]", RegexOptions.Compiled);
		// Format <00:00.00> used inside the lines by the Enhanced LRC format (A2 Extension)
		private static readonly Regex EnhancedLrcFormatRegex = new Regex(@"<\d{2}:\d{2}\.\d{2}>", RegexOptions.Compiled);

		private const string BadFormatMsg = "Stream is not in a valid Lrc format";

		public List<SubtitleModel> ParseStream(Stream lrcStream, Encoding encoding)
		{
			return ParseStream(lrcStream, encoding, new LrcParserConfig());
		}

		public List<SubtitleModel> ParseStream(Stream lrcStream, Encoding encoding, LrcParserConfig configuration)
		{
			var ret = ParseAsEnumerable(lrcStream, encoding, configuration).ToList();
			if (ret.Count == 0) throw new ArgumentException(BadFormatMsg);
			return ret;
		}

		public async Task<List<SubtitleModel>> ParseStreamAsync(Stream stream, Encoding encoding, CancellationToken cancellationToken)
		{
			var ret = await ParseAsEnumerableAsync(stream, encoding, new LrcParserConfig(), cancellationToken).ToListAsync(cancellationToken);
			if (ret.Count == 0) throw new ArgumentException(BadFormatMsg);
			return ret;
		}

		public IEnumerable<SubtitleModel> ParseAsEnumerable(Stream lrcStream, Encoding encoding)
		{
			return ParseAsEnumerable(lrcStream, encoding, new LrcParserConfig());
		}

		public IEnumerable<SubtitleModel> ParseAsEnumerable(Stream lrcStream, Encoding encoding, LrcParserConfig config)
		{
			StreamHelper.ThrowIfStreamIsNotSeekableOrReadable(lrcStream);
			// seek the beginning of the stream
			lrcStream.Position = 0;

			IEnumerable<LrcSubtitlePart> parts = GetParts(lrcStream, encoding, config).Peekable(out var partsAny);
			if (!partsAny)
				throw new ArgumentException(BadFormatMsg);

			bool first = true;
			foreach (LrcSubtitlePart part in parts)
			{
				yield return ParsePart(part, first, config);
				first = false;
			}
		}

		public async IAsyncEnumerable<SubtitleModel> ParseAsEnumerableAsync(Stream lrcStream, Encoding encoding, [EnumeratorCancellation] CancellationToken cancellationToken)
		{
			await foreach (var item in ParseAsEnumerableAsync(lrcStream, encoding, new LrcParserConfig(), cancellationToken))
			{
				yield return item;
			}
		}

		public async IAsyncEnumerable<SubtitleModel> ParseAsEnumerableAsync(Stream lrcStream, Encoding encoding, LrcParserConfig config, [EnumeratorCancellation] CancellationToken cancellationToken)
		{
			StreamHelper.ThrowIfStreamIsNotSeekableOrReadable(lrcStream);
			// seek the beginning of the stream
			lrcStream.Position = 0;

			var parts = GetPartsAsync(lrcStream, encoding, config, cancellationToken);
			var partsAny = await parts.PeekableAsync();
			if (!partsAny)
				throw new ArgumentException(BadFormatMsg);

			bool first = true;
			await foreach (LrcSubtitlePart part in parts.WithCancellation(cancellationToken))
			{
				yield return ParsePart(part, first, config);
				first = false;
			}
		}

		public IEnumerable<LrcSubtitlePart> GetParts(Stream stream, Encoding encoding)
		{
			return GetParts(stream, encoding, new LrcParserConfig());
		}

		public IEnumerable<LrcSubtitlePart> GetParts(Stream stream, Encoding encoding, LrcParserConfig config)
		{
			using StreamReader reader = new StreamReader(stream, encoding, detectEncodingFromByteOrderMarks: true, 1024, true);
			foreach (var part in GetLrcSubtitleParts(reader, config))
			{
				yield return part;
			}
		}

		public async IAsyncEnumerable<LrcSubtitlePart> GetPartsAsync(Stream stream, Encoding encoding, [EnumeratorCancellation] CancellationToken cancellationToken = default)
		{
			await foreach (var part in GetPartsAsync(stream, encoding, new LrcParserConfig(), cancellationToken))
			{
				yield return part;
			}
		}

		public async IAsyncEnumerable<LrcSubtitlePart> GetPartsAsync(Stream stream, Encoding encoding, LrcParserConfig config, [EnumeratorCancellation] CancellationToken cancellationToken = default)
		{
			using StreamReader reader = new StreamReader(stream, encoding, detectEncodingFromByteOrderMarks: true, 1024, true);
			await foreach (var part in GetLrcSubtitlePartsAsync(reader, config, cancellationToken))
			{
				yield return part;
			}
		}

		public SubtitleModel ParsePart(LrcSubtitlePart part, bool isFirstPart)
		{
			return ParsePart(part, isFirstPart, new LrcParserConfig());
		}

		public SubtitleModel ParsePart(LrcSubtitlePart part, bool isFirstPart, LrcParserConfig config)
		{
			return new SubtitleModel()
			{
				StartTime = part.StartTime,
				EndTime = part.EndTime,
				Lines = new List<string> { part.Text }
			};
		}

		/// <summary>
		/// Enumerates the subtitle parts in an LRC file.
		/// </summary>
		/// <param name="reader">The textreader associated with the LRC file</param>
		/// <param name="config">The parser configuration</param>
		/// <returns>An IEnumerable of LrcSubtitlePart objects</returns>
		private static IEnumerable<LrcSubtitlePart> GetLrcSubtitleParts(TextReader reader, LrcParserConfig config)
		{
			string? lastLine = null;
			string? currentLine = null;
			int searchTimeout = config.FirstLineSearchTimeout;

			// Read the file line by line
			while ((currentLine = reader.ReadLine()) != null)
			{
				// Validate the current line format early
				if (!IsValidLrcLine(currentLine))
				{
					searchTimeout--;
					// We didn't find the first valid line and reached the search timeout
					if (searchTimeout <= 0) throw new ArgumentException("Stream is not in a valid Lrc format (could not find valid timestamp format inside given line timeout)");
					continue;
				}

				// Process the previous line now that we know it's end time with the current line
				if (lastLine != null)
				{
					int? startTime = ParseLrcTime(lastLine);
					int? endTime = ParseLrcTime(currentLine);

					if (startTime.HasValue && endTime.HasValue)
					{
						yield return new LrcSubtitlePart
						{
							StartTime = startTime.Value,
							EndTime = endTime.Value,
							Text = CleanLrcLine(lastLine)
						};
					}
				}

				lastLine = currentLine;
			}

			// Process the last line if it exists
			if (lastLine != null)
			{
				int? startTime = ParseLrcTime(lastLine);

				if (startTime.HasValue)
				{
					yield return new LrcSubtitlePart
					{
						StartTime = startTime.Value,
						EndTime = -1, // We can't know the end time of the last line of our file
						Text = CleanLrcLine(lastLine)
					};
				}
			}
		}

		/// <summary>
		/// Asynchronously enumerates the subtitle parts in an LRC file.
		/// </summary>
		/// <param name="reader">The textreader associated with the LRC file</param>
		/// <param name="config">The parser configuration</param>
		/// <param name="cancellationToken">Cancellation token</param>
		/// <returns>An IAsyncEnumerable of LrcSubtitlePart objects</returns>
		private static async IAsyncEnumerable<LrcSubtitlePart> GetLrcSubtitlePartsAsync(TextReader reader, LrcParserConfig config, [EnumeratorCancellation] CancellationToken cancellationToken = default)
		{
			string? lastLine = null;
			string? currentLine = null;
			int searchTimeout = config.FirstLineSearchTimeout;

			// Read the file line by line
			while ((currentLine = await reader.ReadLineAsync()) != null)
			{
				cancellationToken.ThrowIfCancellationRequested();

				// Validate the current line format early
				if (!IsValidLrcLine(currentLine))
				{
					searchTimeout--;
					// We didn't find the first valid line and reached the search timeout
					if (searchTimeout <= 0) throw new ArgumentException("Stream is not in a valid Lrc format (could not find valid timestamp format inside given line timeout)");
					continue;
				}

				// Process the previous line now that we know it's end time with the current line
				if (lastLine != null)
				{
					int? startTime = ParseLrcTime(lastLine);
					int? endTime = ParseLrcTime(currentLine);

					if (startTime.HasValue && endTime.HasValue)
					{
						yield return new LrcSubtitlePart
						{
							StartTime = startTime.Value,
							EndTime = endTime.Value,
							Text = CleanLrcLine(lastLine)
						};
					}
				}

				lastLine = currentLine;
			}

			// Process the last line if it exists
			if (lastLine != null)
			{
				int? startTime = ParseLrcTime(lastLine);

				if (startTime.HasValue)
				{
					yield return new LrcSubtitlePart
					{
						StartTime = startTime.Value,
						EndTime = -1, // We can't know the end time of the last line of our file
						Text = CleanLrcLine(lastLine)
					};
				}
			}
		}

		/// <summary>
		/// Checks if a line contains a valid LRC timestamp.
		/// </summary>
		/// <returns>True if the line is valid, if not, false.</returns>
		/// <param name="line">The line to check.</param>
		private static bool IsValidLrcLine(string line)
		{
			return ShortTimestampRegex.IsMatch(line) || LongTimestampRegex.IsMatch(line);
		}

		/// <summary>
		/// Removes timestamps and enhanced LRC format tags from a line.
		/// </summary>
		/// <returns>The cleaned line without timestamps.</returns>
		/// <param name="line">The line to clean.</param>
		private static string CleanLrcLine(string line)
		{
			int timestampEndIndex = line.IndexOf(']') + 1;
			string content = line.Substring(timestampEndIndex).Trim();
			return EnhancedLrcFormatRegex.Replace(content, string.Empty);
		}

		/// <summary>
		/// Parses the timestamp from a line and converts it to milliseconds.
		/// Example:
		/// [00:00.xx] My lyrics!
		/// </summary>
		/// <returns>The timestamp in milliseconds or null if it could not be parsed.</returns>
		/// <param name="line">The line containing the timestamp.</param>
		private static int? ParseLrcTime(string line)
		{
			Match match = ShortTimestampRegex.Match(line);
			if (!match.Success)
			{
				match = LongTimestampRegex.Match(line);
			}

			if (match.Success)
			{
				// Try to parse the timestamp
				int hours = int.TryParse(match.Groups["H"]?.Value, out int h) ? h : 0;
				int minutes = int.TryParse(match.Groups["M"]?.Value, out int m) ? m : 0;
				int seconds = int.TryParse(match.Groups["S"]?.Value, out int s) ? s : 0;
				int milliseconds = int.TryParse(match.Groups["X"]?.Value, out int x) ? x : 0;

				// Convert to total in milliseconds
				return (int)new TimeSpan(0, hours, minutes, seconds, milliseconds).TotalMilliseconds;
			}

			return null;
		}
	}

	/// <summary>
	/// Represents a parsed LRC subtitle part before conversion to SubtitleModel
	/// </summary>
	internal class LrcSubtitlePart
	{
		public int StartTime { get; set; }
		public int EndTime { get; set; }
		public string Text { get; set; } = string.Empty;
	}
}
