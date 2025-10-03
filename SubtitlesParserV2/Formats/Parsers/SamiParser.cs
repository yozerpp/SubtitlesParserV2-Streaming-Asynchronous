using SubtitlesParserV2.Helpers;
using SubtitlesParserV2.Models;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Runtime.CompilerServices;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using System.Web;

namespace SubtitlesParserV2.Formats.Parsers
{
	/// <summary>
	/// Configuration model for the Sami parser.
	/// </summary>
	public class SamiParserConfig
	{
		/// <summary>
		/// By default, will define itself to the language used by the first subtitle found by the parser.
		/// If you define this value, only subtitle in the language matching what you defined
		/// will be parsed. Example: ENUSCC
		/// </summary>
		public string? TargetLanguage { get; set; }
	}

	/// <summary>
	/// Parser for the .sami/.smi subtitles files (html-like).
	/// <strong>NOTE</strong>: Only support time parsing in ms (Microsoft DirectShow and Windows Media Player support only milliseconds)
	/// </summary>
	/// <!--
	/// Sources:
	/// https://learn.microsoft.com/en-us/previous-versions/windows/desktop/dnacc/understanding-sami-1.0#sami-parameters
	/// https://en.wikipedia.org/wiki/SAMI
	/// https://docs.fileformat.com/video/sami/
	/// -->
	internal class SamiParser : ISubtitlesParser<SamiSubtitlePart>, ISubtitlesParser<SamiSubtitlePart, SamiParserConfig>
	{
		private const string BadFormatMsg = "Stream is not in a valid Sami format";

		public List<SubtitleModel> ParseStream(Stream stream, Encoding encoding)
		{
			return ParseStream(stream, encoding, new SamiParserConfig());
		}

		public List<SubtitleModel> ParseStream(Stream samiStream, Encoding encoding, SamiParserConfig config)
		{
			var ret = ParseAsEnumerable(samiStream, encoding, config).ToList();
			if (ret.Count == 0) throw new ArgumentException(BadFormatMsg);
			return ret;
		}

		public async Task<List<SubtitleModel>> ParseStreamAsync(Stream stream, Encoding encoding, CancellationToken cancellationToken)
		{
			var ret = await ParseAsEnumerableAsync(stream, encoding, new SamiParserConfig(), cancellationToken).ToListAsync(cancellationToken);
			if (ret.Count == 0) throw new ArgumentException(BadFormatMsg);
			return ret;
		}

		public IEnumerable<SubtitleModel> ParseAsEnumerable(Stream samiStream, Encoding encoding)
		{
			return ParseAsEnumerable(samiStream, encoding, new SamiParserConfig());
		}

		public IEnumerable<SubtitleModel> ParseAsEnumerable(Stream samiStream, Encoding encoding, SamiParserConfig config)
		{
			StreamHelper.ThrowIfStreamIsNotSeekableOrReadable(samiStream);
			// seek the beginning of the stream
			samiStream.Position = 0;

			IEnumerable<SamiSubtitlePart> parts = GetParts(samiStream, encoding, config).Peekable(out var partsAny);
			if (!partsAny)
				throw new ArgumentException(BadFormatMsg);

			bool first = true;
			foreach (SamiSubtitlePart part in parts)
			{
				yield return ParsePart(part, first);
				first = false;
			}
		}

		public async IAsyncEnumerable<SubtitleModel> ParseAsEnumerableAsync(Stream samiStream, Encoding encoding, [EnumeratorCancellation] CancellationToken cancellationToken)
		{
			await foreach (var item in ParseAsEnumerableAsync(samiStream, encoding, new SamiParserConfig(), cancellationToken))
			{
				yield return item;
			}
		}

		public async IAsyncEnumerable<SubtitleModel> ParseAsEnumerableAsync(Stream samiStream, Encoding encoding, SamiParserConfig config, [EnumeratorCancellation] CancellationToken cancellationToken)
		{
			StreamHelper.ThrowIfStreamIsNotSeekableOrReadable(samiStream);
			// seek the beginning of the stream
			samiStream.Position = 0;

			var parts = GetPartsAsync(samiStream, encoding, config, cancellationToken);
			var partsAny = await parts.PeekableAsync();
			if (!partsAny)
				throw new ArgumentException(BadFormatMsg);

			bool first = true;
			await foreach (SamiSubtitlePart part in parts.WithCancellation(cancellationToken))
			{
				yield return ParsePart(part, first);
				first = false;
			}
		}

