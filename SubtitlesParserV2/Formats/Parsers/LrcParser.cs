using SubtitlesParserV2.Models;
using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using System.Text.RegularExpressions;

namespace SubtitlesParserV2.Formats.Parsers
{
	/// <summary>
	/// Parser for the .lrc subtitles files
	/// https://en.wikipedia.org/wiki/LRC_(file_format)
	/// https://docs.fileformat.com/misc/lrc/
	/// Support : Core LRC, Enhanced LRC format (A2 extension)
	/// NOTE: Last item end time will always be -1
	/// 
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
	/// </summary>
	internal class LrcParser : ISubtitlesParser
	{
		// Format : [0000:00.00] / [mm:ss.xx]
		private static readonly Regex ShortTimestampRegex = new Regex(@"\[(?<M>\d+):(?<S>\d{2})\.(?<X>\d{2})\]", RegexOptions.Compiled);
		// Format : [0000:00:00.00] / [hh:mm:ss.xx] (Not part of the official docs, but some applications decided to do it that way)
		private static readonly Regex LongTimestampRegex = new Regex(@"\[(?<H>\d+):(?<M>\d+):(?<S>\d{2})\.(?<X>\d{2})\]", RegexOptions.Compiled);
		// Format <00:00.00> used inside the lines by the Enhanced LRC format (A2 Extension)
		private static readonly Regex EnhancedLrcFormatRegex = new Regex(@"<\d{2}:\d{2}\.\d{2}>", RegexOptions.Compiled);
		public List<SubtitleModel> ParseStream(Stream lrcStream, Encoding encoding)
		{
			// test if stream if readable and seekable (just a check, should be good)
			if (!lrcStream.CanRead || !lrcStream.CanSeek)
			{
				throw new ArgumentException($"Stream must be seekable and readable in a subtitles parser. Operation interrupted; isSeekable: {lrcStream.CanSeek} - isReadable: {lrcStream.CanRead}");
			}

			// seek the beginning of the stream
			lrcStream.Position = 0;

			// Create a StreamReader & configure it to leave the main stream open when disposing
			using StreamReader reader = new StreamReader(lrcStream, encoding, true, 1024, true);

			List<SubtitleModel> items = new List<SubtitleModel>();

			// Store a line as the lastLine so we can re-use it once we know the next line start time
			// (Since the next line start time is also the end time for the lastLine)
			string? lastLine = reader.ReadLine();
			// Loop until last line was processed (is null), then do a final loop
			do
			{
				string? nextLine = reader.ReadLine();
				if (nextLine != null && lastLine != null)
				{
					// Current line start time (Aka, end time of lastLine)
					int? lrcNextLineTimeMs = ParseLrcTime(nextLine);
					// Last line start time
					int? lrcLastLineTimeMs = ParseLrcTime(lastLine);

					// If start time or end time of the item we want to add (the lastLine) is null, we ignore the line
					if (lrcLastLineTimeMs != null && lrcNextLineTimeMs != null)
					{
						// Remove the timestamp from the line [mm:ss.xx] & EnhancedLrcFormat
						string cleanLine = EnhancedLrcFormatRegex.Replace(lastLine.Substring(lastLine.IndexOf(']') + 1).Trim(), string.Empty);
						items.Add(new SubtitleModel()
						{
							StartTime = lrcLastLineTimeMs.Value,
							EndTime = lrcNextLineTimeMs.Value,
							Lines = new List<string> { cleanLine }
						});
					}
					lastLine = nextLine; // Put our current line into the lastLine before starting the loop again
				} else if (nextLine == null && lastLine != null) // If we reached the end of the file, there is only "lastLine" that need to be added
				{
					// Last line start time
					int? lrcLastLineTimeMs = ParseLrcTime(lastLine);
					// If start time item we want to add (the lastLine) is null, we ignore the line
					if (lrcLastLineTimeMs != null)
					{
						// Remove the timestamp from the line [mm:ss.xx] & EnhancedLrcFormat
						string cleanLine = EnhancedLrcFormatRegex.Replace(lastLine.Substring(lastLine.IndexOf(']') + 1).Trim(), string.Empty);
						items.Add(new SubtitleModel()
						{
							StartTime = lrcLastLineTimeMs.Value,
							EndTime = -1, // Since this is the last item, we can't know the end time, we could implement support for files that mention the "length".
							Lines = new List<string> { cleanLine }
						});
					}
					lastLine = null; // Cause the loop to stop, could do break; too
				}
			} while (lastLine != null);

			// Ensure we at least found 1 valid item
			if (items.Count >= 1) 
			{
				return items;
			} else
			{
				throw new ArgumentException("Stream is not in a valid Lrc format");
			}
		}

		/// <summary>
		/// Parse lrc time in milliseconds per line
		/// Example:
		/// [00:00.xx] My lyrics!
		/// </summary>
		/// <param name="line">The line to parse</param>
		/// <returns>The time in milliseconds or null if not found.</returns>
		private static int? ParseLrcTime(string line) 
		{
			int hours = 0;
			int minutes = 0;
			int seconds = 0;
			int milliseconds = 0;
			// Try parsing using short time (official one)
			Match match = ShortTimestampRegex.Match(line);
			// Try parsing using the long time (not official)
			if (!match.Success)
				match = LongTimestampRegex.Match(line);

			if (match.Success) 
			{
				// Parse values
				int.TryParse(match?.Groups["H"]?.Value, out hours);
				int.TryParse(match?.Groups["M"]?.Value, out minutes);
				int.TryParse(match?.Groups["S"]?.Value, out seconds);
				int.TryParse(match?.Groups["X"]?.Value, out milliseconds);
				return (int)new TimeSpan(hours, minutes, seconds, milliseconds).TotalMilliseconds;
			}
			return null;
		}
	}
}
