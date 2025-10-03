using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Net;
using System.Runtime.CompilerServices;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using System.Xml;
using SubtitlesParserV2.Helpers;
using SubtitlesParserV2.Models;

namespace SubtitlesParserV2.Formats.Parsers
{
	/// <summary>
	/// Parser for .ytt files (Youtube Timed Text).
	/// </summary>
	/// <!--
	/// Sources (Unofficial) : https://github.com/FyraLabs/yttml/blob/main/crates/srv3-ttml/internals/srv3-format.md
	/// -->
	internal class YttXmlParser : ISubtitlesParser<YttXmlSubtitlePart>
	{
		private const string BadFormatMsg = "Stream is not in a valid Youtube XML format";

		public List<SubtitleModel> ParseStream(Stream xmlStream, Encoding encoding)
		{
			var ret = ParseAsEnumerable(xmlStream, encoding).ToList();
			if (ret.Count == 0) throw new ArgumentException(BadFormatMsg);
			return ret;
		}

		public async Task<List<SubtitleModel>> ParseStreamAsync(Stream stream, Encoding encoding, CancellationToken cancellationToken)
		{
			var ret = await ParseAsEnumerableAsync(stream, encoding, cancellationToken).ToListAsync(cancellationToken);
			if (ret.Count == 0) throw new ArgumentException(BadFormatMsg);
			return ret;
		}

		public IEnumerable<SubtitleModel> ParseAsEnumerable(Stream xmlStream, Encoding encoding)
		{
			StreamHelper.ThrowIfStreamIsNotSeekableOrReadable(xmlStream);
			// seek the beginning of the stream
			xmlStream.Position = 0;

			IEnumerable<YttXmlSubtitlePart> parts = GetParts(xmlStream, encoding).Peekable(out var partsAny);
			if (!partsAny)
				throw new ArgumentException(BadFormatMsg);

			bool first = true;
			foreach (YttXmlSubtitlePart part in parts)
			{
				yield return ParsePart(part, first);
				first = false;
			}
		}

		public async IAsyncEnumerable<SubtitleModel> ParseAsEnumerableAsync(Stream xmlStream, Encoding encoding, [EnumeratorCancellation] CancellationToken cancellationToken)
		{
			StreamHelper.ThrowIfStreamIsNotSeekableOrReadable(xmlStream);
			// seek the beginning of the stream
			xmlStream.Position = 0;

			var parts = GetPartsAsync(xmlStream, encoding, cancellationToken);
			var partsAny = await parts.PeekableAsync();
			if (!partsAny)
				throw new ArgumentException(BadFormatMsg);

			bool first = true;
			await foreach (YttXmlSubtitlePart part in parts.WithCancellation(cancellationToken))
			{
				yield return ParsePart(part, first);
				first = false;
			}
		}

		public IEnumerable<YttXmlSubtitlePart> GetParts(Stream stream, Encoding encoding)
		{
			using XmlReader xmlReader = XmlReader.Create(stream, new XmlReaderSettings() { IgnoreWhitespace = true, IgnoreComments = true });
			foreach (var part in GetPartsFromXmlReader(xmlReader))
			{
				yield return part;
			}
		}

		public async IAsyncEnumerable<YttXmlSubtitlePart> GetPartsAsync(Stream stream, Encoding encoding, [EnumeratorCancellation] CancellationToken cancellationToken = default)
		{
			using XmlReader xmlReader = XmlReader.Create(stream, new XmlReaderSettings() { IgnoreWhitespace = true, IgnoreComments = true, Async = true });
			await foreach (var part in GetPartsFromXmlReaderAsync(xmlReader, cancellationToken))
			{
				yield return part;
			}
		}

