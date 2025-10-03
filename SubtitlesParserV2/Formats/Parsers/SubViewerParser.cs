using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Runtime.CompilerServices;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;
using SubtitlesParserV2.Helpers;
using SubtitlesParserV2.Models;

namespace SubtitlesParserV2.Formats.Parsers
{
	/// <summary>
	/// Parser for SubViewer .sbv subtitles files.
	/// <strong>Support</strong> : SubViewer1 and SubViewer2
	/// </summary>
	/// <!--
	/// Sources:
	/// https://wiki.videolan.org/SubViewer/
	/// https://docs.fileformat.com/settings/sbv/
	/// Example:
	/// 
	/// [INFORMATION]
	/// ....
	/// 
	/// 00:04:35.03,00:04:38.82
	/// Hello guys... please sit down...
	/// 
	/// 00:05:00.19,00:05:03.47
	/// M. Franklin,[br]are you crazy?
	/// -->
	internal class SubViewerParser : ISubtitlesParser<SubViewerSubtitlePart>
	{
		// Properties ----------------------------------------------------------

		private const string SubViewer1InfoHeader = "[INFORMATION]";
		private const string SubViewer1SubtitleHeader = "[SUBTITLE]";
		private const string SubViewer2NewLine = "[br]";
		private const short MaxLineNumberForItems = 20;

		private static readonly Regex _timestampRegex = new Regex(@"\d{1,6}:\d{2}:\d{2}\.\d{2,10},\d{1,6}:\d{2}:\d{2}\.\d{2,10}", RegexOptions.Compiled);
		private const char TimecodeSeparator = ',';

		private const string BadFormatMsg = "Stream is not in a valid SubViewer format";

		// Methods -------------------------------------------------------------

		public List<SubtitleModel> ParseStream(Stream subStream, Encoding encoding)
		{
			var ret = ParseStreamConsuming(subStream, encoding).ToList();
			if (ret.Count == 0) throw new ArgumentException(BadFormatMsg);
			return ret;
		}

		public async Task<List<SubtitleModel>> ParseStreamAsync(Stream stream, Encoding encoding, CancellationToken cancellationToken)
		{
			var ret = await ParseStreamConsumingAsync(stream, encoding, cancellationToken).ToListAsync(cancellationToken);
			if (ret.Count == 0) throw new ArgumentException(BadFormatMsg);
			return ret;
		}

		public IEnumerable<SubtitleModel> ParseStreamConsuming(Stream subStream, Encoding encoding)
		{
			StreamHelper.ThrowIfStreamIsNotSeekableOrReadable(subStream);
			// seek the beginning of the stream
			subStream.Position = 0;

			IEnumerable<SubViewerSubtitlePart> parts = GetParts(subStream, encoding).Peekable(out var partsAny);
			if (!partsAny)
				throw new ArgumentException(BadFormatMsg);

			bool first = true;
			foreach (SubViewerSubtitlePart part in parts)
			{
				yield return ParsePart(part, first);
				first = false;
			}
		}

		public async IAsyncEnumerable<SubtitleModel> ParseStreamConsumingAsync(Stream subStream, Encoding encoding, [EnumeratorCancellation] CancellationToken cancellationToken)
		{
			StreamHelper.ThrowIfStreamIsNotSeekableOrReadable(subStream);
			// seek the beginning of the stream
			subStream.Position = 0;

			var parts = GetPartsAsync(subStream, encoding, cancellationToken);
			var partsAny = await parts.PeekableAsync();
			if (!partsAny)
				throw new ArgumentException(BadFormatMsg);

			bool first = true;
			await foreach (SubViewerSubtitlePart part in parts.WithCancellation(cancellationToken))
			{
				yield return ParsePart(part, first);
				first = false;
			}
		}

		public IEnumerable<SubViewerSubtitlePart> GetParts(Stream stream, Encoding encoding)
		{
			using StreamReader reader = new StreamReader(stream, encoding, detectEncodingFromByteOrderMarks: true, 1024, true);
			foreach (var part in GetSubViewerSubtitleParts(reader))
			{
				yield return part;
			}
		}