		public IEnumerable<SamiSubtitlePart> GetParts(Stream stream, Encoding encoding)
		{
			return GetParts(stream, encoding, new SamiParserConfig());
		}

		public IEnumerable<SamiSubtitlePart> GetParts(Stream stream, Encoding encoding, SamiParserConfig config)
		{
			using StreamReader reader = new StreamReader(stream, encoding, detectEncodingFromByteOrderMarks: true, 1024, true);
			foreach (var part in GetSamiSubtitleParts(reader, config))
			{
				yield return part;
			}
		}

		public async IAsyncEnumerable<SamiSubtitlePart> GetPartsAsync(Stream stream, Encoding encoding, [EnumeratorCancellation] CancellationToken cancellationToken = default)
		{
			await foreach (var part in GetPartsAsync(stream, encoding, new SamiParserConfig(), cancellationToken))
			{
				yield return part;
			}
		}

		public async IAsyncEnumerable<SamiSubtitlePart> GetPartsAsync(Stream stream, Encoding encoding, SamiParserConfig config, [EnumeratorCancellation] CancellationToken cancellationToken = default)
		{
			using StreamReader reader = new StreamReader(stream, encoding, detectEncodingFromByteOrderMarks: true, 1024, true);
			await foreach (var part in GetSamiSubtitlePartsAsync(reader, config, cancellationToken))
			{
				yield return part;
			}
		}

		public SubtitleModel ParsePart(SamiSubtitlePart part, bool isFirstPart)
		{
			return new SubtitleModel()
			{
				StartTime = part.StartTime,
				EndTime = part.EndTime,
				Lines = part.Lines
			};
		}

		/// <summary>
		/// Enumerates the subtitle parts in a SAMI file.
		/// </summary>
		/// <param name="reader">The textreader associated with the SAMI file</param>
		/// <param name="config">The parser configuration</param>
		/// <returns>An IEnumerable of SamiSubtitlePart objects</returns>
		private static IEnumerable<SamiSubtitlePart> GetSamiSubtitleParts(TextReader reader, SamiParserConfig config)
		{
			List<string> linesPerBlock = new List<string>();

			// Store if we are reading in the body or not
			bool inBody = false;
			// Store the sync block beginning time
			int syncBlockStartTimeMs = -1;

			// Read the first line
			string? line = reader.ReadLine();
			// Ensure the file is a sami file by verifying the first line
			if (!line?.Equals("<SAMI>", StringComparison.OrdinalIgnoreCase) ?? true) 
				throw new ArgumentException("Could not find SAMI element at line 1.");

			// Loop until last line was processed
			do
			{
				// Read a new line
				line = reader.ReadLine();
				// If the line is null or empty, re-evaluate the while condition and read next line
				if (string.IsNullOrEmpty(line)) continue;

				// Verify if we are inside the body (where the subtitle are)
				if (inBody)
				{
					// Verify if we exited the body
					if (line.StartsWith("</BODY>", StringComparison.OrdinalIgnoreCase))
					{
						inBody = false;
						continue; // exit body execution as we are no longer inside the body
					}

					// Verify for a new sync block (Parse the time of the current line)
					if (line.StartsWith("<SYNC", StringComparison.OrdinalIgnoreCase))
					{
						int startIndex = line.IndexOf("Start=", StringComparison.OrdinalIgnoreCase) + 6;
						int endIndex = line.IndexOf('>', startIndex);
						int newSyncBlockStartTimeMs = ParseSamiTime(line.Substring(startIndex, endIndex - startIndex).Trim()) ?? -1;

						// We now have the end time of the previous sync block
						if (linesPerBlock.Count >= 1)
						{
							// Yield the previous sync block with its end time
							yield return new SamiSubtitlePart
							{
								StartTime = syncBlockStartTimeMs,
								EndTime = newSyncBlockStartTimeMs,
								Lines = linesPerBlock
							};
							linesPerBlock = new List<string>(); // Clear lines for new sync block
						}

						// Store the new sync block start time
						syncBlockStartTimeMs = newSyncBlockStartTimeMs;
					}

					// Parse the text from the current line
					(string? text, string? textLanguage) = GetTextFromLine(line);
					// Ensure text was found
					if (!string.IsNullOrEmpty(text) && !string.IsNullOrEmpty(textLanguage))
					{
						// If target language is defined, do nothing, else, set the first language we found
						// as the target language. Thus, all lyrics from now on will need to have the same language.
						config.TargetLanguage = config.TargetLanguage != null ? config.TargetLanguage : textLanguage;
						// Ensure that the subtitle we are parsing is the same language as the targeted language
						if (config.TargetLanguage.Equals(textLanguage, StringComparison.OrdinalIgnoreCase))
						{
							linesPerBlock.Add(HttpUtility.HtmlDecode(text).Trim());
						}
					}

					// Handle SYNC block closure
					if (line.StartsWith("</SYNC>", StringComparison.OrdinalIgnoreCase) || line.EndsWith("</SYNC>", StringComparison.OrdinalIgnoreCase))
					{
						// Continue to next line - we'll yield when we find the next SYNC block or end of body
						continue;
					}
				}
				else if (line.StartsWith("<BODY>", StringComparison.OrdinalIgnoreCase)) // Verify if we entered the body
				{
					inBody = true;
				}
			} while (line != null); // Loop until end of stream

			// Yield any remaining lines from the last sync block
			if (linesPerBlock.Count >= 1)
			{
				yield return new SamiSubtitlePart
				{
					StartTime = syncBlockStartTimeMs,
					EndTime = -1, // Last subtitle doesn't have a known end time
					Lines = linesPerBlock
				};
			}
		}

