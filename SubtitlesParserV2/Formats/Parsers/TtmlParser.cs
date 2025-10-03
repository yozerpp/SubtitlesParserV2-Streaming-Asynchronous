using System;
using System.Buffers.Text;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Runtime.CompilerServices;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;
using System.Xml;
using SubtitlesParserV2.Helpers;
using SubtitlesParserV2.Models;


namespace SubtitlesParserV2.Formats.Parsers
{
	/// <summary>
	/// Parser for .ttml / .dfxp / .itt files.
	/// NOTE: Does note support frame drop.
	/// </summary>
	/// File Formats: Support TTML 1.0 and 2.0, dfxp (Distribution Format Exchange Profile) uses TTML 1.0, iTTML (iTunes Timed Text Markup Language) is based on TTML 1.0.
	/// <!--
	/// Sources:
	/// https://www.w3.org/TR/2018/REC-ttml1-20181108/#vocabulary-namespaces
	/// 
	/// Additional sources:
	/// https://help.apple.com/itc/filmspec/#/apdATD1E199-D1E1A1303-D1E199A1126
	/// https://www.eztitles.com/Webhelp/EZConvert/export_subtitles_itt.htm
	/// -->
	internal class TtmlParser : ISubtitlesParser<TtmlSubtitlePart>
	{
		// Format "HH:MM:SS:FF" (SMPTE)
		// NOTE: We create a group for sub frames (so we can still parse without issue), but don't actually uses them,
		// so time will be some MS off for thoses, if you implement subFrame, feel free to make a pull request.
		private static readonly Regex SmpteRegex = new Regex(@"^(?<hours>\d+):(?<minutes>\d{2}):(?<seconds>\d{2}):(?<frames>\d+)(?:\.(?<subFrames>\d+))?$", RegexOptions.Compiled);

		private const string BadFormatMsg = "Stream is not in a valid TTML format, or represents empty subtitles";

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

			IEnumerable<TtmlSubtitlePart> parts = GetParts(xmlStream, encoding).Peekable(out var partsAny);
			if (!partsAny)
				throw new FormatException(BadFormatMsg);

			bool first = true;
			foreach (TtmlSubtitlePart part in parts)
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
			await foreach (TtmlSubtitlePart part in parts.WithCancellation(cancellationToken))
			{
				yield return ParsePart(part, first);
				first = false;
			}
		}

		public IEnumerable<TtmlSubtitlePart> GetParts(Stream stream, Encoding encoding)
		{
			using XmlReader xmlReader = XmlReader.Create(stream, new XmlReaderSettings { IgnoreWhitespace = true, IgnoreComments = true });
			foreach (var part in GetPartsFromXmlReader(xmlReader))
			{
				yield return part;
			}
		}

		public async IAsyncEnumerable<TtmlSubtitlePart> GetPartsAsync(Stream stream, Encoding encoding, [EnumeratorCancellation] CancellationToken cancellationToken = default)
		{
			using XmlReader xmlReader = XmlReader.Create(stream, new XmlReaderSettings { IgnoreWhitespace = true, IgnoreComments = true, Async = true });
			await foreach (var part in GetPartsFromXmlReaderAsync(xmlReader, cancellationToken))
			{
				yield return part;
			}
		}