		public async IAsyncEnumerable<SubViewerSubtitlePart> GetPartsAsync(Stream stream, Encoding encoding, [EnumeratorCancellation] CancellationToken cancellationToken = default)
		{
			using StreamReader reader = new StreamReader(stream, encoding, detectEncodingFromByteOrderMarks: true, 1024, true);
			await foreach (var part in GetSubViewerSubtitlePartsAsync(reader, cancellationToken))
			{
				yield return part;
			}
		}

		public SubtitleModel ParsePart(SubViewerSubtitlePart part, bool isFirstPart)
		{
			// Parse the timecode line
			(int startTime, int endTime) = ParseTimecodeLine(part.TimecodeLine);

			// Process text lines for SubViewer2 [br] tags
			List<string> processedLines = new List<string>();
			foreach (string line in part.TextLines)
			{
				if (line.Contains(SubViewer2NewLine))
				{
					processedLines.AddRange(line.Split(SubViewer2NewLine).Select(realLine => realLine.Trim()));
				}
				else
				{
					processedLines.Add(line.Trim());
				}
			}

			return new SubtitleModel()
			{
				StartTime = startTime,
				EndTime = endTime,
				Lines = processedLines
			};
		}

		/// <summary>
		/// Enumerates the subtitle parts in a SubViewer file.
		/// </summary>
		/// <param name="reader">The textreader associated with the SubViewer file</param>
		/// <returns>An IEnumerable of SubViewerSubtitlePart objects</returns>
		private static IEnumerable<SubViewerSubtitlePart> GetSubViewerSubtitleParts(TextReader reader)
		{
			// Ensure the first line match a .sbv file format for SubViewer1 (optional info header / subtitle header)
			// or SubViewer2 (timestamp)
			string? line = reader.ReadLine() ?? string.Empty;
			int lineNumber = 1;
			if (line.Equals(SubViewer1InfoHeader) || line.Equals(SubViewer1SubtitleHeader) || IsTimestampLine(line))
			{
				// Read the stream until hard-coded max number of lines is read (Prevent infinite loop with SubViewer1)
				// or if the line is a timestamp line. The loop search the first timestamp for SubViewer1, in SubViewer2, the loop
				// should not run as the first line is already a timestamp.
				while (line != null && lineNumber <= MaxLineNumberForItems && !IsTimestampLine(line))
				{
					line = reader.ReadLine();
					lineNumber++;
				}

				// Here, the line is a timestamp line (due to our previous loop), except if we exceded the hard-coded
				// max number of lines read for SubViewer1
				if (line != null && lineNumber <= MaxLineNumberForItems)
				{
					// Store the timecode line (current line)
					string lastTimecodeLine = line;
					List<string> textLines = new List<string>();

					// Parse all the lines
					while (line != null)
					{
						/* We parses text lines until we find a timestamp line, at which point we
						 * add all of the found text lines under the previously found timestamp line (lastTimecodeLine)
						 * before starting again until the next timestamp line.
						 */
						line = reader.ReadLine();
						if (!string.IsNullOrEmpty(line))
						{
							if (IsTimestampLine(line))
							{
								// Yield the previous subtitle part if we have text lines
								if (textLines.Count >= 1)
								{
									yield return new SubViewerSubtitlePart
									{
										TimecodeLine = lastTimecodeLine,
										TextLines = textLines
									};
								}

								// Update the previous timestamp line to current timecode line and reset text lines
								lastTimecodeLine = line;
								textLines = new List<string>();
							}
							else
							{
								// Store the text line
								textLines.Add(line);
							}
						}
					}

					// If any text lines are left, we add them under the last known valid timecode line
					if (textLines.Count >= 1)
					{
						yield return new SubViewerSubtitlePart
						{
							TimecodeLine = lastTimecodeLine,
							TextLines = textLines
						};
					}
				}
				else
				{
					throw new ArgumentException($"Couldn't find the first timestamp line in the current sub file. Last line read: '{line}', line number #{lineNumber}.");
				}
			}
			else
			{
				throw new ArgumentException("Stream is not in a valid SubViewer format");
			}
		}

