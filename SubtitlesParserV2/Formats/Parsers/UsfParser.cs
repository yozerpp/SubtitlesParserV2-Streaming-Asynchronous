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
	internal class UsfParser : ISubtitlesParser<UsfSubtitlePart>
	{
		// hh:mm:ss:mmm
		private static readonly Regex BaseTimestampFormat = new Regex(@"^(?:(?<hours>\d+)):(?<minutes>[0-5]\d):(?<seconds>[0-5]\d)(?:\.(?<milliseconds>\d+))?$", RegexOptions.Compiled);
		// ss[.mmm]
		private static readonly Regex ShortTimestampFormat = new Regex(@"^(?<seconds>\d+)(?:\.(?<millisecond>\d+))?$", RegexOptions.Compiled);

		private const string BadFormatMsg = "Stream is not in a valid USF format, or represents empty subtitles";

		public List<SubtitleModel> ParseStream(Stream xmlStream, Encoding encoding)
		{
			var ret = ParseStreamConsuming(xmlStream, encoding).ToList();
			if (ret.Count == 0) throw new FormatException(BadFormatMsg);
			return ret;
		}

		public async Task<List<SubtitleModel>> ParseStreamAsync(Stream stream, Encoding encoding, CancellationToken cancellationToken)
		{
			var ret = await ParseStreamConsumingAsync(stream, encoding, cancellationToken).ToListAsync(cancellationToken);
			if (ret.Count == 0) throw new FormatException(BadFormatMsg);
			return ret;
		}

		public IEnumerable<SubtitleModel> ParseStreamConsuming(Stream xmlStream, Encoding encoding)
		{
			StreamHelper.ThrowIfStreamIsNotSeekableOrReadable(xmlStream);
			// seek the beginning of the stream
			xmlStream.Position = 0;

			IEnumerable<UsfSubtitlePart> parts = GetParts(xmlStream, encoding).Peekable(out var partsAny);
			if (!partsAny)
				throw new FormatException(BadFormatMsg);

			bool first = true;
			foreach (UsfSubtitlePart part in parts)
			{
				yield return ParsePart(part, first);
				first = false;
			}
		}

		public async IAsyncEnumerable<SubtitleModel> ParseStreamConsumingAsync(Stream xmlStream, Encoding encoding, [EnumeratorCancellation] CancellationToken cancellationToken)
		{
			StreamHelper.ThrowIfStreamIsNotSeekableOrReadable(xmlStream);
			// seek the beginning of the stream
			xmlStream.Position = 0;

			var parts = GetPartsAsync(xmlStream, encoding, cancellationToken);
			var partsAny = await parts.PeekableAsync();
			if (!partsAny)
				throw new FormatException(BadFormatMsg);

			bool first = true;
			await foreach (UsfSubtitlePart part in parts.WithCancellation(cancellationToken))
			{
				yield return ParsePart(part, first);
				first = false;
			}
		}

		public IEnumerable<UsfSubtitlePart> GetParts(Stream stream, Encoding encoding)
		{
			using XmlReader xmlReader = XmlReader.Create(stream, new XmlReaderSettings { IgnoreWhitespace = true, IgnoreComments = true });
			foreach (var part in GetPartsFromXmlReader(xmlReader))
			{
				yield return part;
			}
		}

		public async IAsyncEnumerable<UsfSubtitlePart> GetPartsAsync(Stream stream, Encoding encoding, [EnumeratorCancellation] CancellationToken cancellationToken = default)
		{
			using XmlReader xmlReader = XmlReader.Create(stream, new XmlReaderSettings { IgnoreWhitespace = true, IgnoreComments = true, Async = true });
			await foreach (var part in GetPartsFromXmlReaderAsync(xmlReader, cancellationToken))
			{
				yield return part;
			}
		}

		private IEnumerable<UsfSubtitlePart> GetPartsFromXmlReader(XmlReader reader)
		{
			bool rootElementValidated = false;

			while (reader.Read())
			{
				if (reader.NodeType == XmlNodeType.Element)
				{
					// Ensure the root element matches the definition of USF files
					if (reader.Depth == 0 && !rootElementValidated)
					{
						if (!reader.Name.Equals("USFSubtitles", StringComparison.OrdinalIgnoreCase))
						{
							throw new FormatException("Stream is not in a valid USF format (root element is not USFSubtitles)");
						}
						rootElementValidated = true;
					}

					// Parse the <subtitle> element
					if (reader.LocalName == "subtitle")
					{
						string startString = reader.GetAttribute("start") ?? string.Empty;
						string endString = reader.GetAttribute("stop") ?? string.Empty;
						string durString = reader.GetAttribute("duration") ?? string.Empty;

						List<string> textLines = ParserHelper.XmlReadCurrentElementInnerText(reader);

						yield return new UsfSubtitlePart
						{
							StartAttribute = startString,
							EndAttribute = endString,
							DurationAttribute = durString,
							TextLines = textLines
						};
					}
				}
			}
		}

		private async IAsyncEnumerable<UsfSubtitlePart> GetPartsFromXmlReaderAsync(XmlReader reader, [EnumeratorCancellation] CancellationToken cancellationToken = default)
		{
			bool rootElementValidated = false;

			while (await reader.ReadAsync())
			{
				cancellationToken.ThrowIfCancellationRequested();

				if (reader.NodeType == XmlNodeType.Element)
				{
					// Ensure the root element matches the definition of USF files
					if (reader.Depth == 0 && !rootElementValidated)
					{
						if (!reader.Name.Equals("USFSubtitles", StringComparison.OrdinalIgnoreCase))
						{
							throw new FormatException("Stream is not in a valid USF format (root element is not USFSubtitles)");
						}
						rootElementValidated = true;
					}

					// Parse the <subtitle> element
					if (reader.LocalName == "subtitle")
					{
						string startString = reader.GetAttribute("start") ?? string.Empty;
						string endString = reader.GetAttribute("stop") ?? string.Empty;
						string durString = reader.GetAttribute("duration") ?? string.Empty;

						List<string> textLines = ParserHelper.XmlReadCurrentElementInnerText(reader);

						yield return new UsfSubtitlePart
						{
							StartAttribute = startString,
							EndAttribute = endString,
							DurationAttribute = durString,
							TextLines = textLines
						};
					}
				}
			}
		}

		public SubtitleModel ParsePart(UsfSubtitlePart part, bool isFirstPart)
		{
			// Parse time
			int startMs = ParseTimecode(part.StartAttribute);
			int endMs = startMs; // Default value if not found is end time same as start time

			// If no end string, try to look for a duration string
			if (string.IsNullOrEmpty(part.EndAttribute))
			{
				// Sometime time has start and duration instead of start and end time
				if (!string.IsNullOrEmpty(part.DurationAttribute))
				{
					// Duration in ms + start time in ms = end time in ms
					endMs = ParseTimecode(part.DurationAttribute) + startMs;
				}
			}
			else
			{
				endMs = ParseTimecode(part.EndAttribute);
			}

			// Parse subtitle text
			List<string> textLines = part.TextLines;
			if (textLines.Count >= 1)
			{
				return new SubtitleModel()
				{
					StartTime = startMs,
					EndTime = endMs,
					Lines = textLines
				};
			}

			// Return an empty subtitle if no text lines
			return new SubtitleModel()
			{
				StartTime = startMs,
				EndTime = endMs,
				Lines = new List<string>()
			};
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

	/// <summary>
	/// Represents a parsed USF subtitle part before conversion to SubtitleModel
	/// </summary>
	internal class UsfSubtitlePart
	{
		public string StartAttribute { get; set; } = string.Empty;
		public string EndAttribute { get; set; } = string.Empty;
		public string DurationAttribute { get; set; } = string.Empty;
		public List<string> TextLines { get; set; } = new List<string>();
	}
}
