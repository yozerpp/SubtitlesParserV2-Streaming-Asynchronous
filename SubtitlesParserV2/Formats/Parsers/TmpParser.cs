using SubtitlesParserV2.Helpers;
using SubtitlesParserV2.Models;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Runtime.CompilerServices;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

namespace SubtitlesParserV2.Formats.Parsers
{
	/// <summary>
	/// <para>Parser for the .tmp subtitles files.</para>
	/// <strong>NOTE</strong>: Last item end time will always be -1
	/// </summary>
	/// 
	/// <!--
	/// Sources:
	/// https://web.archive.org/web/20080121210347/https://napisy.ovh.org/readarticle.php?article_id=4
	/// https://wiki.multimedia.cx/index.php/TMPlayer
	/// Example:
	/// 00:01:52:Sample 1
	/// 00:01:55:Sample 2!
	/// -->
	internal class TmpParser : ISubtitlesParser<TmpSubtitlePart>
	{
		private const string BadFormatMsg = "Stream is not in a valid Tmp format";

		public List<SubtitleModel> ParseStream(Stream tmpStream, Encoding encoding)
		{
			var ret = ParseAsEnumerable(tmpStream, encoding).ToList();
			if (ret.Count == 0) throw new ArgumentException(BadFormatMsg);
			return ret;
		}

		public async Task<List<SubtitleModel>> ParseStreamAsync(Stream stream, Encoding encoding, CancellationToken cancellationToken)
		{
			var ret = await ParseAsEnumerableAsync(stream, encoding, cancellationToken).ToListAsync(cancellationToken);
			if (ret.Count == 0) throw new ArgumentException(BadFormatMsg);
			return ret;
		}

		public IEnumerable<SubtitleModel> ParseAsEnumerable(Stream tmpStream, Encoding encoding)
		{
			StreamHelper.ThrowIfStreamIsNotSeekableOrReadable(tmpStream);
			// seek the beginning of the stream
			tmpStream.Position = 0;

			IEnumerable<TmpSubtitlePart> parts = GetParts(tmpStream, encoding).Peekable(out var partsAny);
			if (!partsAny)
				throw new ArgumentException(BadFormatMsg);

			bool first = true;
			foreach (TmpSubtitlePart part in parts)
			{
				yield return ParsePart(part, first);
				first = false;
			}
		}

		public async IAsyncEnumerable<SubtitleModel> ParseAsEnumerableAsync(Stream tmpStream, Encoding encoding, [EnumeratorCancellation] CancellationToken cancellationToken)
		{
			StreamHelper.ThrowIfStreamIsNotSeekableOrReadable(tmpStream);
			// seek the beginning of the stream
			tmpStream.Position = 0;

			var parts = GetPartsAsync(tmpStream, encoding, cancellationToken);
			var partsAny = await parts.PeekableAsync();
			if (!partsAny)
				throw new ArgumentException(BadFormatMsg);

			bool first = true;
			await foreach (TmpSubtitlePart part in parts.WithCancellation(cancellationToken))
			{
				yield return ParsePart(part, first);
				first = false;
			}
		}

		public IEnumerable<TmpSubtitlePart> GetParts(Stream stream, Encoding encoding)
		{
			using StreamReader reader = new StreamReader(stream, encoding, detectEncodingFromByteOrderMarks: true, 1024, true);
			foreach (var part in GetTmpSubtitleParts(reader))
			{
				yield return part;
			}
		}

		public async IAsyncEnumerable<TmpSubtitlePart> GetPartsAsync(Stream stream, Encoding encoding, [EnumeratorCancellation] CancellationToken cancellationToken = default)
		{
			using StreamReader reader = new StreamReader(stream, encoding, detectEncodingFromByteOrderMarks: true, 1024, true);
			await foreach (var part in GetTmpSubtitlePartsAsync(reader, cancellationToken))
			{
				yield return part;
			}
		}

		public SubtitleModel ParsePart(TmpSubtitlePart part, bool isFirstPart)
		{
			return new SubtitleModel()
			{
				StartTime = part.StartTime,
				EndTime = part.EndTime,
				Lines = part.Lines
			};
		}