		/// <summary>
		/// Asynchronously enumerates the subtitle parts in a SubViewer file.
		/// </summary>
		/// <param name="reader">The textreader associated with the SubViewer file</param>
		/// <param name="cancellationToken">Cancellation token</param>
		/// <returns>An IAsyncEnumerable of SubViewerSubtitlePart objects</returns>
		private static async IAsyncEnumerable<SubViewerSubtitlePart> GetSubViewerSubtitlePartsAsync(TextReader reader, [EnumeratorCancellation] CancellationToken cancellationToken = default)
		{
			// Ensure the first line match a .sbv file format for SubViewer1 (optional info header / subtitle header)
			// or SubViewer2 (timestamp)
			string? line = await reader.ReadLineAsync() ?? string.Empty;
			int lineNumber = 1;
			if (line.Equals(SubViewer1InfoHeader) || line.Equals(SubViewer1SubtitleHeader) || IsTimestampLine(line))
			{
				// Read the stream until hard-coded max number of lines is read (Prevent infinite loop with SubViewer1)
				// or if the line is a timestamp line. The loop search the first timestamp for SubViewer1, in SubViewer2, the loop
				// should not run as the first line is already a timestamp.
				while (line != null && lineNumber <= MaxLineNumberForItems && !IsTimestampLine(line))
				{
					cancellationToken.ThrowIfCancellationRequested();
					line = await reader.ReadLineAsync();
					lineNumber++;
				}

				// Here, the line is a timestamp line (due to our previous loop), except if we exceded the hard-coded
				// max number of lines read for SubViewer1
				if (line != null && lineNumber <= MaxLineNumberForItems)
				{
					// Store the timecode line (current line)
					string lastTimecodeLine = line;
					List<string> textLines = new List<string>();

					// Parse all the lines
					while (line != null)
					{
						cancellationToken.ThrowIfCancellationRequested();

						/* We parses text lines until we find a timestamp line, at which point we
						 * add all of the found text lines under the previously found timestamp line (lastTimecodeLine)
						 * before starting again until the next timestamp line.
						 */
						line = await reader.ReadLineAsync();
						if (!string.IsNullOrEmpty(line))
						{
							if (IsTimestampLine(line))
							{
								// Yield the previous subtitle part if we have text lines
								if (textLines.Count >= 1)
								{
									yield return new SubViewerSubtitlePart
									{
										TimecodeLine = lastTimecodeLine,
										TextLines = textLines
									};
								}

								// Update the previous timestamp line to current timecode line and reset text lines
								lastTimecodeLine = line;
								textLines = new List<string>();
							}
							else
							{
								// Store the text line
								textLines.Add(line);
							}
						}
					}

					// If any text lines are left, we add them under the last known valid timecode line
					if (textLines.Count >= 1)
					{
						yield return new SubViewerSubtitlePart
						{
							TimecodeLine = lastTimecodeLine,
							TextLines = textLines
						};
					}
				}
				else
				{
					throw new ArgumentException($"Couldn't find the first timestamp line in the current sub file. Last line read: '{line}', line number #{lineNumber}.");
				}
			}
			else
			{
				throw new ArgumentException("Stream is not in a valid SubViewer format");
			}
		}

		// ValueTuple
		private static (int startTime, int endTime) ParseTimecodeLine(string line)
		{
			string[] parts = line.Split(TimecodeSeparator);
			if (parts.Length == 2)
			{
				int start = ParserHelper.ParseTimeSpanLineAsMilliseconds(parts[0]);
				int end = ParserHelper.ParseTimeSpanLineAsMilliseconds(parts[1]);
				return (start, end);
			}
			else
			{
				throw new ArgumentException($"Couldn't parse the timecodes in line '{line}'.");
			}
		}

		/// <summary>
		/// Tests if the current line is a timestamp line
		/// </summary>
		/// <param name="line">The subtitle file line</param>
		/// <returns>True if it's a timestamp line, false otherwise</returns>
		private static bool IsTimestampLine(string line)
		{
			if (string.IsNullOrEmpty(line))
			{
				return false;
			}
			bool isMatch = _timestampRegex.IsMatch(line);
			return isMatch;
		}
	}

	/// <summary>
	/// Represents a parsed SubViewer subtitle part before conversion to SubtitleModel
	/// </summary>
	internal class SubViewerSubtitlePart
	{
		public string TimecodeLine { get; set; } = string.Empty;
		public List<string> TextLines { get; set; } = new List<string>();
	}
}
