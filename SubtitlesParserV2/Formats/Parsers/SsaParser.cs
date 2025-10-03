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
using SubtitlesParserV2.Helpers.Formats;
using SubtitlesParserV2.Models;

namespace SubtitlesParserV2.Formats.Parsers
{
	/// <summary>
	/// A parser for the SubStation Alpha subtitles format, .ass /.ssa
	/// <strong>Support</strong> : v4.00, v4 is backward compatible.
	/// </summary>
	/// <!--
	/// Sources:
	/// http://en.wikipedia.org/wiki/SubStation_Alpha
	/// http://www.tcax.org/docs/ass-specs.htm
	/// https://wiki.videolan.org/SubStation_Alpha/
	/// Example:
	/// 
	/// [Script Info]
	/// ; This is a Sub Station Alpha v4 script.
	/// ; For Sub Station Alpha info and downloads,
	/// ; go to http://www.eswat.demon.co.uk/ (https://web.archive.org/web/20000618130810/http://www.eswat.demon.co.uk/downloads/format.zip)
	/// Title: Neon Genesis Evangelion - Episode 26 (neutral Spanish)
	/// Original Script: RoRo
	/// Script Updated By: version 2.8.01
	/// ScriptType: v4.00
	/// Collisions: Normal
	/// PlayResY: 600
	/// PlayDepth: 0
	/// Timer: 100,0000
	///  
	/// [V4 Styles]
	/// Format: Name, Fontname, Fontsize, PrimaryColour, SecondaryColour, TertiaryColour, BackColour, Bold, Italic, BorderStyle, Outline, Shadow, Alignment, MarginL, MarginR, MarginV, AlphaLevel, Encoding
	/// Style: DefaultVCD, Arial,28,11861244,11861244,11861244,-2147483640,-1,0,1,1,2,2,30,30,30,0,0
	///   
	/// [Events]
	/// Format: Marked, Start, End, Style, Name, MarginL, MarginR, MarginV, Effect, Text
	/// Dialogue: Marked=0,0:00:01.18,0:00:06.85,DefaultVCD, NTP,0000,0000,0000,,{\pos(400,570)}Like an angel with pity on nobody
	/// -->
	internal class SsaParser : ISubtitlesParser<SsaSubtitlePart>
	{
		private const string BadFormatMsg = "Stream is not in a valid Ssa format";

		// Methods ------------------------------------------------------------------

		public List<SubtitleModel> ParseStream(Stream ssaStream, Encoding encoding)
		{
			var ret = ParseAsEnumerable(ssaStream, encoding).ToList();
			if (ret.Count == 0) throw new ArgumentException(BadFormatMsg);
			return ret;
		}

		public async Task<List<SubtitleModel>> ParseStreamAsync(Stream stream, Encoding encoding, CancellationToken cancellationToken)
		{
			var ret = await ParseAsEnumerableAsync(stream, encoding, cancellationToken).ToListAsync(cancellationToken);
			if (ret.Count == 0) throw new ArgumentException(BadFormatMsg);
			return ret;
		}

		public IEnumerable<SubtitleModel> ParseAsEnumerable(Stream ssaStream, Encoding encoding)
		{
			StreamHelper.ThrowIfStreamIsNotSeekableOrReadable(ssaStream);
			// seek the beginning of the stream
			ssaStream.Position = 0;

			IEnumerable<SsaSubtitlePart> parts = GetParts(ssaStream, encoding).Peekable(out var partsAny);
			if (!partsAny)
				throw new ArgumentException(BadFormatMsg);

			bool first = true;
			foreach (SsaSubtitlePart part in parts)
			{
				yield return ParsePart(part, first);
				first = false;
			}
		}

		public async IAsyncEnumerable<SubtitleModel> ParseAsEnumerableAsync(Stream ssaStream, Encoding encoding, [EnumeratorCancellation] CancellationToken cancellationToken)
		{
			StreamHelper.ThrowIfStreamIsNotSeekableOrReadable(ssaStream);
			// seek the beginning of the stream
			ssaStream.Position = 0;

			var parts = GetPartsAsync(ssaStream, encoding, cancellationToken);
			var partsAny = await parts.PeekableAsync();
			if (!partsAny)
				throw new ArgumentException(BadFormatMsg);

			bool first = true;
			await foreach (SsaSubtitlePart part in parts.WithCancellation(cancellationToken))
			{
				yield return ParsePart(part, first);
				first = false;
			}
		}

		public IEnumerable<SsaSubtitlePart> GetParts(Stream stream, Encoding encoding)
		{
			using StreamReader reader = new StreamReader(stream, encoding, detectEncodingFromByteOrderMarks: true, 1024, true);
			foreach (var part in GetSsaSubtitleParts(reader))
			{
				yield return part;
			}
		}

