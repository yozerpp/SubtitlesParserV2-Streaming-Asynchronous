using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Runtime.CompilerServices;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;
using SubtitlesParserV2.Helpers;
using SubtitlesParserV2.Logger;
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

		/// <summary>
		/// Define the maximum number of lines the program will continue reading before exiting if it
		/// haven't found any lines in MicroDvd format.
		/// </summary>
		public int FirstLineSearchTimeout { get; set; } = 20;
	}

	/// <summary>
	/// Parser for MicroDVD .sub subtitles files.
	/// Will try to detect the framerate by default.
	/// </summary>
	/// <remarks>
	/// If no framerate are found, will default to 25. To force specific settings, you can use
	/// <see cref="SubtitleFormat.GetFormat(SubtitleFormatType)"/> and define the <see cref="SubtitleFormat.ParserInstance"/>
	/// as a <see cref="ISubtitlesParserWithConfig{MicroDvdParserConfig}"/>.
	/// </remarks>
	/// <!--
	/// Sources:
	/// https://en.wikipedia.org/wiki/MicroDVD
	/// Example:
	/// {1}{1}29.970
	/// {0}{180}PIRATES OF THE WORLD|English by chicken
	/// {509}{629}Drink up water yo ho!
	/// {635}{755}We eat and don't give a hoot.
	/// -->
	internal class MicroDvdParser : ISubtitlesParserWithConfig<MicroDvdSubtitlePart, MicroDvdParserConfig>
	{
		private static readonly Type CurrentType = typeof(MicroDvdParser);
		// Alternative for static class, create a logger with the full namespace name
		private static readonly ILogger _logger = LoggerManager.GetLogger(CurrentType.FullName ?? CurrentType.Name);

		// Properties -----------------------------------------------------------------------

		private const float defaultFrameRate = 25;
		private static readonly char[] _lineSeparators = { '|' };
		private static readonly string LineRegex = @"^[{\[](-?\d+)[}\]][{\[](-?\d+)[}\]](.*)";

		private const string BadFormatMsg = "Stream is not in a valid MicroDVD format";

		// Methods -------------------------------------------------------------------------

		public List<SubtitleModel> ParseStream(Stream subStream, Encoding encoding)
		{
			return ParseStream(subStream, encoding, new MicroDvdParserConfig());
		}

		public List<SubtitleModel> ParseStream(Stream subStream, Encoding encoding, MicroDvdParserConfig config)
		{
			var ret = ParseAsEnumerable(subStream, encoding, config).ToList();
			if (ret.Count == 0) throw new FormatException(BadFormatMsg);
			return ret;
		}

		public async Task<List<SubtitleModel>> ParseStreamAsync(Stream stream, Encoding encoding, CancellationToken cancellationToken)
		{
			var ret = await ParseAsEnumerableAsync(stream, encoding, new MicroDvdParserConfig(), cancellationToken).ToListAsync(cancellationToken);
			if (ret.Count == 0) throw new FormatException(BadFormatMsg);
			return ret;
		}

		public IEnumerable<SubtitleModel> ParseStreamConsuming(Stream subStream, Encoding encoding)
		{
			return ParseAsEnumerable(subStream, encoding, new MicroDvdParserConfig());
		}

		public IEnumerable<SubtitleModel> ParseAsEnumerable(Stream subStream, Encoding encoding, MicroDvdParserConfig config)
		{
			StreamHelper.ThrowIfStreamIsNotSeekableOrReadable(subStream);
			// seek the beginning of the stream
			subStream.Position = 0;

			IEnumerable<MicroDvdSubtitlePart> parts = GetParts(subStream, encoding, config).Peekable(out var partsAny);
			if (!partsAny)
				throw new FormatException(BadFormatMsg);

			bool first = true;
			foreach (MicroDvdSubtitlePart part in parts)
			{
				yield return ParsePart(part, first, config);
				first = false;
			}
		}

		public async IAsyncEnumerable<SubtitleModel> ParseStreamConsumingAsync(Stream subStream, Encoding encoding, [EnumeratorCancellation] CancellationToken cancellationToken)
		{
			await foreach (var item in ParseAsEnumerableAsync(subStream, encoding, new MicroDvdParserConfig(), cancellationToken))
			{
				yield return item;
			}
		}

		public async IAsyncEnumerable<SubtitleModel> ParseAsEnumerableAsync(Stream subStream, Encoding encoding, MicroDvdParserConfig config, [EnumeratorCancellation] CancellationToken cancellationToken)
		{
			StreamHelper.ThrowIfStreamIsNotSeekableOrReadable(subStream);
			// seek the beginning of the stream
			subStream.Position = 0;

			var partsOld = GetPartsAsync(subStream, encoding, cancellationToken);
			var (parts,partsAny) = await partsOld.PeekableAsync();
			if (!partsAny)
				throw new FormatException(BadFormatMsg);

			bool first = true;
			await foreach (MicroDvdSubtitlePart part in parts.WithCancellation(cancellationToken))
			{
				yield return ParsePart(part, first, config);
				first = false;
			}
		}

		public IEnumerable<MicroDvdSubtitlePart> GetParts(Stream stream, Encoding encoding)
		{
			return GetParts(stream, encoding, new MicroDvdParserConfig());
		}

		public IEnumerable<MicroDvdSubtitlePart> GetParts(Stream stream, Encoding encoding, MicroDvdParserConfig config)
		{
			using StreamReader reader = new StreamReader(stream, encoding, detectEncodingFromByteOrderMarks: true, 1024, true);
			foreach (var part in GetMicroDvdSubtitleParts(reader, config))
			{
				yield return part;
			}
		}

		public async IAsyncEnumerable<MicroDvdSubtitlePart> GetPartsAsync(Stream stream, Encoding encoding, [EnumeratorCancellation] CancellationToken cancellationToken = default)
		{
			await foreach (var part in GetPartsAsync(stream, encoding, new MicroDvdParserConfig(), cancellationToken))
			{
				yield return part;
			}
		}

		public async IAsyncEnumerable<MicroDvdSubtitlePart> GetPartsAsync(Stream stream, Encoding encoding, MicroDvdParserConfig config, [EnumeratorCancellation] CancellationToken cancellationToken = default)
		{
			using StreamReader reader = new StreamReader(stream, encoding, detectEncodingFromByteOrderMarks: true, 1024, true);
			await foreach (var part in GetMicroDvdSubtitlePartsAsync(reader, config, cancellationToken))
			{
				yield return part;
			}
		}

		public SubtitleModel ParsePart(MicroDvdSubtitlePart part, bool isFirstPart)
		{
			return ParsePart(part, isFirstPart, new MicroDvdParserConfig());
		}

		public SubtitleModel ParsePart(MicroDvdSubtitlePart part, bool isFirstPart, MicroDvdParserConfig config)
		{
			int start = (int)(1000 * part.StartFrame / part.FrameRate);
			int end = (int)(1000 * part.EndFrame / part.FrameRate);

			return new SubtitleModel()
			{
				StartTime = start,
				EndTime = end,
				Lines = part.Lines
			};
		}

		/// <summary>
		/// Enumerates the subtitle parts in a MicroDVD file.
		/// </summary>
		/// <param name="reader">The textreader associated with the MicroDVD file</param>
		/// <param name="config">The parser configuration</param>
		/// <returns>An IEnumerable of MicroDvdSubtitlePart objects</returns>
		private static IEnumerable<MicroDvdSubtitlePart> GetMicroDvdSubtitleParts(TextReader reader, MicroDvdParserConfig config)
		{
			string? line = reader.ReadLine();
			int searchTimeout = config.FirstLineSearchTimeout;

			// find the first relevant line
			while (line != null && !IsMicroDvdLine(line) && searchTimeout > 0)
			{
				line = reader.ReadLine();
				searchTimeout--;
			}

			if (line == null)
			{
				yield break;
			}

			float frameRate;
			if (config.Framerate.HasValue) // If a framerate was given when calling the method, use it
			{
				frameRate = config.Framerate.Value;
			}
			else
			{
				// try to extract the framerate from the first line
				(int startFrame, int endFrame, List<string> lines) = ParseMicroDvdLine(line);
				if (lines != null && lines.Count >= 1)
				{
					bool success = TryExtractFrameRate(lines[0], out frameRate);
					if (!success)
					{
						_logger.LogWarning("Couldn't extract frame rate of sub file with first line {line}. We use the default frame rate: {frameRate}", line, defaultFrameRate);
						frameRate = defaultFrameRate;
						// We didn't find the framerate on the line, the line might be a subtitle so we yield it
						yield return new MicroDvdSubtitlePart
						{
							StartFrame = startFrame,
							EndFrame = endFrame,
							Lines = lines,
							FrameRate = frameRate
						};
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
				if (!string.IsNullOrEmpty(line) && IsMicroDvdLine(line))
				{
					(int startFrame, int endFrame, List<string> lines) = ParseMicroDvdLine(line);
					yield return new MicroDvdSubtitlePart
					{
						StartFrame = startFrame,
						EndFrame = endFrame,
						Lines = lines,
						FrameRate = frameRate
					};
				}
				line = reader.ReadLine();
			}
		}

		/// <summary>
		/// Asynchronously enumerates the subtitle parts in a MicroDVD file.
		/// </summary>
		/// <param name="reader">The textreader associated with the MicroDVD file</param>
		/// <param name="config">The parser configuration</param>
		/// <param name="cancellationToken">Cancellation token</param>
		/// <returns>An IAsyncEnumerable of MicroDvdSubtitlePart objects</returns>
		private static async IAsyncEnumerable<MicroDvdSubtitlePart> GetMicroDvdSubtitlePartsAsync(TextReader reader, MicroDvdParserConfig config, [EnumeratorCancellation] CancellationToken cancellationToken = default)
		{
			string? line = await reader.ReadLineAsync();
			int searchTimeout = config.FirstLineSearchTimeout;

			// find the first relevant line
			while (line != null && !IsMicroDvdLine(line) && searchTimeout > 0)
			{
				cancellationToken.ThrowIfCancellationRequested();
				line = await reader.ReadLineAsync();
				searchTimeout--;
			}

			if (line == null)
			{
				yield break;
			}

			float frameRate;
			if (config.Framerate.HasValue) // If a framerate was given when calling the method, use it
			{
				frameRate = config.Framerate.Value;
			}
			else
			{
				// try to extract the framerate from the first line
				(int startFrame, int endFrame, List<string> lines) = ParseMicroDvdLine(line);
				if (lines != null && lines.Count >= 1)
				{
					bool success = TryExtractFrameRate(lines[0], out frameRate);
					if (!success)
					{
						_logger.LogWarning("Couldn't extract frame rate of sub file with first line {line}. We use the default frame rate: {frameRate}", line, defaultFrameRate);
						frameRate = defaultFrameRate;
						// We didn't find the framerate on the line, the line might be a subtitle so we yield it
						yield return new MicroDvdSubtitlePart
						{
							StartFrame = startFrame,
							EndFrame = endFrame,
							Lines = lines,
							FrameRate = frameRate
						};
					}
				}
				else
				{
					frameRate = defaultFrameRate;
				}
			}

			// Parse other lines
			line = await reader.ReadLineAsync();
			while (line != null)
			{
				cancellationToken.ThrowIfCancellationRequested();

				if (!string.IsNullOrEmpty(line) && IsMicroDvdLine(line))
				{
					(int startFrame, int endFrame, List<string> lines) = ParseMicroDvdLine(line);
					yield return new MicroDvdSubtitlePart
					{
						StartFrame = startFrame,
						EndFrame = endFrame,
						Lines = lines,
						FrameRate = frameRate
					};
				}
				line = await reader.ReadLineAsync();
			}
		}

		private static bool IsMicroDvdLine(string line)
		{
			return Regex.IsMatch(line, LineRegex);
		}

		/// <summary>
		/// Parses one line of the .sub file
		/// </summary>
		/// <!--
		/// Example:
		/// {0}{180}PIRATES OF THE WORLD|English subtitlez by chicken
		/// -->
		/// <param name="line">The .sub file line</param>
		/// <returns>A tuple containing start frame, end frame, and text lines</returns>
		private static (int startFrame, int endFrame, List<string> lines) ParseMicroDvdLine(string line)
		{
			Match match = Regex.Match(line, LineRegex);
			if (match.Success && match.Groups.Count > 2)
			{
				string startFrameStr = match.Groups[1].Value;
				int startFrame = int.Parse(startFrameStr);
				string endFrameStr = match.Groups[2].Value;
				int endFrame = int.Parse(endFrameStr);
				string text = match.Groups[match.Groups.Count - 1].Value;
				string[] lineArray = text.Split(_lineSeparators);
				List<string> nonEmptyLines = lineArray.Where(l => !string.IsNullOrEmpty(l)).ToList();

				return (startFrame, endFrame, nonEmptyLines);
			}
			else
			{
				throw new InvalidDataException($"The subtitle file line {line} is not in the micro dvd format. We stop the process.");
			}
		}

		/// <summary>
		/// Tries to extract the frame rate from a subtitle file line.
		/// </summary>
		/// <!--
		/// Supported formats are:
		/// {x}{y}25
		/// {x}{y}{...}23.976
		/// -->
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

	/// <summary>
	/// Represents a parsed MicroDVD subtitle part before conversion to SubtitleModel
	/// </summary>
	internal class MicroDvdSubtitlePart
	{
		public int StartFrame { get; set; }
		public int EndFrame { get; set; }
		public List<string> Lines { get; set; } = new List<string>();
		public float FrameRate { get; set; }
	}
}