		private IEnumerable<TtmlSubtitlePart> GetPartsFromXmlReader(XmlReader reader)
		{
			// Value for parsing timecode for ttml file using ticks only, null by default (disabled)
			long? definedTickRate = null;
			// Value for parsing timecode for ttml files using frames, null by default (disabled)
			int? definedFrameRate = null;
			(int numerator, int denumerator)? definedFrameRateMultiplier = null;
			// Value that says if we should try parsing SMPTE timecode on the file, null by default (disabled) (disabled to prevent conflict with normal timecode unless we detect SMPTE in header)
			bool trySmpteTimeBase = false;

			while (reader.Read())
			{
				if (reader.NodeType == XmlNodeType.Element)
				{
					// Parse the <tt> element attributes to find the tickRate
					if (reader.Name == "tt" && reader.HasAttributes)
					{
						// Loop through the attributes of the <tt> element
						while (reader.MoveToNextAttribute())
						{
							switch (reader.LocalName)
							{
								case "tickRate" when long.TryParse(reader.Value, out long parsedTickRate):
									definedTickRate = parsedTickRate;
									break;
								case "frameRate" when int.TryParse(reader.Value, out int parsedFrameRate):
									definedFrameRate = parsedFrameRate;
									break;
								case "frameRateMultiplier" when !string.IsNullOrEmpty(reader.Value):
									string[] parts = reader.Value.Split(new char[' '], StringSplitOptions.RemoveEmptyEntries);
									if (parts.Length == 2 && int.TryParse(parts[0], out int numerator) && int.TryParse(parts[1], out int denumerator))
									{
										// Check if the frameRateMultiplier is a valid for future "/" operation
										if (numerator == 0 || denumerator == 0) throw new FormatException($"Invalid frameRateMultiplier value: {reader.Value}. Zero was found, cannot divide.");
										definedFrameRateMultiplier = (numerator, denumerator);
									}
									break;
								case "timeBase" when reader.Value.Equals("smpte", StringComparison.InvariantCultureIgnoreCase):
									trySmpteTimeBase = true;
									break;
							}
						}
						reader.MoveToElement(); // Set our reader back to the <tt> element
					}

					// Parse the <p> element (subtitle)
					if (reader.LocalName == "p")
					{
						string beginString = reader.GetAttribute("begin") ?? string.Empty;
						string endString = reader.GetAttribute("end") ?? string.Empty;
						string durString = reader.GetAttribute("dur") ?? string.Empty;

						List<string> textLines = ParserHelper.XmlReadCurrentElementInnerText(reader);

						yield return new TtmlSubtitlePart
						{
							BeginAttribute = beginString,
							EndAttribute = endString,
							DurationAttribute = durString,
							TextLines = textLines,
							TickRate = definedTickRate,
							FrameRate = definedFrameRate,
							FrameRateMultiplier = definedFrameRateMultiplier,
							TrySmpteTimeBase = trySmpteTimeBase
						};
					}
				}
			}
		}

		private async IAsyncEnumerable<TtmlSubtitlePart> GetPartsFromXmlReaderAsync(XmlReader reader, [EnumeratorCancellation] CancellationToken cancellationToken = default)
		{
			// Value for parsing timecode for ttml file using ticks only, null by default (disabled)
			long? definedTickRate = null;
			// Value for parsing timecode for ttml files using frames, null by default (disabled)
			int? definedFrameRate = null;
			(int numerator, int denumerator)? definedFrameRateMultiplier = null;
			// Value that says if we should try parsing SMPTE timecode on the file, null by default (disabled) (disabled to prevent conflict with normal timecode unless we detect SMPTE in header)
			bool trySmpteTimeBase = false;

			while (await reader.ReadAsync())
			{
				cancellationToken.ThrowIfCancellationRequested();

				if (reader.NodeType == XmlNodeType.Element)
				{
					// Parse the <tt> element attributes to find the tickRate
					if (reader.Name == "tt" && reader.HasAttributes)
					{
						// Loop through the attributes of the <tt> element
						while (reader.MoveToNextAttribute())
						{
							switch (reader.LocalName)
							{
								case "tickRate" when long.TryParse(reader.Value, out long parsedTickRate):
									definedTickRate = parsedTickRate;
									break;
								case "frameRate" when int.TryParse(reader.Value, out int parsedFrameRate):
									definedFrameRate = parsedFrameRate;
									break;
								case "frameRateMultiplier" when !string.IsNullOrEmpty(reader.Value):
									string[] parts = reader.Value.Split(new char[' '], StringSplitOptions.RemoveEmptyEntries);
									if (parts.Length == 2 && int.TryParse(parts[0], out int numerator) && int.TryParse(parts[1], out int denumerator))
									{
										// Check if the frameRateMultiplier is a valid for future "/" operation
										if (numerator == 0 || denumerator == 0) throw new FormatException($"Invalid frameRateMultiplier value: {reader.Value}. Zero was found, cannot divide.");
										definedFrameRateMultiplier = (numerator, denumerator);
									}
									break;
								case "timeBase" when reader.Value.Equals("smpte", StringComparison.InvariantCultureIgnoreCase):
									trySmpteTimeBase = true;
									break;
							}
						}
						reader.MoveToElement(); // Set our reader back to the <tt> element
					}

					// Parse the <p> element (subtitle)
					if (reader.LocalName == "p")
					{
						string beginString = reader.GetAttribute("begin") ?? string.Empty;
						string endString = reader.GetAttribute("end") ?? string.Empty;
						string durString = reader.GetAttribute("dur") ?? string.Empty;

						List<string> textLines = ParserHelper.XmlReadCurrentElementInnerText(reader);

						yield return new TtmlSubtitlePart
						{
							BeginAttribute = beginString,
							EndAttribute = endString,
							DurationAttribute = durString,
							TextLines = textLines,
							TickRate = definedTickRate,
							FrameRate = definedFrameRate,
							FrameRateMultiplier = definedFrameRateMultiplier,
							TrySmpteTimeBase = trySmpteTimeBase
						};
					}
				}
			}
		}