		public async IAsyncEnumerable<SsaSubtitlePart> GetPartsAsync(Stream stream, Encoding encoding, [EnumeratorCancellation] CancellationToken cancellationToken = default)
		{
			using StreamReader reader = new StreamReader(stream, encoding, detectEncodingFromByteOrderMarks: true, 1024, true);
			await foreach (var part in GetSsaSubtitlePartsAsync(reader, cancellationToken))
			{
				yield return part;
			}
		}

		public SubtitleModel ParsePart(SsaSubtitlePart part, bool isFirstPart)
		{
			int start = ParserHelper.ParseTimeSpanLineAsMilliseconds(part.StartText);
			int end = ParserHelper.ParseTimeSpanLineAsMilliseconds(part.EndText);

			if (start > 0 && end > 0 && !string.IsNullOrEmpty(part.TextLine))
			{
				List<string> lines;
				switch (part.WrapStyle)
				{
					case SsaWrapStyleHelper.Smart:
					case SsaWrapStyleHelper.SmartWideLowerLine:
					case SsaWrapStyleHelper.EndOfLine:
						// according to the spec doc: 
						// `\n` is ignored by SSA if smart-wrapping (and therefore smart with wider lower line) is enabled
						// end-of-line word wrapping: only `\N` breaks
						lines = part.TextLine.Split(@"\N").ToList();
						break;
					case SsaWrapStyleHelper.None:
						// the default value of the variable is None, which breaks on either `\n` or `\N`

						// according to the spec doc: 
						// no word wrapping: `\n` `\N` both breaks
						lines = Regex.Split(part.TextLine, @"(?:\\n)|(?:\\N)").ToList();
						break;
					default:
						throw new ArgumentOutOfRangeException();
				}

				// trim any spaces from the start of a line (happens when a subtitler includes a space after a newline char ie `this is\N two lines` instead of `this is\Ntwo lines`)
				// this doesn't actually matter for the SSA/ASS format, however if you were to want to convert from SSA/ASS to a format like SRT, it could lead to spaces preceding the second line, which looks funny 
				lines = lines.Select(line => line.TrimStart()).ToList();

				return new SubtitleModel()
				{
					StartTime = start,
					EndTime = end,
					// strip formatting by removing anything within curly braces, this will not remove duplicate content however,
					// which can happen when working with signs for example
					Lines = lines.Select(subtitleLine => Regex.Replace(subtitleLine, @"\{.*?\}", string.Empty)).ToList()
				};
			}

			// Return an empty subtitle if parsing failed
			return new SubtitleModel()
			{
				StartTime = start,
				EndTime = end,
				Lines = new List<string>()
			};
		}

		/// <summary>
		/// Enumerates the subtitle parts in an SSA file.
		/// </summary>
		/// <param name="reader">The textreader associated with the SSA file</param>
		/// <returns>An IEnumerable of SsaSubtitlePart objects</returns>
		private static IEnumerable<SsaSubtitlePart> GetSsaSubtitleParts(TextReader reader)
		{
			// default wrap style to none if the header section doesn't contain a wrap style definition
			// (very possible since it wasn't present in SSA, only ASS) 
			SsaWrapStyleHelper wrapStyle = SsaWrapStyleHelper.None;

			string? line = reader.ReadLine();
			int lineNumber = 1;
			// read the line until the [Events] section
			while (line != null && line != SsaFormatConstantsHelper.EVENT_LINE)
			{
				if (line.StartsWith(SsaFormatConstantsHelper.WRAP_STYLE_PREFIX))
				{
					// get the wrap style
					// the raw string is the second array item after splitting the line at `:` (which we know will be present since it's
					// included in the `WRAP_STYLE_PREFIX` const), so trim the space off the beginning of that item, and parse that string into the enum 
					wrapStyle = line.Split(':')[1].TrimStart().FromString();
				}

				line = reader.ReadLine();
				lineNumber++;
			}

			if (line != null)
			{
				// We are at the event section
				string? headerLine = reader.ReadLine();
				if (!string.IsNullOrEmpty(headerLine))
				{
					List<string> columnHeaders = headerLine.Split(SsaFormatConstantsHelper.SEPARATOR).Select(head => head.Trim()).ToList();
					int startIndexColumn = columnHeaders.IndexOf(SsaFormatConstantsHelper.START_COLUMN);
					int endIndexColumn = columnHeaders.IndexOf(SsaFormatConstantsHelper.END_COLUMN);
					int textIndexColumn = columnHeaders.IndexOf(SsaFormatConstantsHelper.TEXT_COLUMN);

					if (startIndexColumn > 0 && endIndexColumn > 0 && textIndexColumn > 0)
					{
						line = reader.ReadLine();
						while (!string.IsNullOrEmpty(line))
						{
							string[] columns = line.Split(SsaFormatConstantsHelper.SEPARATOR);
							string startText = columns[startIndexColumn];
							string endText = columns[endIndexColumn];
							string textLine = string.Join(",", columns.Skip(textIndexColumn));

							yield return new SsaSubtitlePart
							{
								StartText = startText,
								EndText = endText,
								TextLine = textLine,
								WrapStyle = wrapStyle
							};

							line = reader.ReadLine();
						}
					}
					else
					{
						throw new ArgumentException($"Couldn't find all the necessary columns headers ({SsaFormatConstantsHelper.START_COLUMN}, {SsaFormatConstantsHelper.END_COLUMN}, {SsaFormatConstantsHelper.TEXT_COLUMN}) in header line {headerLine}");
					}
				}
				else
				{
					throw new ArgumentException($"The header line after the line '{line}' was null -> no need to continue parsing.");
				}
			}
			else
			{
				throw new ArgumentException($"Reached line ${line} on a total of #{lineNumber} lines, without finding Event section ({SsaFormatConstantsHelper.EVENT_LINE}). Aborted parsing.");
			}
		}

