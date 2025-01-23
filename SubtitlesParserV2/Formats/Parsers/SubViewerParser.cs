using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;
using SubtitlesParserV2.Helpers;
using SubtitlesParserV2.Models;

namespace SubtitlesParserV2.Formats.Parsers
{
	/// <summary>
	/// Parser for SubViewer .sbv subtitles files
	/// Support: SubViewer1 and SubViewer2
	/// 
	/// [INFORMATION]
	/// ....
	/// 
	/// 00:04:35.03,00:04:38.82
	/// Hello guys... please sit down...
	/// 
	/// 00:05:00.19,00:05:03.47
	/// M. Franklin,[br]are you crazy?
	/// 
	/// see https://wiki.videolan.org/SubViewer/
	/// https://docs.fileformat.com/settings/sbv/
	/// </summary>
	internal class SubViewerParser : ISubtitlesParser
	{
		// Properties ----------------------------------------------------------

		private const string SubViewer1InfoHeader = "[INFORMATION]";
		private const string SubViewer1SubtitleHeader = "[SUBTITLE]";
		private const string SubViewer2NewLine = "[br]";
		private const short MaxLineNumberForItems = 20;

		private static readonly Regex _timestampRegex = new Regex(@"\d{1,6}:\d{2}:\d{2}\.\d{2,10},\d{1,6}:\d{2}:\d{2}\.\d{2,10}", RegexOptions.Compiled);
		private const char TimecodeSeparator = ',';

		// Methods -------------------------------------------------------------

		public List<SubtitleModel> ParseStream(Stream subStream, Encoding encoding)
		{
			StreamHelper.ThrowIfStreamIsNotSeekableOrReadable(subStream);
			// seek the beginning of the stream
			subStream.Position = 0;
			// Create a StreamReader & configure it to leave the main stream open when disposing
			using StreamReader reader = new StreamReader(subStream, encoding, true, 1024, true);

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

				// Here, the line is a timestamp line (due to our previous loop), except it we exceded the hard-coded
				// max number of lines read for SubViewer1
				if (line != null && lineNumber <= MaxLineNumberForItems)
				{
					// Store final subtitles
					List<SubtitleModel> items = new List<SubtitleModel>();

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
								// Get previous timestamp line time
								(int, int) timeCodes = ParseTimecodeLine(lastTimecodeLine);
								int start = timeCodes.Item1;
								int end = timeCodes.Item2;

								// Ensure the timecode is valid and that we have at least 1 text line to add
								if (start > 0 && end > 0 && textLines.Count >= 1)
								{
									items.Add(new SubtitleModel()
									{
										StartTime = start,
										EndTime = end,
										Lines = textLines,
									});
								}

								// Update the previous timestamp line to current timecode line and reset text lines
								lastTimecodeLine = line;
								textLines = new List<string>();
							}
							else
							{
								/* SubViewer2 uses "[br]" to define new lines, so line = multiple lines
								* SubViewer1 used to separate the lines in the file, so line = unique line
								*/
								// Store the text line
								if (line.Contains(SubViewer2NewLine)) // SubViewer2
								{
									textLines.AddRange(line.Split(SubViewer2NewLine).Select(realLine => realLine.Trim()));
								}
								else // SubViewer1
								{
									textLines.Add(line.Trim());
								}
							}
						}
					}

					// If any text lines are left, we add them under the last known valid timecode line
					if (textLines.Count >= 1)
					{
						(int, int) lastTimeCodes = ParseTimecodeLine(lastTimecodeLine);
						int lastStart = lastTimeCodes.Item1;
						int lastEnd = lastTimeCodes.Item2;
						if (lastStart > 0 && lastEnd > 0)
						{
							items.Add(new SubtitleModel()
							{
								StartTime = lastStart,
								EndTime = lastEnd,
								Lines = textLines
							});
						}
					}

					if (items.Count >= 1)
					{
						return items;
					}
					else
					{
						throw new ArgumentException("Stream is not in a valid SubViewer format");
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
		private static (int, int) ParseTimecodeLine(string line)
		{
			string[] parts = line.Split(TimecodeSeparator);
			if (parts.Length == 2)
			{
				int start = ParseTimecode(parts[0]);
				int end = ParseTimecode(parts[1]);
				return (start, end);
			}
			else
			{
				throw new ArgumentException($"Couldn't parse the timecodes in line '{line}'.");
			}
		}

		/// <summary>
		/// Takes an SRT timecode as a string and parses it into a double (in seconds). A SRT timecode reads as follows: 
		/// 00:00:20,000
		/// </summary>
		/// <param name="s">The timecode to parse</param>
		/// <returns>The parsed timecode as a TimeSpan instance. If the parsing was unsuccessful, -1 is returned (subtitles should never show)</returns>
		private static int ParseTimecode(string s)
		{
			TimeSpan result;

			if (TimeSpan.TryParse(s, out result))
			{
				int nbOfMs = (int)result.TotalMilliseconds;
				return nbOfMs;
			}
			else
			{
				return -1;
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
}