		public SubtitleModel ParsePart(TtmlSubtitlePart part, bool isFirstPart)
		{
			// Parse time
			int startMs = ParseTimecode(part.BeginAttribute, part.TrySmpteTimeBase, part.TickRate, part.FrameRate, part.FrameRateMultiplier);
			int endMs = startMs; // Default value if not found is end time same as start time

			// If no end string, try to look for a duration string
			if (string.IsNullOrEmpty(part.EndAttribute))
			{
				// Sometime time has begin and duration instead of begin and end time
				if (!string.IsNullOrEmpty(part.DurationAttribute))
				{
					// Duration in ms + start time in ms = end time in ms
					endMs = ParseTimecode(part.DurationAttribute, part.TrySmpteTimeBase, part.TickRate, part.FrameRate, part.FrameRateMultiplier) + startMs;
				}
			}
			else
			{
				endMs = ParseTimecode(part.EndAttribute, part.TrySmpteTimeBase, part.TickRate, part.FrameRate, part.FrameRateMultiplier);
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
		/// Takes an TTML timecode as a string and parses it into a int (millisegonds).
		/// </summary>
		/// <remarks>
		/// A TTML timecode can reads as follows: 
		/// <code>
		/// 5.0s (default version, based on time in seconds)
		/// 79249170t (ticks version, dependant on tickRate)
		/// </code>
		/// </remarks>
		/// <param name="s">The timecode to parse</param>
		/// <param name="trySmpteTimeBase">True if we should try to parse the timecode as a SMPTE timecode (HH:MM:SS:FF). Will be done before checking for normal timestamp (default behavior)</param>
		/// <param name="tickRate">If found in the file ttp namespace, the tickRate used for time using Ticks format.</param>
		/// <param name="frameRate">If found in the file ttp namespace, the frameRate used for time using Frames format.</param>
		/// <param name="frameRateMultiplier">If found in the file ttp namespace, the frameRateMultiplier applied to Frames format.</param>
		/// <returns>The parsed string timecode in milliseconds. If the parsing was unsuccessful, -1 is returned</returns>
		private static int ParseTimecode(string s, bool trySmpteTimeBase = false, long? tickRate = 10000000, int? frameRate = 24, (int numerator, int denumerator)? frameRateMultiplier = null)
		{
			// Ensure null values get a "default" value
			tickRate = tickRate.HasValue ? tickRate : 10000000;
			frameRate = frameRate.HasValue ? frameRate : 24;
			frameRateMultiplier = frameRateMultiplier.HasValue ? frameRateMultiplier : null;

			// Get the last char of the time
			char lastChar = s.Substring(s.Length - 1)[0];

			switch (lastChar)
			{
				// Handle MS and S
				case 's':
					// Millisecond based "500ms" (need to ensure our string end with ms)
					if (s.EndsWith("ms") && double.TryParse(s.TrimEnd('m', 's'), NumberStyles.AllowDecimalPoint, CultureInfo.InvariantCulture, out double msTime))
					{
						return (int)msTime;
					} // Seconds based "5.0s"
					else if (double.TryParse(s.TrimEnd('s'), NumberStyles.AllowDecimalPoint, CultureInfo.InvariantCulture, out double secondsTime))
					{
						return (int)TimeSpan.FromSeconds(secondsTime).TotalMilliseconds;
					}
					break;
				// Handle Minutes "1.5m"
				case 'm':
					if (double.TryParse(s.TrimEnd('m'), NumberStyles.AllowDecimalPoint, CultureInfo.InvariantCulture, out double minutes))
					{
						return (int)TimeSpan.FromMinutes(minutes).TotalMilliseconds;
					}
					break;
				// Handle Hours "1.25h"
				case 'h':
					if (double.TryParse(s.TrimEnd('h'), NumberStyles.AllowDecimalPoint, CultureInfo.InvariantCulture, out double hours))
					{
						return (int)TimeSpan.FromHours(hours).TotalMilliseconds;
					}
					break;
				// Handle Ticks based time
				case 't':
					// Get time in 79249170t format (ticks), this format is used by netflix for example.
					// According to https://www.w3.org/TR/ttml1/#parameter-attribute-tickRate, the tickRate is egual to 1 seconds.
					if (long.TryParse(s.TrimEnd('t'), out long ticks))
					{
						// Divide ticks by it's tickRate (defined by the person who made that specific ttml file),
						// result is the time in segonds, we convert it into MS.
						return (int)TimeSpan.FromSeconds(ticks / tickRate.Value).TotalMilliseconds;
					}
					break;
				// Handle Frames based time
				case 'f':
					// Frame based time https://www.w3.org/TR/ttml1/#parameter-attribute-frameRate
					if (long.TryParse(s.TrimEnd('f'), out long frames))
					{
						return ConvertFramesToMilliseconds(frames, frameRate.Value, frameRateMultiplier);
					}
					break;
				default:
					// Verify if we should try to parse the timecode as a SMPTE timecode
					if (trySmpteTimeBase && SmpteRegex.IsMatch(s))
					{
						Match match = SmpteRegex.Match(s);
						int hoursGroup = 0;
						int minutesGroup = 0;
						int secondsGroup = 0;
						int framesGroup = 0;

						bool parsingSuccess = int.TryParse(match.Groups["hours"].Value, out hoursGroup) &&
							int.TryParse(match.Groups["minutes"].Value, out minutesGroup) &&
							int.TryParse(match.Groups["seconds"].Value, out secondsGroup) &&
							int.TryParse(match.Groups["frames"].Value, out framesGroup);

						if (parsingSuccess)
						{
							return (int)new TimeSpan(hoursGroup, minutesGroup, secondsGroup).TotalMilliseconds + ConvertFramesToMilliseconds(framesGroup, frameRate.Value, frameRateMultiplier);
						}
					}
					// Assume it's a TimeSpan format
					// Get time in "00:01:05.500" (TimeSpan format)
					else if (TimeSpan.TryParse(s, out TimeSpan result))
					{
						return (int)result.TotalMilliseconds;
					}
					break;
			}

			return -1;
		}

		/// <summary>
		/// Convert frames to milliseconds.
		/// </summary>
		/// <param name="frames">The frame to convert</param>
		/// <param name="frameRate">The ttml file framerate (FPS)</param>
		/// <param name="frameRateMultiplier">A frame rate multiplier (if any), optional</param>
		/// <returns>The time in milliseconds</returns>
		private static int ConvertFramesToMilliseconds(long frames, int frameRate, (int numerator, int denumerator)? frameRateMultiplier = null)
		{
			// Calculate effective fps
			double effectiveFps = frameRate;
			// If a frameRateMultiplier is defined, we need to apply it to the fps
			if (frameRateMultiplier.HasValue)
			{
				effectiveFps *= (frameRateMultiplier.Value.numerator / (double)frameRateMultiplier.Value.denumerator);
			}

			// frames / fps = seconds
			double seconds = frames / effectiveFps;
			return (int)TimeSpan.FromSeconds(seconds).TotalMilliseconds;
		}
	}

	/// <summary>
	/// Represents a parsed TTML subtitle part before conversion to SubtitleModel
	/// </summary>
	internal class TtmlSubtitlePart
	{
		public string BeginAttribute { get; set; } = string.Empty;
		public string EndAttribute { get; set; } = string.Empty;
		public string DurationAttribute { get; set; } = string.Empty;
		public List<string> TextLines { get; set; } = new List<string>();
		public long? TickRate { get; set; }
		public int? FrameRate { get; set; }
		public (int numerator, int denumerator)? FrameRateMultiplier { get; set; }
		public bool TrySmpteTimeBase { get; set; }
	}
}
