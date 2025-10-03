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
	/// Parser for the .srt subtitles files
	/// </summary>
	/// <!--
	/// Sources:
	/// https://en.wikipedia.org/wiki/SubRip
	/// https://docs.fileformat.com/video/srt/
	/// Example:
	/// 1
	/// 00:00:10,500 --> 00:00:13,000
	/// Elephant's Dream
	///
	/// 2
	/// 00:00:15,000 --> 00:00:18,000
	/// At the left we can see...[12]
	/// --> 
	internal class SrtParser : ISubtitlesParser<string>
	{

		// Properties -----------------------------------------------------------------------

		private static readonly string[] _delimiters = { "-->", "- >", "->" };
		private static readonly string[] _newLineCharacters = { "\r\n", "\r", "\n" };

		private const string NoPartsMsg = "Parsing as srt returned no srt part.";

		private const string BadFormatMsg = "Stream is not in a valid Srt format";
		// Methods -------------------------------------------------------------------------

		public List<SubtitleModel> ParseStream(Stream srtStream, Encoding encoding)
		{
			var ret = ParseStreamConsuming(srtStream, encoding).ToList();
			if(ret.Count == 0) throw new FormatException(BadFormatMsg);
			return ret;
		}

		public async Task<List<SubtitleModel>> ParseStreamAsync(Stream stream, Encoding encoding, CancellationToken cancellationToken)
		{
			var ret = await ParseStreamConsumingAsync(stream, encoding, cancellationToken).ToListAsync(cancellationToken);
			if(ret.Count == 0) throw new FormatException(BadFormatMsg);
			return ret;
		}

		public IEnumerable<SubtitleModel> ParseStreamConsuming(Stream srtStream, Encoding encoding)
		{
			StreamHelper.ThrowIfStreamIsNotSeekableOrReadable(srtStream);
			// seek the beginning of the srtStream
			srtStream.Position = 0;

			// Create a StreamReader & configure it to leave the main srtStream open when disposing
			IEnumerable<string> srtSubParts = GetParts(srtStream, encoding).Peekable(out var srtSubPartsAny); // This is a lazy list, not yet into memory
			if(!srtSubPartsAny)
				throw new FormatException(NoPartsMsg);
			bool first = true;
			foreach (string part in srtSubParts)
			{
				yield return ParsePart(part, first);
				first = false;
			}
		}

		public async IAsyncEnumerable<SubtitleModel> ParseStreamConsumingAsync(Stream srtStream, Encoding encoding, [EnumeratorCancellation] CancellationToken cancellationToken)
		{
			StreamHelper.ThrowIfStreamIsNotSeekableOrReadable(srtStream);
			// seek the beginning of the srtStream
			srtStream.Position = 0;

			// Create a StreamReader & configure it to leave the main srtStream open when disposing

			var srtSubPartsOld = GetPartsAsync(srtStream, encoding, cancellationToken); // This is a lazy list, not yet into memory
			var (srtSubParts, srtSubPartsAny) = await srtSubPartsOld.PeekableAsync();
			if(!srtSubPartsAny) throw new FormatException(NoPartsMsg);
			await foreach (string part in srtSubParts)
				yield return ParsePart(part, srtSubPartsAny);
		}



		/// <summary>
		/// Enumerates the subtitle parts in a srt file based on the standard line break observed between them. 
		/// A srt subtitle part is in the form:
		/// <code>
		/// 1
		/// 00:00:20,000 --> 00:00:24,400
		/// Altocumulus clouds occur between six thousand
		/// </code>
		/// </summary>
		/// <returns>An IEnumerable(string) object containing all the subtitle parts</returns>
		public IEnumerable<string> GetParts(Stream stream, Encoding encoding)
		{
			var reader = new StreamReader(stream, encoding, true, 1024, true);
			string? line;
			StringBuilder stringBuilder = new StringBuilder();
			while ((line = reader.ReadLine()) != null)
			{
				if (string.IsNullOrEmpty(line.Trim()))
				{
					// return only if not empty
					string res = stringBuilder.ToString().TrimEnd();
					if (!string.IsNullOrEmpty(res))
					{
						yield return res;
					}
					stringBuilder = new StringBuilder();
				}
				else
				{
					stringBuilder.AppendLine(line);
				}
			}

			if (stringBuilder.Length > 0)
			{
				yield return stringBuilder.ToString();
			}
		}

		public async IAsyncEnumerable<string> GetPartsAsync(Stream stream, Encoding encoding,[EnumeratorCancellation] CancellationToken cancellationToken = default)
		{
			string? line;
			var reader = new StreamReader(stream, encoding, true, 1024, true);
			StringBuilder stringBuilder = new StringBuilder();
			while ((line = await reader.ReadLineAsync()) != null)
			{
				if (string.IsNullOrEmpty(line.Trim()))
				{
					// return only if not empty
					string res = stringBuilder.ToString().TrimEnd();
					if (!string.IsNullOrEmpty(res))
					{
						yield return res;
					}
					stringBuilder = new StringBuilder();
				}
				else
				{
					stringBuilder.AppendLine(line);
				}
			}

			if (stringBuilder.Length > 0)
			{
				yield return stringBuilder.ToString();
			}
		}
		public SubtitleModel ParsePart(string part, bool isFirstPart)
		{
			SubtitleModel item;
			// Ensure that our stream does not have the WebVTT header in the first part (this happen when the SRT parser pick a WebVTT file),
			// it has some similarities with SRT and thus can somtimes work*, but time parsing will fail. https://www.w3.org/TR/webvtt1/#file-structure
			if (isFirstPart && part.Equals("WEBVTT", StringComparison.InvariantCultureIgnoreCase))
			{
				throw new FormatException("This stream seems to be in WebVTT format, SRT cannot parse it.");
			}

			// Split new lines
			IEnumerable<string> lines = part.Split(_newLineCharacters, StringSplitOptions.RemoveEmptyEntries).Select(line => line.Trim());

			item = new SubtitleModel();
			foreach (string line in lines)
			{
				// Verify if we already have defined the subtitle time (found the line that tell us the time info) or not
				if (item.StartTime == -1 && item.EndTime == -1)
				{
					// Try to get the timecode
					bool success = TryParseTimecodeLine(line, out int startTc, out int endTc);
					if (success)
					{
						item.StartTime = startTc;
						item.EndTime = endTc;
					}
				}
				else // We already found the subtitle time
				{
					// Strip formatting by removing anything within curly braces or angle brackets, which is how SRT styles text according to wikipedia (https://en.wikipedia.org/wiki/SubRip#Formatting)
					item.Lines.Add(Regex.Replace(line, @"\{.*?\}|<.*?>", string.Empty).Trim());
				}
			}

			// Ensure the subtitle item is valid before pushing it to the final list


			// Reached the end of the processing of a part, so we are no longer in the "first part"
			return item;
		}
		/// <summary>
		/// Method that try to parse a line of the srt file to get the start and end timecode.
		/// </summary>
		/// <param name="line">The line to parse</param>
		/// <param name="startTc">The output start time in milliseconds</param>
		/// <param name="endTc">The output end time in milliseconds</param>
		/// <returns>True if it parsed the timecode from the line, else false</returns>
		private static bool TryParseTimecodeLine(string line, out int startTc, out int endTc)
		{
			string[] parts = line.Split(_delimiters, StringSplitOptions.None);
			if (parts.Length != 2)
			{
				// this is not a timecode line
				startTc = -1;
				endTc = -1;
				return false;
			}
			else
			{
				startTc = ParseSrtTimecode(parts[0]);
				endTc = ParseSrtTimecode(parts[1]);
				return true;
			}
		}

		/// <summary>
		/// Takes an SRT timecode as a string and parses it into a milliseconds. A SRT timecode reads as follows: 
		/// 00:00:20,000
		/// </summary>
		/// <param name="timecode">The timecode to parse</param>
		/// <returns>The parsed timecode in milliseconds. If the parsing was unsuccessful, -1 is returned</returns>
		private static int ParseSrtTimecode(string timecode)
		{
			Match match = Regex.Match(timecode, "[0-9]+:[0-9]+:[0-9]+([,\\.][0-9]+)?");
			if (match.Success)
			{
				timecode = match.Value;
				TimeSpan result;
				if (TimeSpan.TryParse(timecode.Replace(',', '.'), out result))
				{
					int nbOfMs = (int)result.TotalMilliseconds;
					return nbOfMs;
				}
			}
			return -1;
		}

	}
}