		/// <summary>
		/// Asynchronously enumerates the subtitle parts in a SAMI file.
		/// </summary>
		/// <param name="reader">The textreader associated with the SAMI file</param>
		/// <param name="config">The parser configuration</param>
		/// <param name="cancellationToken">Cancellation token</param>
		/// <returns>An IAsyncEnumerable of SamiSubtitlePart objects</returns>
		private static async IAsyncEnumerable<SamiSubtitlePart> GetSamiSubtitlePartsAsync(TextReader reader, SamiParserConfig config, [EnumeratorCancellation] CancellationToken cancellationToken = default)
		{
			List<string> linesPerBlock = new List<string>();

			// Store if we are reading in the body or not
			bool inBody = false;
			// Store the sync block beginning time
			int syncBlockStartTimeMs = -1;

			// Read the first line
			string? line = await reader.ReadLineAsync();
			// Ensure the file is a sami file by verifying the first line
			if (!line?.Equals("<SAMI>", StringComparison.OrdinalIgnoreCase) ?? true) 
				throw new ArgumentException("Could not find SAMI element at line 1.");

			// Loop until last line was processed
			do
			{
				cancellationToken.ThrowIfCancellationRequested();

				// Read a new line
				line = await reader.ReadLineAsync();
				// If the line is null or empty, re-evaluate the while condition and read next line
				if (string.IsNullOrEmpty(line)) continue;

				// Verify if we are inside the body (where the subtitle are)
				if (inBody)
				{
					// Verify if we exited the body
					if (line.StartsWith("</BODY>", StringComparison.OrdinalIgnoreCase))
					{
						inBody = false;
						continue; // exit body execution as we are no longer inside the body
					}

					// Verify for a new sync block (Parse the time of the current line)
					if (line.StartsWith("<SYNC", StringComparison.OrdinalIgnoreCase))
					{
						int startIndex = line.IndexOf("Start=", StringComparison.OrdinalIgnoreCase) + 6;
						int endIndex = line.IndexOf('>', startIndex);
						int newSyncBlockStartTimeMs = ParseSamiTime(line.Substring(startIndex, endIndex - startIndex).Trim()) ?? -1;

						// We now have the end time of the previous sync block
						if (linesPerBlock.Count >= 1)
						{
							// Yield the previous sync block with its end time
							yield return new SamiSubtitlePart
							{
								StartTime = syncBlockStartTimeMs,
								EndTime = newSyncBlockStartTimeMs,
								Lines = linesPerBlock
							};
							linesPerBlock = new List<string>(); // Clear lines for new sync block
						}

						// Store the new sync block start time
						syncBlockStartTimeMs = newSyncBlockStartTimeMs;
					}

					// Parse the text from the current line
					(string? text, string? textLanguage) = GetTextFromLine(line);
					// Ensure text was found
					if (!string.IsNullOrEmpty(text) && !string.IsNullOrEmpty(textLanguage))
					{
						// If target language is defined, do nothing, else, set the first language we found
						// as the target language. Thus, all lyrics from now on will need to have the same language.
						config.TargetLanguage = config.TargetLanguage != null ? config.TargetLanguage : textLanguage;
						// Ensure that the subtitle we are parsing is the same language as the targeted language
						if (config.TargetLanguage.Equals(textLanguage, StringComparison.OrdinalIgnoreCase))
						{
							linesPerBlock.Add(HttpUtility.HtmlDecode(text).Trim());
						}
					}

					// Handle SYNC block closure
					if (line.StartsWith("</SYNC>", StringComparison.OrdinalIgnoreCase) || line.EndsWith("</SYNC>", StringComparison.OrdinalIgnoreCase))
					{
						// Continue to next line - we'll yield when we find the next SYNC block or end of body
						continue;
					}
				}
				else if (line.StartsWith("<BODY>", StringComparison.OrdinalIgnoreCase)) // Verify if we entered the body
				{
					inBody = true;
				}
			} while (line != null); // Loop until end of stream

			// Yield any remaining lines from the last sync block
			if (linesPerBlock.Count >= 1)
			{
				yield return new SamiSubtitlePart
				{
					StartTime = syncBlockStartTimeMs,
					EndTime = -1, // Last subtitle doesn't have a known end time
					Lines = linesPerBlock
				};
			}
		}

