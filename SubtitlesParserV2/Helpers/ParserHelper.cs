using System;

namespace SubtitlesParserV2.Helpers
{
    internal static class ParserHelper
    {
		/// <summary>
		/// Takes an string, prase it as a <see cref="TimeSpan"/> timecode and turn it into milliseconds.
		/// </summary>
		/// <remarks>
		/// <strong>Only uses this method when the line you are reading does not require format specific formatting.</strong>
		/// </remarks>
		/// <param name="s">The string to parse</param>
		/// <returns>The parsed timecode in milliseconds. If the parsing was unsuccessful, -1 is returned.</returns>
		internal static int ParseTimeSpanLineAsMilliseconds(string s)
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
	}
}