		/// <summary>
		/// Enumerates the subtitle parts in a TMP file.
		/// Each part contains a line with timing and content, and we need the next line to determine the end time.
		/// </summary>
		/// <param name="reader">The textreader associated with the tmp file</param>
		/// <returns>An IEnumerable of TmpSubtitlePart objects</returns>
		private static IEnumerable<TmpSubtitlePart> GetTmpSubtitleParts(TextReader reader)
		{
			// Store a line as the lastLine so we can re-use it once we know the next line
			// (Since the nextLine start time is also the end time for the lastLine)
			string? lastLine = reader.ReadLine();
			if (lastLine == null)
				throw new ArgumentException("Stream reached end of file on first reading attempt.");

			// Loop until last line was processed (is null), then do a final loop
			do
			{
				string? nextLine = reader.ReadLine();
				// Parse last line
				(int lastLineTimeMs, List<string> lastLinesContent) = ParseTmpLine(lastLine);

				// If nextLine exists, we can know the end time of the previous line
				if (nextLine != null)
				{
					// Parse current line (Aka, end time of lastLine)
					(int nextLineTimeMs, _) = ParseTmpLine(nextLine);
					yield return new TmpSubtitlePart
					{
						StartTime = lastLineTimeMs,
						EndTime = nextLineTimeMs,
						Lines = lastLinesContent
					};
				}
				else // If we reached the end of the file, there is only "lastLine" that need to be added to items
				{
					yield return new TmpSubtitlePart
					{
						StartTime = lastLineTimeMs,
						EndTime = -1, // Since this is the last item, we can't know the end time
						Lines = lastLinesContent
					};
					break; // Once we reach that point, end of file was reached
				}
				lastLine = nextLine; // Put our current line into the lastLine before starting the loop again
			} while (lastLine != null);
		}

		/// <summary>
		/// Asynchronously enumerates the subtitle parts in a TMP file.
		/// </summary>
		/// <param name="reader">The textreader associated with the tmp file</param>
		/// <param name="cancellationToken">Cancellation token</param>
		/// <returns>An IAsyncEnumerable of TmpSubtitlePart objects</returns>
		private static async IAsyncEnumerable<TmpSubtitlePart> GetTmpSubtitlePartsAsync(TextReader reader, [EnumeratorCancellation] CancellationToken cancellationToken = default)
		{
			// Store a line as the lastLine so we can re-use it once we know the next line
			// (Since the nextLine start time is also the end time for the lastLine)
			string? lastLine = await reader.ReadLineAsync();
			if (lastLine == null)
				throw new ArgumentException("Stream reached end of file on first reading attempt.");

			// Loop until last line was processed (is null), then do a final loop
			do
			{
				cancellationToken.ThrowIfCancellationRequested();

				string? nextLine = await reader.ReadLineAsync();
				// Parse last line
				(int lastLineTimeMs, List<string> lastLinesContent) = ParseTmpLine(lastLine);

				// If nextLine exists, we can know the end time of the previous line
				if (nextLine != null)
				{
					// Parse current line (Aka, end time of lastLine)
					(int nextLineTimeMs, _) = ParseTmpLine(nextLine);
					yield return new TmpSubtitlePart
					{
						StartTime = lastLineTimeMs,
						EndTime = nextLineTimeMs,
						Lines = lastLinesContent
					};
				}
				else // If we reached the end of the file, there is only "lastLine" that need to be added to items
				{
					yield return new TmpSubtitlePart
					{
						StartTime = lastLineTimeMs,
						EndTime = -1, // Since this is the last item, we can't know the end time
						Lines = lastLinesContent
					};
					break; // Once we reach that point, end of file was reached
				}
				lastLine = nextLine; // Put our current line into the lastLine before starting the loop again
			} while (lastLine != null);
		}

		/// <summary>
		/// Parse one Tmp format line to get the time in milliseconds and the lines content (assuming '|' new line character is used)
		/// </summary>
		/// <!--
		/// Time Format: HH:MM:SS
		/// Example:
		/// 00:00:00:My lyrics!
		/// 00:00:02:My first line!|Second line!
		/// -->
		/// <param name="line"></param>
		/// <returns>The time in milliseconds and the lines content</returns>
		/// <exception cref="ArgumentException">When line is not in a valid format</exception>
		private static (int time, List<string> linesContent) ParseTmpLine(string line)
		{
			// Only split the first 4 ':', after which everything else is part of index 3 (Aka, the content)
			string[] parts = line.Split(':', 4);
			// Ensure they is at least 4 separations on the line
			// NOTE: We could default to defining time to -1 when invalid, however due to the file having almost no unique feature,
			// a invalid timestamp is the best way to detect that the current stream is not in TMP format and stop parsing early
			if (parts.Length < 4) throw new ArgumentException("Stream line is not in a valid TMP format.");

			int hours = 0;
			int minutes = 0;
			int seconds = 0;
			// Parse time, throw error if it fail
			if (!int.TryParse(parts[0], out hours) || !int.TryParse(parts[1], out minutes) || !int.TryParse(parts[2], out seconds))
			{
				throw new ArgumentException("Stream line has invalid characters at positions used for time. Stream is not a valid TMP format.");
			}
			// Return time in MS along with line content
			return ((int)new TimeSpan(hours, minutes, seconds).TotalMilliseconds, parts[3].Split('|').Select(line => line.Trim()).ToList());
		}
	}

	/// <summary>
	/// Represents a parsed TMP subtitle part before conversion to SubtitleModel
	/// </summary>
	internal class TmpSubtitlePart
	{
		public int StartTime { get; set; }
		public int EndTime { get; set; }
		public List<string> Lines { get; set; } = new List<string>();
	}
}