		/// <summary>
		/// Parse sami text using the ms format only (assume sami uses MS time).
		/// </summary>
		/// <!--
		/// We could detect other time format implemented by third-party programs,
		/// but doc says time metrics should be MS by default:
		///	<SAMIParam>
		///	Metrics {time:ms;}
		///	Spec {MSFT:1.0;}
		///	</SAMIParam >
		/// ]]>
		/// -->
		/// <param name="text">The text to parse</param>
		/// <returns>The time in milliseconds or null if it failed</returns>
		private static int? ParseSamiTime(string text)
		{
			bool success = int.TryParse(text, out int time);
			if (success) return time;
			return null;
		}

		/// <summary>
		/// Parse a Sami stream to get the text of the current line along with it's language
		/// </summary>
		/// <param name="line">The line to parse</param>
		/// <returns>A ValueTuple with the text as Item1 and the targetLanguage as Item2</returns>
		private static (string? text, string? targetLanguage) GetTextFromLine(string line)
		{
			string? text = null;
			string? targetLanguage = null;
			// Directly try to find a P element as some .sami files have the sync block and the P Element on the same line
			int pIndex = line.IndexOf("<P", StringComparison.OrdinalIgnoreCase);
			// Ensure the position of the P element was found on the line
			if (pIndex >= 1)
			{
				// Get the class (language) of the P element (subtitle text) we found
				int pClassIndex = line.IndexOf("Class=", pIndex, StringComparison.OrdinalIgnoreCase) + 6;
				string pClassName = string.Empty;
				if (pClassIndex >= 1)
				{
					/* Here we have two possible way of finding the end of the attribute, a space, meaning a new attribute is
					 * starting, or a tag ">" meaning it's the end of the P element tag. Ex : <p Class=value-here>
					 * We ensure to take the index with the smallest number, to prevent issues where the space could have been
					 * found but at a index position after the ">" tag.
					 */
					int classEndSpace = line.IndexOf(" ", pClassIndex);
					int classEndTag = line.IndexOf(">", pClassIndex);
					int classEndIndex = classEndSpace >= 1 && (classEndSpace < classEndTag) ? classEndSpace : classEndTag;
					// Get the class (language name)
					targetLanguage = line.Substring(pClassIndex, classEndIndex - pClassIndex).Trim();
				}

				// Find the closure of the P element (html first part/creation)
				int textStart = line.IndexOf(">", pIndex) + 1;
				// Find the closure of the P element (html last part/closure)
				int textEnd = line.IndexOf("</P>", textStart);
				text = line.Substring(textStart, textEnd - textStart);
				return (text, targetLanguage);
			}
			return (null, null);
		}
	}

	/// <summary>
	/// Represents a parsed SAMI subtitle part before conversion to SubtitleModel
	/// </summary>
	internal class SamiSubtitlePart
	{
		public int StartTime { get; set; }
		public int EndTime { get; set; }
		public List<string> Lines { get; set; } = new List<string>();
	}
}