		/// <summary>
		/// Asynchronously enumerates the subtitle parts in an SSA file.
		/// </summary>
		/// <param name="reader">The textreader associated with the SSA file</param>
		/// <param name="cancellationToken">Cancellation token</param>
		/// <returns>An IAsyncEnumerable of SsaSubtitlePart objects</returns>
		private static async IAsyncEnumerable<SsaSubtitlePart> GetSsaSubtitlePartsAsync(TextReader reader, [EnumeratorCancellation] CancellationToken cancellationToken = default)
		{
			// default wrap style to none if the header section doesn't contain a wrap style definition
			// (very possible since it wasn't present in SSA, only ASS) 
			SsaWrapStyleHelper wrapStyle = SsaWrapStyleHelper.None;

			string? line = await reader.ReadLineAsync();
			int lineNumber = 1;
			// read the line until the [Events] section
			while (line != null && line != SsaFormatConstantsHelper.EVENT_LINE)
			{
				cancellationToken.ThrowIfCancellationRequested();

				if (line.StartsWith(SsaFormatConstantsHelper.WRAP_STYLE_PREFIX))
				{
					// get the wrap style
					// the raw string is the second array item after splitting the line at `:` (which we know will be present since it's
					// included in the `WRAP_STYLE_PREFIX` const), so trim the space off the beginning of that item, and parse that string into the enum 
					wrapStyle = line.Split(':')[1].TrimStart().FromString();
				}

				line = await reader.ReadLineAsync();
				lineNumber++;
			}

			if (line != null)
			{
				// We are at the event section
				string? headerLine = await reader.ReadLineAsync();
				if (!string.IsNullOrEmpty(headerLine))
				{
					List<string> columnHeaders = headerLine.Split(SsaFormatConstantsHelper.SEPARATOR).Select(head => head.Trim()).ToList();
					int startIndexColumn = columnHeaders.IndexOf(SsaFormatConstantsHelper.START_COLUMN);
					int endIndexColumn = columnHeaders.IndexOf(SsaFormatConstantsHelper.END_COLUMN);
					int textIndexColumn = columnHeaders.IndexOf(SsaFormatConstantsHelper.TEXT_COLUMN);

					if (startIndexColumn > 0 && endIndexColumn > 0 && textIndexColumn > 0)
					{
						line = await reader.ReadLineAsync();
						while (!string.IsNullOrEmpty(line))
						{
							cancellationToken.ThrowIfCancellationRequested();

							string[] columns = line.Split(SsaFormatConstantsHelper.SEPARATOR);
							string startText = columns[startIndexColumn];
							string endText = columns[endIndexColumn];
							string textLine = string.Join(",", columns.Skip(textIndexColumn));

							yield return new SsaSubtitlePart
							{
								StartText = startText,
								EndText = endText,
								TextLine = textLine,
								WrapStyle = wrapStyle
							};

							line = await reader.ReadLineAsync();
						}
					}
					else
					{
						throw new ArgumentException($"Couldn't find all the necessary columns headers ({SsaFormatConstantsHelper.START_COLUMN}, {SsaFormatConstantsHelper.END_COLUMN}, {SsaFormatConstantsHelper.TEXT_COLUMN}) in header line {headerLine}");
					}
				}
				else
				{
					throw new ArgumentException($"The header line after the line '{line}' was null -> no need to continue parsing.");
				}
			}
			else
			{
				throw new ArgumentException($"Reached line ${line} on a total of #{lineNumber} lines, without finding Event section ({SsaFormatConstantsHelper.EVENT_LINE}). Aborted parsing.");
			}
		}
	}

	/// <summary>
	/// Represents a parsed SSA subtitle part before conversion to SubtitleModel
	/// </summary>
	internal class SsaSubtitlePart
	{
		public string StartText { get; set; } = string.Empty;
		public string EndText { get; set; } = string.Empty;
		public string TextLine { get; set; } = string.Empty;
		public SsaWrapStyleHelper WrapStyle { get; set; }
	}
}
