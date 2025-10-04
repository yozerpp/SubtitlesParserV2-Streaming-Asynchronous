using System.Collections.Generic;
using System.IO;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using SubtitlesParserV2.Models;

namespace SubtitlesParserV2.Formats.Parsers
{
	/// <summary>
	/// Base interface specifying the required method for a Parser.
	/// Use <see cref="ISubtitlesParserWithConfig{TConfig}"/> to overwrite the default configuration of a specific parser,
	/// if available.
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
		/// <b>When using this method, make sure you call it from inside a try-catch as exceptions will be thrown on failure to parse.</b>
		/// As a alternative, you can use the <see cref="SubtitleParser.ParseStream(Stream, Encoding, IEnumerable{SubtitleFormatType}?)"/> method.
		/// </para>
		/// <para>
		/// This method uses the <see cref="ISubtitlesParser.ParseStreamConsuming"/> to collect the subtitles from the stream.
		/// </para>
		/// </remarks>
		/// <param name="stream">The subtitles file stream to parse</param>
		/// <param name="encoding">The stream encoding (if known)</param>
		/// <returns>The corresponding list of SubtitleItems</returns>
		List<SubtitleModel> ParseStream(Stream stream, Encoding encoding);

		/// <summary>
		/// Parses a subtitles file stream in a list of SubtitleItem using the default configuration, asynchronously.
		/// </summary>
		/// <remarks>
		/// If the parser require additional configuration, this method will uses the
		/// default configuration. To define your own, consider using <see cref="ISubtitlesParserWithConfig{TConfig}"/>.
		/// <para>
		/// <b>When using this method, make sure you call it from inside a try-catch as exceptions will be thrown on failure to parse.</b>
		/// As a alternative, you can use the <see cref="SubtitleParser.ParseStreamAsync(Stream, Encoding, IEnumerable{SubtitleFormatType}?, CancellationToken)"/> method.
		/// </para>
		/// <para>
		/// This method uses the <see cref="ISubtitlesParser.ParseStreamConsumingAsync"/> to collect the subtitles from the stream.
		/// </para>
		/// </remarks>
		/// <param name="stream">The subtitles file stream to parse</param>
		/// <param name="encoding">The stream encoding (if known)</param>
		/// <param name="cancellationToken">Token to abort the operation. This will halt the enumeration of the <see cref="ISubtitlesParser{TPart}.GetPartsAsync"/> that this method internally uses.</param>
		/// <returns>The corresponding list of SubtitleItems</returns>
		/// <returns>List of subtitle items in the stream.</returns>
		Task<List<SubtitleModel>> ParseStreamAsync(Stream stream, Encoding encoding, CancellationToken cancellationToken = default);
		/// <summary>
		/// Gets a consuming parser to the stream. The stream will be read and parsed part-by-part (for definition of parts, see <see cref="ISubtitlesParser{TPart}.GetParts"/>). This means the file will be read progressively each time the enumerable yields, not all at once.
		/// </summary>
		/// <remarks>
		/// If the parser require additional configuration, this method will uses the
		/// default configuration. To define your own, consider using <see cref="ISubtitlesParserWithConfig{TConfig}"/>.
		/// <para>
		/// <b>When using this method, make sure you call it from inside a try-catch as exceptions will be thrown on failure to parse.</b>
		/// As a alternative, you can use the <see cref="SubtitleParser.GetConsumingParser(Stream, Encoding, IEnumerable{SubtitleFormatType}?)"/> method.
		/// </para>
		/// </remarks>
		/// <param name="stream">the stream containing subtitle content</param>
		/// <param name="encoding">encoding of the stream</param>
		/// <returns>Consuming enumerable to parse the stream.</returns>
		IEnumerable<SubtitleModel> ParseStreamConsuming(Stream stream, Encoding encoding);

