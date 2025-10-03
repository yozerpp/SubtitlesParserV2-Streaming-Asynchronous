using System.Collections.Generic;
using System.IO;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using SubtitlesParserV2.Models;

namespace SubtitlesParserV2.Formats.Parsers
{
	/// <summary>
	/// 
	/// </summary>
	public interface ISubtitlesParser
	{
		/// <summary>
		/// Parses a subtitles file stream in a list of SubtitleItem using the default configuration.
		/// </summary>
		/// <remarks>
		/// If the parser require additional configuration, this method will uses the
		/// default configuration. To define your own, consider using <see cref="ISubtitlesParserWithConfig{TConfig}"/>.
		/// <para>
		/// <strong>When using this method, make sure you call it from inside a try-catch as exceptions will be thrown on failure to parse.</strong>
		/// As a alternative, you can use the <see cref="SubtitleParser.ParseStream(Stream, Encoding, IEnumerable{SubtitleFormatType}?, bool)"/> method.
		/// </para>
		/// </remarks>
		/// <param name="stream">The subtitles file stream to parse</param>
		/// <param name="encoding">The stream encoding (if known)</param>
		/// <returns>The corresponding list of SubtitleItems</returns>
		List<SubtitleModel> ParseStream(Stream stream, Encoding encoding);
		Task<List<SubtitleModel>> ParseStreamAsync(Stream stream, Encoding encoding, CancellationToken cancellationToken);
		IEnumerable<SubtitleModel> ParseStreamConsuming(Stream srtStream, Encoding encoding);
		IAsyncEnumerable<SubtitleModel> ParseStreamConsumingAsync(Stream stream, Encoding encoding, CancellationToken cancellationToken);
	}
	/// <summary>
	/// Base interface specifying the required method for a Parser.
	/// Use <see cref="ISubtitlesParserWithConfig{TConfig}"/> to overwrite the default configuration of a specific parser,
	/// if available.
	/// </summary>
	public interface ISubtitlesParser<TPart> : ISubtitlesParser
	{
		IEnumerable<TPart> GetParts(Stream reader, Encoding encoding);
		IAsyncEnumerable<TPart> GetPartsAsync(Stream reader, Encoding encoding, CancellationToken cancellationToken = default);
		SubtitleModel ParsePart(TPart part, bool isFirstPart);
	}

	/// <summary>
	/// Interface for parsers with a option for additional configuration.
	/// <para>
	/// Example:
	/// <code>
	/// <![CDATA[
	/// // Get the format
	/// SubtitleFormat format = SubtitleFormat.GetFormat(SubtitleFormatType.MicroDvd);
	/// // Get the instance as a advanced parser
	/// ISubtitlesParserWithConfig<MicroDvdParserConfig> microDvdParserInstance = format.ParserInstance as ISubtitlesParserWithConfig<MicroDvdParserConfig>;
	/// ]]>
	/// </code>
	/// Now ensure <strong>microDvdParserInstance</strong> is not null (in case your ParserInstance does not support <![CDATA[ISubtitlesParserWithConfig<TConfig>)]]>
	/// </para>
	/// </summary>
	public interface ISubtitlesParserWithConfig<in TConfig> : ISubtitlesParser // where TConfig : class // Restrict TConfig to a class type to allow it to be a nullable Type
	{
		/// <summary>
		/// Parses a subtitles file stream in a list of SubtitleItem using a specific configuration.
		/// </summary>
		/// <remarks>
		/// <strong>When using this method, make sure you call it from inside a try-catch as exceptions will be thrown on failure to parse.</strong>
		/// </remarks>
		/// <param name="stream">The subtitles file stream to parse</param>
		/// <param name="encoding">The stream encoding (if known)</param>
		/// <param name="configuration">The configuration for the parser.</param>
		/// <returns>The corresponding list of SubtitleItems</returns>
		List<SubtitleModel> ParseStream(Stream stream, Encoding encoding, TConfig configuration);
	}

	public interface ISubtitlesParserWithConfig<TPart, in TConfig> : ISubtitlesParserWithConfig<TConfig>
	{
		IEnumerable<TPart> GetParts(Stream reader, Encoding encoding, TConfig config);
		IAsyncEnumerable<TPart> GetPartsAsync(Stream reader, Encoding encoding, TConfig config, CancellationToken cancellationToken = default);
		SubtitleModel ParsePart(TPart part, bool isFirstPart, TConfig config);
	}
}