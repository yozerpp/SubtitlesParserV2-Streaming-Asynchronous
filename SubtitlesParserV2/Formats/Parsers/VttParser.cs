using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Runtime.CompilerServices;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;
using System.Web;
using SubtitlesParserV2.Helpers;
using SubtitlesParserV2.Models;

namespace SubtitlesParserV2.Formats.Parsers
{
	/// <summary>
	/// Parser for the .vtt subtitles files. Does not handle formatting tags within the text.
	/// </summary>
	/// <!--
	/// Sources:
	/// https://www.w3.org/TR/webvtt1/#file-structure
	/// https://developer.mozilla.org/en-US/docs/Web/API/WebVTT_API/Web_Video_Text_Tracks_Format#cue_payload_text_tags
	/// 
	/// Example:
	/// WEBVTT
	///
	/// CUE - 1
	/// 00:00:10.500 --> 00:00:13.000
	/// Elephant's Dream
	///
	/// CUE - 2
	/// 00:00:15.000 --> 00:00:18.000
	/// At the left we can see...
	/// -->
	internal class VttParser : ISubtitlesParser<string>
	{
		// Properties -----------------------------------------------------------------------

		private static readonly Regex _rxLongTimestamp = new Regex("(?<H>[0-9]+):(?<M>[0-9]+):(?<S>[0-9]+)[,\\.](?<m>[0-9]+)", RegexOptions.Compiled);
		private static readonly Regex _rxShortTimestamp = new Regex("(?<M>[0-9]+):(?<S>[0-9]+)[,\\.](?<m>[0-9]+)", RegexOptions.Compiled);
		
		private static readonly string[] _delimiters = new string[] { "-->", "- >", "->" };
		private static readonly string[] _newLineCharacters = { "\r\n", "\r", "\n" };

		private const string BadFormatMsg = "Stream is not in a valid WebVTT format";

		// Methods -------------------------------------------------------------------------

		public List<SubtitleModel> ParseStream(Stream vttStream, Encoding encoding)
		{
			var ret = ParseStreamConsuming(vttStream, encoding).ToList();
			if (ret.Count == 0) throw new FormatException(BadFormatMsg);
			return ret;
		}

		public async Task<List<SubtitleModel>> ParseStreamAsync(Stream stream, Encoding encoding, CancellationToken cancellationToken)
		{
			var ret = await ParseStreamConsumingAsync(stream, encoding, cancellationToken).ToListAsync(cancellationToken);
			if (ret.Count == 0) throw new FormatException(BadFormatMsg);
			return ret;
		}

		public IEnumerable<SubtitleModel> ParseStreamConsuming(Stream vttStream, Encoding encoding)
		{
			StreamHelper.ThrowIfStreamIsNotSeekableOrReadable(vttStream);
			// seek the beginning of the stream
			vttStream.Position = 0;

			IEnumerable<string> parts = GetParts(vttStream, encoding).Peekable(out var partsAny);
			if (!partsAny)
				throw new FormatException(BadFormatMsg);

			bool first = true;
			foreach (string part in parts)
			{
				var ret =  ParsePart(part, first);
				first = false;
				if(ret.Equals(SubtitleModel.Default))
					continue;
				yield return ret;
			}
		}

		public async IAsyncEnumerable<SubtitleModel> ParseStreamConsumingAsync(Stream vttStream, Encoding encoding, [EnumeratorCancellation] CancellationToken cancellationToken)
		{
			StreamHelper.ThrowIfStreamIsNotSeekableOrReadable(vttStream);
			// seek the beginning of the stream
			vttStream.Position = 0;

			var partsOld = GetPartsAsync(vttStream, encoding, cancellationToken);
			var (parts, partsAny) = await partsOld.PeekableAsync();
			if (!partsAny)
				throw new FormatException(BadFormatMsg);

			bool first = true;
			await foreach (string part in parts.WithCancellation(cancellationToken))
			{
				var ret =  ParsePart(part, first);
				first = false;
				if(ret.Equals(SubtitleModel.Default))
					continue;
				yield return ret;
			}
		}

		public IEnumerable<string> GetParts(Stream stream, Encoding encoding)
		{
			using StreamReader reader = new StreamReader(stream, encoding, detectEncodingFromByteOrderMarks: true, 1024, true);
			foreach (var part in GetVttSubTitleParts(reader))
			{
				yield return part;
			}
		}

		public async IAsyncEnumerable<string> GetPartsAsync(Stream stream, Encoding encoding, [EnumeratorCancellation] CancellationToken cancellationToken = default)
		{
			using StreamReader reader = new StreamReader(stream, encoding, detectEncodingFromByteOrderMarks: true, 1024, true);
			await foreach (var part in GetVttSubTitlePartsAsync(reader, cancellationToken))
			{
				yield return part;
			}
		}

		public SubtitleModel ParsePart(string part, bool isFirstPart)
		{
			// If this is the first part, verify it's the WebVTT header
			if (isFirstPart)
			{
				if (part.Equals("WEBVTT", StringComparison.InvariantCultureIgnoreCase))
				{
					// Return an empty subtitle for the header
					return SubtitleModel.Default;
				}
				else
				{
					throw new FormatException("Could not find WebVTT header at line 1.");
				}
			}

			IEnumerable<string> lines = part.Split(_newLineCharacters, StringSplitOptions.RemoveEmptyEntries).Select(s => s.Trim());

			SubtitleModel item = new SubtitleModel();
			foreach (string line in lines)
			{
				// Verify if we already have defined the subtitle time (found the line that tell us the time info) or not
				if (item.StartTime == -1 && item.EndTime == -1)
				{
					// Verify if current line is a timecode line
					bool success = TryParseTimecodeLine(line, out int startTc, out int endTc);
					if (success)
					{
						// Set current item time
						item.StartTime = startTc;
						item.EndTime = endTc;
					}
				}
				else
				{
					/* Add current line to item,
					* Decode it using html as docs recommend to html encode special characters like ">" & "<" or "&".
					* We then remove all angle brackets and the content inside, as this is how formatting (not supported here)
					* is done on WebVTT. This mean timed lyrics, for example karaoke-style "My <00:00:00>time<00:02:40> is up!" or
					* text with specific style / fonts won't work.
					*/
					item.Lines.Add(Regex.Replace(HttpUtility.HtmlDecode(line), @"<.*?>", string.Empty).Trim());
				}
			}

			return item;
		}

