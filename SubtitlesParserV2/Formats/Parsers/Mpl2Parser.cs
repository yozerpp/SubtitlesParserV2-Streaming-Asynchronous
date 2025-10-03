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
	/// <para>Parser for the .mpl subtitles files.</para>
	/// </summary>
	/// 
	/// <!--
	/// Sources:
	/// https://wiki.multimedia.cx/index.php/MPL2
	/// Example:
	/// [604][640]Sample 1
	/// [650][686]Sample 2!
	/// -->
	internal class Mpl2Parser : ISubtitlesParser<Mpl2SubtitlePart>
	{
		// Format [00][00] and separate by two group
		private static readonly Regex TimestampRegex = new Regex(@"\[(?<START>\d+)]\[(?<END>\d+)]", RegexOptions.Compiled);

		private const string BadFormatMsg = "Stream is not in a valid Mpl2 format";

		public List<SubtitleModel> ParseStream(Stream mpl2Stream, Encoding encoding)
		{
			var ret = ParseStreamConsuming(mpl2Stream, encoding).ToList();
			if (ret.Count == 0) throw new FormatException(BadFormatMsg);
			return ret;
		}

		public async Task<List<SubtitleModel>> ParseStreamAsync(Stream stream, Encoding encoding, CancellationToken cancellationToken)
		{
			var ret = await ParseStreamConsumingAsync(stream, encoding, cancellationToken).ToListAsync(cancellationToken);
			if (ret.Count == 0) throw new FormatException(BadFormatMsg);
			return ret;
		}

		public IEnumerable<SubtitleModel> ParseStreamConsuming(Stream mpl2Stream, Encoding encoding)
		{
			StreamHelper.ThrowIfStreamIsNotSeekableOrReadable(mpl2Stream);
			// seek the beginning of the stream
			mpl2Stream.Position = 0;

			IEnumerable<Mpl2SubtitlePart> parts = GetParts(mpl2Stream, encoding).Peekable(out var partsAny);
			if (!partsAny)
				throw new FormatException(BadFormatMsg);

			bool first = true;
			foreach (Mpl2SubtitlePart part in parts)
			{
				yield return ParsePart(part, first);
				first = false;
			}
		}

		public async IAsyncEnumerable<SubtitleModel> ParseStreamConsumingAsync(Stream mpl2Stream, Encoding encoding, [EnumeratorCancellation] CancellationToken cancellationToken)
		{
			StreamHelper.ThrowIfStreamIsNotSeekableOrReadable(mpl2Stream);
			// seek the beginning of the stream
			mpl2Stream.Position = 0;

			var partsOld = GetPartsAsync(mpl2Stream, encoding, cancellationToken);
			var (parts,partsAny) = await partsOld.PeekableAsync();
			if (!partsAny)
				throw new FormatException(BadFormatMsg);

			bool first = true;
			await foreach (Mpl2SubtitlePart part in parts.WithCancellation(cancellationToken))
			{
				yield return ParsePart(part, first);
				first = false;
			}
		}

		public IEnumerable<Mpl2SubtitlePart> GetParts(Stream stream, Encoding encoding)
		{
			using StreamReader reader = new StreamReader(stream, encoding, detectEncodingFromByteOrderMarks: true, 1024, true);
			foreach (var part in GetMpl2SubtitleParts(reader))
			{
				yield return part;
			}
		}

		public async IAsyncEnumerable<Mpl2SubtitlePart> GetPartsAsync(Stream stream, Encoding encoding, [EnumeratorCancellation] CancellationToken cancellationToken = default)
		{
			using StreamReader reader = new StreamReader(stream, encoding, detectEncodingFromByteOrderMarks: true, 1024, true);
			await foreach (var part in GetMpl2SubtitlePartsAsync(reader, cancellationToken))
			{
				yield return part;
			}
		}

		public SubtitleModel ParsePart(Mpl2SubtitlePart part, bool isFirstPart)
		{
			return new SubtitleModel()
			{
				StartTime = part.StartTime,
				EndTime = part.EndTime,
				Lines = part.Lines
			};
		}

		/// <summary>
		/// Enumerates the subtitle parts in an MPL2 file.
		/// </summary>
		/// <param name="reader">The textreader associated with the MPL2 file</param>
		/// <returns>An IEnumerable of Mpl2SubtitlePart objects</returns>
		private static IEnumerable<Mpl2SubtitlePart> GetMpl2SubtitleParts(TextReader reader)
		{
			string? currentLine = reader.ReadLine();
			// Loop until we reach end of file
			while (currentLine != null)
			{
				(int lineStartms, int lineEndms) = ParseMpl2Timestamp(currentLine);
				List<string> lineContent = ParseMpl2Line(currentLine);

				yield return new Mpl2SubtitlePart
				{
					StartTime = lineStartms,
					EndTime = lineEndms,
					Lines = lineContent
				};

				currentLine = reader.ReadLine();
			}
		}

		/// <summary>
		/// Asynchronously enumerates the subtitle parts in an MPL2 file.
		/// </summary>
		/// <param name="reader">The textreader associated with the MPL2 file</param>
		/// <param name="cancellationToken">Cancellation token</param>
		/// <returns>An IAsyncEnumerable of Mpl2SubtitlePart objects</returns>
		private static async IAsyncEnumerable<Mpl2SubtitlePart> GetMpl2SubtitlePartsAsync(TextReader reader, [EnumeratorCancellation] CancellationToken cancellationToken = default)
		{
			string? currentLine = await reader.ReadLineAsync();
			// Loop until we reach end of file
			while (currentLine != null)
			{
				cancellationToken.ThrowIfCancellationRequested();

				(int lineStartms, int lineEndms) = ParseMpl2Timestamp(currentLine);
				List<string> lineContent = ParseMpl2Line(currentLine);

				yield return new Mpl2SubtitlePart
				{
					StartTime = lineStartms,
					EndTime = lineEndms,
					Lines = lineContent
				};

				currentLine = await reader.ReadLineAsync();
			}
		}

		/// <summary>
		/// Parse the content of one Mpl2 line (assuming '|' new line character is used)
		/// </summary>
		/// <param name="line"></param>
		/// <returns>The lines content</returns>
		private static List<string> ParseMpl2Line(string line)
		{
			// Parse the line
			string[] parts = line.Split(']', 3);
			// Ensure there is at least 3 string ( [START] & [END] & CONTENT )
			if (parts.Length < 3) throw new ArgumentException("Stream line is not in a valid Mpl2 format.");

			// Two first elements are the timestamp, what follow it the content
			string content = parts[2];
			// Apply new line character
			return content.Split('|').Select(line => line.Trim()).ToList();
		}

		/// <summary>
		/// Parse the time of a Mpl2 line and convert it to milliseconds
		/// </summary>
		/// <!--
		/// Time Format: [SS][SS] (start time followed by end time in seconds)
		/// Example:
		/// [604][640]Sample 1
		/// [650][686]Sample 2!
		/// -->
		/// <param name="line"></param>
		/// <returns>The start and end time in milliseconds of the line</returns>
		/// <exception cref="ArgumentException">When line is not in a valid format</exception>
		private static (int startTime, int endTime) ParseMpl2Timestamp(string line)
		{
			// Parse the timestamp
			Match matchs = TimestampRegex.Match(line);
			// Ensure there is at least 2 matches ( [START] & [END] )
			// NOTE: We could default to defining time to -1 when invalid, however due to the file having almost no unique feature,
			// a invalid timestamp is the best way to detect that the current stream is not in Mpl2 format and stop parsing early.
			if (matchs.Groups.Count < 2) throw new ArgumentException("Stream line is not in a valid Mpl2 format.");

			int startTime = 0;
			int endTime = 0;
			// Parse time, throw error if it fail
			if (!int.TryParse(matchs?.Groups["START"]?.Value, out startTime) || !int.TryParse(matchs?.Groups["END"]?.Value, out endTime))
			{
				throw new ArgumentException("Stream line has invalid characters at positions used for time. Stream is not a valid Mpl2 format.");
			}
			return ((int)new TimeSpan(0, 0, startTime).TotalMilliseconds, (int)new TimeSpan(0, 0, endTime).TotalMilliseconds);
		}
	}

	/// <summary>
	/// Represents a parsed MPL2 subtitle part before conversion to SubtitleModel
	/// </summary>
	internal class Mpl2SubtitlePart
	{
		public int StartTime { get; set; }
		public int EndTime { get; set; }
		public List<string> Lines { get; set; } = new List<string>();
	}
}
