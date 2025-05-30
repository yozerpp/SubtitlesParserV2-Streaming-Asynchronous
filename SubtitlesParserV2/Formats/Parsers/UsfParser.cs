using SubtitlesParserV2.Helpers;
using SubtitlesParserV2.Models;
using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using System.Text.RegularExpressions;
using System.Xml;

namespace SubtitlesParserV2.Formats.Parsers
{
	/// <summary>
	/// Parser for .usf files.
	/// </summary>
	/// File Formats: v1.1
	/// <!--
	/// Sources:
	/// https://www.titlevision.dk/usf-file-format/
	/// https://subtitld.org/en/development/usf
	/// -->
	internal class UsfParser : ISubtitlesParser
	{
		// hh:mm:ss:mmm
		private static readonly Regex BaseTimestampFormat = new Regex(@"^(?:(?<hours>\d+)):(?<minutes>[0-5]\d):(?<seconds>[0-5]\d)(?:\.(?<milliseconds>\d+))?$", RegexOptions.Compiled);
		// ss[.mmm]
		private static readonly Regex ShortTimestampFormat = new Regex(@"^(?<seconds>\d+)(?:\.(?<millisecond>\d+))?$", RegexOptions.Compiled);

		public List<SubtitleModel> ParseStream(Stream xmlStream, Encoding encoding)
		{
			StreamHelper.ThrowIfStreamIsNotSeekableOrReadable(xmlStream);
			// seek the beginning of the stream
			xmlStream.Position = 0;
			List<SubtitleModel> items = new List<SubtitleModel>();

			// Read the xml stream line by line
			using XmlReader reader = XmlReader.Create(xmlStream, new XmlReaderSettings { IgnoreWhitespace = true, IgnoreComments = true });

			while (reader.Read())
			{
				if (reader.NodeType == XmlNodeType.Element)
				{
					// Ensure the root element matches the definition of USF files
					if (reader.Depth == 0 && !reader.Name.Equals("USFSubtitles", StringComparison.OrdinalIgnoreCase))
					{
						throw new ArgumentException("Stream is not in a valid USF format (root element is not USFSubtitles)");
					}


					// Parse the <subtitle> element
					if (reader.LocalName == "subtitle")
					{
						// Parse time
						string startString = reader.GetAttribute("start") ?? string.Empty;
						int startMs = ParseTimecode(startString);

						string endString = reader.GetAttribute("stop") ?? string.Empty;
						int endMs = startMs; // Default value if not found is end time same as start time

						// If no end string, try to look for a duration string
						if (string.IsNullOrEmpty(endString))
						{
							// Sometime time has start and duration instead of start and end time
							string durString = reader.GetAttribute("duration") ?? string.Empty;
							if (!string.IsNullOrEmpty(durString))
							{
								// Duration in ms + start time in ms = end time in ms
								endMs = ParseTimecode(durString) + startMs;
							}
						}
						else endMs = ParseTimecode(endString);



						// Parse subtitle text
						List<string> textLines = ParserHelper.XmlReadCurrentElementInnerText(reader);
						if (textLines.Count >= 1)
						{
							items.Add(new SubtitleModel()
							{
								StartTime = startMs,
								EndTime = endMs,
								Lines = textLines
							});
						}
					}
				}
			}

			if (items.Count >= 1)
			{
				return items;
			}
			throw new ArgumentException("Stream is not in a valid TTML format, or represents empty subtitles");
			
		}

		/// <summary>
		/// Takes an USF timecode as a string and parses it into a int (millisegonds).
		/// </summary>
		/// <remarks>
		/// A USF timecode can reads as follows: 
		/// <code>
		/// hh:mm:ss.mmm (00-23:00-59:00-59:000-999)
		/// ss[.mmm] (100 would convert to 00:01:40.000) (1,100 would convert to 00:00:01.100)
		/// </code>
		/// </remarks>
		/// <param name="s">The timecode to parse</param>
		/// <returns>The parsed string timecode in milliseconds. If the parsing was unsuccessful, -1 is returned</returns>
		private static int ParseTimecode(string s)
		{
			int hoursGroup = 0;
			int minutesGroup = 0;
			int secondsGroup = 0;
			int millisecondsGroup = 0;

			// Try parse hh:mm:ss.mmm format
			Match regex = BaseTimestampFormat.Match(s);
			if (regex.Success) 
			{
				bool parsingSuccess = int.TryParse(regex.Groups["hours"].Value, out hoursGroup) &&
					int.TryParse(regex.Groups["minutes"].Value, out minutesGroup) &&
					int.TryParse(regex.Groups["seconds"].Value, out secondsGroup) &&
					int.TryParse(regex.Groups["milliseconds"].Value, out millisecondsGroup);

				if (parsingSuccess)
				{
					return (int)new TimeSpan(0, hoursGroup, minutesGroup, secondsGroup, millisecondsGroup).TotalMilliseconds;
				}
			}
			else // Fall back to short ss[.mmm] format
			{
				regex = ShortTimestampFormat.Match(s);
				if (regex.Success)
				{
					bool parsingSuccess = int.TryParse(regex.Groups["seconds"].Value, out secondsGroup) &&
						int.TryParse(regex.Groups["millisecond"].Value, out millisecondsGroup);

					if (parsingSuccess)
					{
						return (int)new TimeSpan(0, 0, 0, secondsGroup, millisecondsGroup).TotalMilliseconds;
					}
				}
			}

			return -1;
		}
	}
}