		/// <summary>
		/// Gets a consuming parser to the stream. The stream will be read and parsed part-by-part (for definition of parts, see <see cref="ISubtitlesParser{TPart}.GetPartsAsync"/>). This means the file will be read progressively each time the enumerable yields, not all at once.
		/// </summary>
		/// <remarks>
		/// If the parser require additional configuration, this method will uses the
		/// default configuration. To define your own, consider using <see cref="ISubtitlesParserWithConfig{TConfig}"/>.
		/// <para>
		/// <b>When using this method, make sure you call it from inside a try-catch as exceptions will be thrown on failure to parse.</b>
		/// As a alternative, you can use the <see cref="SubtitleParser.GetAsyncConsumingParser(Stream, Encoding, IEnumerable{SubtitleFormatType}?, CancellationToken)"/> method.
		/// </para>
		/// </remarks>
		/// <param name="stream">the stream containing subtitle content</param>
		/// <param name="encoding">encoding of the stream</param>
		/// <param name="cancellationToken">Token to abort the operation. This will halt the enumeration of this enumerator and the <see cref="ISubtitlesParser{TPart}.GetPartsAsync"/> that this method internally uses.</param>
		/// <returns>Consuming enumerable to parse the stream.</returns>
		IAsyncEnumerable<SubtitleModel> ParseStreamConsumingAsync(Stream stream, Encoding encoding, CancellationToken cancellationToken = default);
	}
	/// <summary>
	/// internal interface that contains methods for lexing the subtitle content. This interface is implemented by all parsers.
	/// Use <see cref="ISubtitlesParserWithConfig{TPart,TConfig}"/> to overwrite the default configuration of a specific parser,
	/// if available.
	/// <typeparam name="TPart">type of the internally used part. Parser stores the lexed parts into this before they are parsed</typeparam>
	/// </summary>
	internal interface ISubtitlesParser<TPart> : ISubtitlesParser
	{
		/// <summary>
		/// Lex the parts from the subtitle content without parsing them. This reads the content part-by-part, yielding when it completed each part.
		/// </summary>
		/// <remarks>Parts are defined by the implementation and the subtitle format. Generally, it's a subtitle sentence/block.</remarks>
		/// <param name="reader">Stream to read subtitle content</param>
		/// <typeparam name="TPart">The type that the subtitle part will be stored at.</typeparam>
		/// <param name="encoding">encoding of the stream</param>
		/// <returns>An enumerable yielding subtitle parts.</returns>
		IEnumerable<TPart> GetParts(Stream reader, Encoding encoding);

		/// <summary>
		/// Lex the parts from the subtitle content without parsing them. This reads the content part-by-part, yielding when it completed each part.
		/// </summary>
		/// <remarks>Parts are defined by the implementation and the subtitle format. Generally, it's a subtitle sentence/block.</remarks>
		/// <param name="reader">Stream to read subtitle content</param>
		/// <typeparam name="TPart">The type that the subtitle part will be stored at.</typeparam>
		/// <param name="encoding">encoding of the stream</param>
		/// <param name="cancellationToken">token to abort consuming the stream.</param>
		/// <returns>An enumerable yielding subtitle parts.</returns>
		IAsyncEnumerable<TPart> GetPartsAsync(Stream reader, Encoding encoding, CancellationToken cancellationToken = default);
		/// <summary>
		/// Parses the content of the part (as expressed by TPart) into <see cref="SubtitleModel"/>.
		/// </summary>
		/// <param name="part"></param>
		/// <param name="isFirstPart"></param>
		/// <returns></returns>
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
	/// Now ensure <b>microDvdParserInstance</b> is not null (in case your ParserInstance does not support <![CDATA[ISubtitlesParserWithConfig<TConfig>)]]>
	/// </para>
	/// </summary>
	public interface ISubtitlesParserWithConfig<in TConfig> : ISubtitlesParser // where TConfig : class // Restrict TConfig to a class type to allow it to be a nullable Type
	{
		/// <summary>
		/// Parses a subtitles file stream in a list of SubtitleItem using a specific configuration.
		/// </summary>
		/// <remarks>
		/// <b>When using this method, make sure you call it from inside a try-catch as exceptions will be thrown on failure to parse.</b>
		/// </remarks>
		/// <param name="stream">The subtitles file stream to parse</param>
		/// <param name="encoding">The stream encoding (if known)</param>
		/// <param name="configuration">The configuration for the parser.</param>
		/// <returns>The corresponding list of SubtitleItems</returns>
		List<SubtitleModel> ParseStream(Stream stream, Encoding encoding, TConfig configuration);
		/// <summary>
		/// Parses a subtitles file stream in a list of SubtitleItem using a specific configuration, asynchronously.
		/// </summary>
		/// <remarks>
		/// <b>When using this method, make sure you call it from inside a try-catch as exceptions will be thrown on failure to parse.</b>
		/// </remarks>
		/// <param name="stream">The subtitles file stream to parse</param>
		/// <param name="encoding">The stream encoding (if known)</param>
		/// <param name="configuration">The configuration for the parser.</param>
		/// <returns>The corresponding list of SubtitleItems</returns>
		Task<List<SubtitleModel>> ParseStreamAsync(Stream stream, Encoding encoding, TConfig configuration, CancellationToken cancellationToken = default);

		/// <summary>
		/// Consumes the stream with the given <paramref name="configuration"/>. See <see cref="ISubtitlesParser.ParseStreamConsuming"/> for details of the operation.
		/// </summary>
		/// <param name="stream">the stream containing subtitle content</param>
		/// <param name="encoding">encoding of the stream</param>
		/// <param name="configuration">configuration to use</param>
		/// <returns>Consuming enumerable to parse the stream.</returns>
		IEnumerable<SubtitleModel> ParseStreamConsuming(Stream stream, Encoding encoding, TConfig configuration);

		/// <summary>
		/// Consumes the stream with the given <paramref name="configuration"/>, asynchronously. See <see cref="ISubtitlesParser.ParseStreamConsumingAsync"/> for details of the operation.
		/// </summary>
		/// <param name="stream">the stream containing subtitle content</param>
		/// <param name="encoding">encoding of the stream</param>
		/// <param name="configuration">configuration to use</param>
		/// <param name="cancellationToken">token to halt the operation</param>
		/// <returns>Consuming enumerable to parse the stream.</returns>
		IAsyncEnumerable<SubtitleModel> ParseStreamConsumingAsync(Stream stream, Encoding encoding, TConfig configuration, CancellationToken cancellationToken = default);
	}
	/// <summary>
	/// internal interface that contains methods for lexing the subtitle content. Additionally contains configuration to be passed in to the methods
	/// <typeparam name="TConfig">type of configuration</typeparam>
	/// <typeparam name="TPart">type of the internally used part. Parser stores the lexed parts into this before they are parsed</typeparam>
	/// </summary>
	internal interface ISubtitlesParserWithConfig<TPart, in TConfig> : ISubtitlesParserWithConfig<TConfig>
	{
		/// <summary>
		/// Gets a consuming enumerable to parts in the subtitle content in the <paramref name="stream"/>, uses configuration to lex the parts. See <see cref="ISubtitlesParser{TPart}.GetParts"/> for details of the operation.
		/// </summary>
		IEnumerable<TPart> GetParts(Stream stream, Encoding encoding, TConfig config);
		/// <summary>
		/// Gets a asynchronous consuming enumerable to parts in the subtitle content in the <paramref name="stream"/>, uses configuration to lex the parts. See <see cref="ISubtitlesParser{TPart}.GetPartsAsync"/> for details of the operation.
		/// </summary>
		IAsyncEnumerable<TPart> GetPartsAsync(Stream stream, Encoding encoding, TConfig config, CancellationToken cancellationToken = default);
		/// <summary>
		/// Parses the part with the given <paramref name="config"/>. See <see cref="ISubtitlesParser{TPart}.ParsePart"/> for details.
		/// </summary>
		SubtitleModel ParsePart(TPart part, bool isFirstPart, TConfig config);
	}
}