		private IEnumerable<YttXmlSubtitlePart> GetPartsFromXmlReader(XmlReader reader)
		{
			while (reader.Read())
			{
				// Search for subtitle elements (p for SRV3 and text for SRV1/SRV2)
				if (reader.NodeType == XmlNodeType.Element && (reader.Name == "p" || reader.Name == "text"))
				{
					string startString = reader.GetAttribute("t") ?? string.Empty;
					string durString = reader.GetAttribute("d") ?? string.Empty;
					string startStringSrv1 = reader.GetAttribute("start") ?? string.Empty;
					string durStringSrv1 = reader.GetAttribute("dur") ?? string.Empty;

					List<string> textLines = ParserHelper.XmlReadCurrentElementInnerText(reader);

					yield return new YttXmlSubtitlePart
					{
						StartAttribute = startString,
						DurationAttribute = durString,
						StartAttributeSrv1 = startStringSrv1,
						DurationAttributeSrv1 = durStringSrv1,
						TextLines = textLines
					};
				}
			}
		}

		private async IAsyncEnumerable<YttXmlSubtitlePart> GetPartsFromXmlReaderAsync(XmlReader reader, [EnumeratorCancellation] CancellationToken cancellationToken = default)
		{
			while (await reader.ReadAsync())
			{
				cancellationToken.ThrowIfCancellationRequested();

				// Search for subtitle elements (p for SRV3 and text for SRV1/SRV2)
				if (reader.NodeType == XmlNodeType.Element && (reader.Name == "p" || reader.Name == "text"))
				{
					string startString = reader.GetAttribute("t") ?? string.Empty;
					string durString = reader.GetAttribute("d") ?? string.Empty;
					string startStringSrv1 = reader.GetAttribute("start") ?? string.Empty;
					string durStringSrv1 = reader.GetAttribute("dur") ?? string.Empty;

					List<string> textLines = ParserHelper.XmlReadCurrentElementInnerText(reader);

					yield return new YttXmlSubtitlePart
					{
						StartAttribute = startString,
						DurationAttribute = durString,
						StartAttributeSrv1 = startStringSrv1,
						DurationAttributeSrv1 = durStringSrv1,
						TextLines = textLines
					};
				}
			}
		}

		public SubtitleModel ParsePart(YttXmlSubtitlePart part, bool isFirstPart)
		{
			float start;
			float duration = 0; // Default duration if parsing fails.

			// Try to get the start & end time for SRV3 & SRV2 format (already in MS)
			string startString = part.StartAttribute;
			string durString = part.DurationAttribute;

			// Fallback to SRV1 format if parsing fail (In seconds)
			if (!float.TryParse(startString, NumberStyles.Float, CultureInfo.InvariantCulture, out start) && !float.TryParse(durString, NumberStyles.Float, CultureInfo.InvariantCulture, out duration))
			{
				startString = part.StartAttributeSrv1;
				durString = part.DurationAttributeSrv1;
				if (float.TryParse(startString, NumberStyles.Float, CultureInfo.InvariantCulture, out start))
				{
					start = start * 1000; // Convert S to MS
				}
				else start = -1; // Could not find start time, default "invalid" value is -1

				if (float.TryParse(durString, NumberStyles.Float, CultureInfo.InvariantCulture, out duration))
				{
					duration = duration * 1000; // Convert duration S to MS.
				}
			}

			List<string> textLines = part.TextLines;
			if (textLines.Count >= 1)
			{
				// Get the text and html decode it as some versions (SRV1 & SRV2) uses html encoding
				// for certains characters ( ' > &#39;t).
				// NOTE: We does this on all lines
				textLines = textLines.Select(line => WebUtility.HtmlDecode(line)).ToList();

				return new SubtitleModel()
				{
					StartTime = (int)start,
					EndTime = (int)(start + duration), // Calculate the "end" time with the duration of the subtitle.
					Lines = textLines
				};
			}

			// Return an empty subtitle if no text lines
			return new SubtitleModel()
			{
				StartTime = (int)start,
				EndTime = (int)(start + duration),
				Lines = new List<string>()
			};
		}
	}

	/// <summary>
	/// Represents a parsed YTT XML subtitle part before conversion to SubtitleModel
	/// </summary>
	internal class YttXmlSubtitlePart
	{
		public string StartAttribute { get; set; } = string.Empty;
		public string DurationAttribute { get; set; } = string.Empty;
		public string StartAttributeSrv1 { get; set; } = string.Empty;
		public string DurationAttributeSrv1 { get; set; } = string.Empty;
		public List<string> TextLines { get; set; } = new List<string>();
	}
}
