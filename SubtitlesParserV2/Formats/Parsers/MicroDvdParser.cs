using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;
using SubtitlesParserV2.Models;

namespace SubtitlesParserV2.Formats.Parsers
{
	/// <summary>
	/// Configuration model for the MicroDVD parser.
	/// </summary>
	public class MicroDvdParserConfig 
    {
        /// <summary>
        /// If defined, will skip the automatic framerate detection and use
        /// this value when parsing the file.
        /// </summary>
        public float? Framerate { get; set; }
    }
	/// <summary>
	/// Parser for MicroDVD .sub subtitles files
	/// 
	/// A .sub file looks like this:
	/// {1}{1}29.970
	/// {0}{180}PIRATES OF THE CARIBBEAN|English subtitlez by tHe.b0dY
	/// {509}{629}Drink up me 'earties yo ho!
	/// {635}{755}We kidnap and ravage and don't give a hoot.
	/// 
	/// We need the video frame rate to extract .sub files -> careful when using it
	/// 
	/// see https://en.wikipedia.org/wiki/MicroDVD
	/// </summary>
	internal class MicroDvdParser : ISubtitlesParser, ISubtitlesParser<MicroDvdParserConfig>
	{

	    // Properties -----------------------------------------------------------------------

	    private const float defaultFrameRate = 25;
        private static readonly char[] _lineSeparators = { '|' };
		private static readonly string LineRegex = @"^[{\[](-?\d+)[}\]][{\[](-?\d+)[}\]](.*)";

		// Methods -------------------------------------------------------------------------

		public List<SubtitleModel> ParseStream(Stream subStream, Encoding encoding)
		{
			return ParseStream(subStream, encoding, new MicroDvdParserConfig());
		}

		public List<SubtitleModel> ParseStream(Stream subStream, Encoding encoding, MicroDvdParserConfig config)
        {
            // test if stream if readable and seekable (just a check, should be good)
            if (!subStream.CanRead || !subStream.CanSeek)
            {
                var message = string.Format("Stream must be seekable and readable in a subtitles parser. " +
                                   "Operation interrupted; isSeekable: {0} - isReadable: {1}",
                                   subStream.CanSeek, subStream.CanSeek);
                throw new ArgumentException(message);
            }

            // seek the beginning of the stream
            subStream.Position = 0;
            // Create a StreamReader & configure it to leave the main stream open when disposing
            using (var reader = new StreamReader(subStream, encoding, true, 1024, true))
            {
				List<SubtitleModel> items = new List<SubtitleModel>();
                string? line = reader.ReadLine();
                // find the first relevant line
                while (line != null && !IsMicroDvdLine(line))
                {
                    line = reader.ReadLine();
                }

                if (line != null)
                {
                    float frameRate;
                    if (config.Framerate.HasValue) // If a framerate was given when calling the method, use it
                    {
                        frameRate = config.Framerate.Value;
                    }
                    else 
                    {
						// try to extract the framerate from the first line
						SubtitleModel firstItem = ParseLine(line, defaultFrameRate);
						if (firstItem.Lines != null && firstItem.Lines.Count >= 1)
						{
							bool success = TryExtractFrameRate(firstItem.Lines[0], out frameRate);
							if (!success)
							{

								Debug.WriteLine("Couldn't extract frame rate of sub file with first line {0}. " +
												  "We use the default frame rate: {1}", line, defaultFrameRate);
								frameRate = defaultFrameRate;

								// We didn't find the framerate on the line, the line might be a subtitle so we add it to the list
								items.Add(firstItem);
							}
						}
						else
						{
							frameRate = defaultFrameRate;
						}
					}

                    // Parse other lines
                    line = reader.ReadLine();
                    while (line != null)
                    {
                        if (!string.IsNullOrEmpty(line))
                        {
                            SubtitleModel item = ParseLine(line, frameRate);
							items.Add(item);
                        }
                        line = reader.ReadLine();
                    }
                }

                if (items.Count != 0)
                {
                    return items;
                }
                else
                {
                    throw new ArgumentException("Stream is not in a valid MicroDVD format");
                }
            };
        }

        private static bool IsMicroDvdLine(string line)
        {
            return Regex.IsMatch(line, LineRegex);
        }

        /// <summary>
        /// Parses one line of the .sub file
        /// 
        /// ex:
        /// {0}{180}PIRATES OF THE CARIBBEAN|English subtitlez by tHe.b0dY
        /// </summary>
        /// <param name="line">The .sub file line</param>
        /// <param name="frameRate">The frame rate with which the .sub file was created</param>
        /// <returns>The corresponding SubtitleItem</returns>
        private static SubtitleModel ParseLine(string line, float frameRate)
        {
            Match match = Regex.Match(line, LineRegex);
            if (match.Success && match.Groups.Count > 2)
            {
                string startFrame = match.Groups[1].Value;
                int start = (int)(1000 * double.Parse(startFrame) / frameRate);
                string endTime = match.Groups[2].Value;
                int end = (int)(1000 * double.Parse(endTime) / frameRate);
                string text = match.Groups[match.Groups.Count - 1].Value;
                string[] lines = text.Split(_lineSeparators);
                List<string> nonEmptyLines = lines.Where(l => !string.IsNullOrEmpty(l)).ToList();
				SubtitleModel item = new SubtitleModel
                {
                    Lines = nonEmptyLines,
                    StartTime = start,
                    EndTime = end
                };

                return item;
            }
            else
            {
                throw new InvalidDataException($"The subtitle file line {line} is not in the micro dvd format. We stop the process.");
            }
        }

        /// <summary>
        /// Tries to extract the frame rate from a subtitle file line.
        /// 
        /// Supported formats are:
        /// - {x}{y}25
        /// - {x}{y}{...}23.976
        /// </summary>
        /// <param name="text">The subtitle file line</param>
        /// <param name="frameRate">The frame rate if we can parse it</param>
        /// <returns>True if the parsing was successful, false otherwise</returns>
        private static bool TryExtractFrameRate(string text, out float frameRate)
        {
            if (!string.IsNullOrEmpty(text))
            {
                bool success = float.TryParse(text, NumberStyles.AllowDecimalPoint, CultureInfo.InvariantCulture, out frameRate);
                return success;
            }
            else
            {
                frameRate = defaultFrameRate;
                return false;
            }
        }
	}
}