		/// <summary>
		/// Enumerates the subtitle parts in a VTT file based on the standard line break observed between them.
		/// A VTT subtitle part is in the form:
		///
		/// CUE - 1
		/// 00:00:20.000 --> 00:00:24.400
		/// Altocumulus clouds occur between six thousand
		///
		/// The first line (cue) is optional, as well as the hours in the time codes.
		/// </summary>
		/// <param name="reader">The textreader associated with the vtt file</param>
		/// <returns>An IEnumerable(string) object containing all the subtitle parts</returns>
		private static IEnumerable<string> GetVttSubTitleParts(TextReader reader)
		{
			string? line = reader.ReadLine(); // Read first line
			StringBuilder sb = new StringBuilder();
			while (line != null)
			{
				// Verify if it's the end of the current part (new empty line)
				if (string.IsNullOrEmpty(line.Trim()))
				{
					// Return if the string builder has text, else do nothing
					string res = sb.ToString().TrimEnd();
					if (!string.IsNullOrEmpty(res))
					{
						yield return res;
					}
					sb = new StringBuilder();
				}
				else // Still inside the part, save the content
				{
					sb.AppendLine(line);
				}
				line = reader.ReadLine(); // Read line for next loop
			}

			if (sb.Length > 0)
			{
				yield return sb.ToString();
			}
		}

		/// <summary>
		/// Asynchronously enumerates the subtitle parts in a VTT file based on the standard line break observed between them.
		/// </summary>
		/// <param name="reader">The textreader associated with the vtt file</param>
		/// <param name="cancellationToken">Cancellation token</param>
		/// <returns>An IAsyncEnumerable(string) object containing all the subtitle parts</returns>
		private static async IAsyncEnumerable<string> GetVttSubTitlePartsAsync(TextReader reader, [EnumeratorCancellation] CancellationToken cancellationToken = default)
		{
			string? line = await reader.ReadLineAsync(); // Read first line
			StringBuilder sb = new StringBuilder();

			while (line != null)
			{
				cancellationToken.ThrowIfCancellationRequested();

				// Verify if it's the end of the current part (new empty line)
				if (string.IsNullOrEmpty(line.Trim()))
				{
					// Return if the string builder has text, else do nothing
					string res = sb.ToString().TrimEnd();
					if (!string.IsNullOrEmpty(res))
					{
						yield return res;
					}
					sb = new StringBuilder();
				}
				else // Still inside the part, save the content
				{
					sb.AppendLine(line);
				}
				line = await reader.ReadLineAsync(); // Read line for next loop
			}

			if (sb.Length > 0)
			{
				yield return sb.ToString();
			}
		}

		/// <summary>
		/// Method that try to parse a line of the vtt file to get the start and end timecode.
		/// </summary>
		/// <param name="line">The line to parse</param>
		/// <param name="startTc">The output start time in milliseconds</param>
		/// <param name="endTc">The output end time in milliseconds</param>
		/// <returns>True if it parsed the timecode from the line, else false</returns>
		private static bool TryParseTimecodeLine(string line, out int startTc, out int endTc)
		{
			string[] parts = line.Split(_delimiters, StringSplitOptions.None);
			if (parts.Length != 2)
			{
				// this is not a timecode line
				startTc = -1;
				endTc = -1;
				return false;
			}
			else
			{
				startTc = ParseVttTimecode(parts[0]);
				endTc = ParseVttTimecode(parts[1]);
				return true;
			}
		}

		/// <summary>
		/// Takes an VTT timecode as a string and parses it into milliseconds. A VTT timecode reads as follows:
		/// 00:00:20.000
		/// or
		/// 00:20.000
		/// </summary>
		/// <param name="s">The timecode to parse</param>
		/// <returns>The parsed string timecode converted to milliseconds. If the parsing was unsuccessful, -1 is returned</returns>
		private static int ParseVttTimecode(string s)
		{
			int hours = 0;
			int minutes = 0;
			int seconds = 0;
			int milliseconds = -1;
			Match match = _rxLongTimestamp.Match(s);
			if (match.Success)
			{
				hours = int.Parse(match.Groups["H"].Value);
				minutes = int.Parse(match.Groups["M"].Value);
				seconds = int.Parse(match.Groups["S"].Value);
				milliseconds = int.Parse(match.Groups["m"].Value);
			}
			else
			{
				match = _rxShortTimestamp.Match(s);
				if (match.Success)
				{
					minutes = int.Parse(match.Groups["M"].Value);
					seconds = int.Parse(match.Groups["S"].Value);
					milliseconds = int.Parse(match.Groups["m"].Value);
				}
			}

			if (milliseconds >= 0)
			{
				TimeSpan result = new TimeSpan(0, hours, minutes, seconds, milliseconds);
				return (int)result.TotalMilliseconds;
			}

			return -1;
		}
	}
}
