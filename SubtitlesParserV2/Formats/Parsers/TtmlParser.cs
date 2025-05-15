using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using System.Xml;
using SubtitlesParserV2.Helpers;
using SubtitlesParserV2.Models;


namespace SubtitlesParserV2.Formats.Parsers
{
	/// <summary>
	/// Parser for .ttml / .dfxp files.
	/// </summary>
	/// File Formats: Support TTML 1.0 and 2.0, dfxp (Distribution Format Exchange Profile) uses TTML 1.0
	/// <!--
	/// Sources:
	/// https://www.w3.org/TR/2018/REC-ttml1-20181108/#vocabulary-namespaces
	/// -->
	internal class TtmlParser : ISubtitlesParser
    {
		public List<SubtitleModel> ParseStream(Stream xmlStream, Encoding encoding)
        {
			StreamHelper.ThrowIfStreamIsNotSeekableOrReadable(xmlStream);
			// seek the beginning of the stream
			xmlStream.Position = 0;
			List<SubtitleModel> items = new List<SubtitleModel>();

			// Read the xml stream line by line
			using XmlReader reader = XmlReader.Create(xmlStream, new XmlReaderSettings { IgnoreWhitespace = true, IgnoreComments = true });

			// Value for parsing timecode for ttml file using ticks only, null by default (disabled)
			long? definedTickRate = null;
			// Value for parsing timecode for ttml files using frames, null by default (disabled)
			int? definedFrameRate = null;
			(int numerator, int denumerator)? definedFrameRateMultiplier = null;

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
							if (reader.LocalName == "tickRate" && long.TryParse(reader.Value, out long parsedTickRate))
							{
								definedTickRate = parsedTickRate;
							}
							else if (reader.LocalName == "frameRate" && int.TryParse(reader.Value, out int parsedFrameRate))
							{
								definedFrameRate = parsedFrameRate;
							}
							else if (reader.LocalName == "frameRateMultiplier" && !string.IsNullOrEmpty(reader.Value))
							{
								string[] parts = reader.Value.Split(new char[' '], StringSplitOptions.RemoveEmptyEntries);
								if (parts.Length == 2 && int.TryParse(parts[0], out int numerator) && int.TryParse(parts[1], out int denumerator))
								{
									// Check if the frameRateMultiplier is a valid for future "/" operation
									if (numerator == 0 || denumerator == 0) throw new FormatException($"Invalid frameRateMultiplier value: {reader.Value}. Zero was found, cannot divide.");
									definedFrameRateMultiplier = (numerator, denumerator);
								}
							}
						}
						reader.MoveToElement(); // Set our reader back to the <tt> element
					}

					// Parse the <p> element (subtitle)
					if (reader.LocalName == "p")
					{
						// Parse time
						string beginString = reader.GetAttribute("begin") ?? string.Empty;
						int startMs = ParseTimecode(beginString, definedTickRate, definedFrameRate, definedFrameRateMultiplier);

						string endString = reader.GetAttribute("end") ?? string.Empty;
						int endMs = ParseTimecode(endString, definedTickRate, definedFrameRate, definedFrameRateMultiplier);

						// Parse subtitle text
						string text = ParserHelper.XmlReadCurrentElementInnerText(reader);
						if (text != null)
						{
							items.Add(new SubtitleModel()
							{
								StartTime = startMs,
								EndTime = endMs,
								Lines = new List<string> { text }
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
		/// <param name="tickRate">If found in the file ttp namespace, the tickRate used for time using Ticks format.</param>
		/// <param name="frameRate">If found in the file ttp namespace, the frameRate used for time using Frames format.</param>
		/// <param name="frameRateMultiplier">If found in the file ttp namespace, the frameRateMultiplier applied to Frames format.</param>
		/// <returns>The parsed string timecode in milliseconds. If the parsing was unsuccessful, -1 is returned</returns>
		private static int ParseTimecode(string s, long? tickRate = 10000000, int? frameRate = 24, (int numerator, int denumerator)? frameRateMultiplier = null)
        {
            // Ensure null values get a "default" value
            tickRate = tickRate.HasValue ? tickRate : 10000000;
			frameRate = frameRate.HasValue ? frameRate : 24;
			frameRateMultiplier = frameRateMultiplier.HasValue ? frameRateMultiplier : null;


			// Get time in "00:01:05.500" or "5.0s" format (TimeSpan format)
			if (TimeSpan.TryParse(s, out TimeSpan result))
            {
                return (int)result.TotalMilliseconds;
            }
			// Get time in 79249170t format (ticks), this format is used by netflix for example.
			// According to https://www.w3.org/TR/ttml1/#parameter-attribute-tickRate, the tickRate is egual to 1 segonds.
			if (s.EndsWith('t') && long.TryParse(s.TrimEnd('t'), out long ticks))
            {
                // Divide ticks by it's tickRate (defined by the person who made that specific ttml file),
                // result is the time in segonds, we convert it into MS.
				return (int)TimeSpan.FromSeconds(ticks / tickRate.Value).TotalMilliseconds;
            }

			// Frame based time https://www.w3.org/TR/ttml1/#parameter-attribute-frameRate
			if (s.EndsWith("f") && long.TryParse(s.TrimEnd('f'), out long frames))
			{
				// Calculate effective fps
				double effectiveFps = frameRate.Value;
				// If a frameRateMultiplier is defined, we need to apply it to the fps
				if (frameRateMultiplier.HasValue)
				{
					effectiveFps *= (frameRateMultiplier.Value.numerator / (double)frameRateMultiplier.Value.denumerator);
				}

				// frames / fps = seconds
				double seconds = frames / effectiveFps;
				return (int)TimeSpan.FromSeconds(seconds).TotalMilliseconds;
			}
			return -1;
        }
    }
}