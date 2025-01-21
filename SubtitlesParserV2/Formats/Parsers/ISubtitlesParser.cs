using System.Collections.Generic;
using System.IO;
using System.Text;
using SubtitlesParserV2.Models;

namespace SubtitlesParserV2.Formats.Parsers
{
	/// <summary>
	/// Base interface specifying the required method for a Parser.
	/// Use <see cref="ISubtitlesParser{TConfig}"/> to overwrite the default configuration of a specific parser,
	/// if available.
	/// </summary>
	public interface ISubtitlesParser
	{
		/// <summary>
		/// Parses a subtitles file stream in a list of SubtitleItem.
		/// </summary>
		/// <remarks>
		/// If the parser require additional configuration, this method will uses the
		/// default configuration. To define your own, consider using <see cref="ISubtitlesParser{TConfig}"/>.
		/// </remarks>
		/// <param name="stream">The subtitles file stream to parse</param>
		/// <param name="encoding">The stream encoding (if known)</param>
		/// <returns>The corresponding list of SubtitleItems</returns>
		List<SubtitleModel> ParseStream(Stream stream, Encoding encoding);
	}

	/// <summary>
	/// Interface specifying the required method for a Parser with a additional <see cref="TConfig"/> for parsers that require additional configuration.
	/// </summary>
	public interface ISubtitlesParser<TConfig> // where TConfig : class // Restrict TConfig to a class type to allow it to be a nullable Type
	{
		/// <summary>
		/// Parses a subtitles file stream in a list of SubtitleItem
		/// </summary>
		/// <param name="stream">The subtitles file stream to parse</param>
		/// <param name="encoding">The stream encoding (if known)</param>
		/// <returns>The corresponding list of SubtitleItems</returns>
		List<SubtitleModel> ParseStream(Stream stream, Encoding encoding, TConfig configuration);
    }
}