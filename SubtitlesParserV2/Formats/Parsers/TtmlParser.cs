using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using System.Xml;
using System.Xml.Linq;
using Microsoft.Extensions.Logging;
using SubtitlesParserV2.Helpers;
using SubtitlesParserV2.Logger;
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
		private static readonly Type CurrentType = typeof(TtmlParser);
		// Alternative for static class, create a logger with the full namespace name
		private static readonly ILogger _logger = LoggerManager.GetLogger(CurrentType.FullName ?? CurrentType.Name);

		public List<SubtitleModel> ParseStream(Stream xmlStream, Encoding encoding)
        {
			StreamHelper.ThrowIfStreamIsNotSeekableOrReadable(xmlStream);
			// seek the beginning of the stream
			xmlStream.Position = 0;
			List<SubtitleModel> items = new List<SubtitleModel>();

            // parse xml stream
            XElement xElement = XElement.Load(xmlStream);
            XNamespace tt = xElement.GetNamespaceOfPrefix("tt") ?? xElement.GetDefaultNamespace();
			XNamespace? ttp = xElement.GetNamespaceOfPrefix("ttp");
			// Value for parsing timecode for ttml file using ticks only, null by default (disabled)
			long? definedTickRate = null;

            // Verify for optional file specific values like tickRate
			if (ttp != null) 
            {
                // tickRate
                if (long.TryParse(xElement.Attribute(ttp + "tickRate")?.Value, out long parsed_definedTickRate))
                    definedTickRate = parsed_definedTickRate;
			}
            
			IEnumerable<XElement> nodeList = xElement.Descendants(tt + "p");
            foreach (XElement node in nodeList)
            {
                try
                {
                    string beginString = node.Attribute("begin")?.Value ?? string.Empty;
                    int startTicks = ParseTimecode(beginString, definedTickRate);
                    string endString = node.Attribute("end")?.Value ?? string.Empty;
                    int endTicks = ParseTimecode(endString, definedTickRate);

                    string text = node.Value.Trim();

                    items.Add(new SubtitleModel()
                    {
                        StartTime = startTicks,
                        EndTime = endTicks,
                        Lines = new List<string> { text }
                    });
                }
                catch (Exception ex)
                {
					_logger.LogDebug(ex, "Exception raised when parsing xml node {node}: {ex}", node, ex.Message);
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
		/// 00:00:20,000
		/// 79249170t (ticks version, dependant on tickRate)
		/// </code>
		/// </remarks>
		/// <param name="s">The timecode to parse</param>
		/// <param name="tickRate">If found in the file ttp namespace, the tickRate used for time using Ticks format.</param>
		/// <returns>The parsed string timecode in milliseconds. If the parsing was unsuccessful, -1 is returned</returns>
		private static int ParseTimecode(string s, long? tickRate = 10000000)
        {
            // Ensure null values get a "default" value
            tickRate = tickRate.HasValue ? tickRate : 10000000;

			// Get time in 00:00:20,000 format (TimeSpan format)
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
			// TODO : Add support for frame based time https://www.w3.org/TR/ttml1/#parameter-attribute-frameRate
			return -1;
        }
    }
}