using SubtitlesParserV2.Formats.Parsers;
using System;
using System.Collections.Generic;
using System.Linq;

#pragma warning disable CS1591
namespace SubtitlesParserV2
{
    /// <summary>
    /// Contains a list of all of the supported formats
    /// </summary>
    public enum SubtitleFormatType
	{
		SubRip,
		LRC,
		MicroDvd,
		SubViewer,
		SubStationAlpha,
		TTML,
		WebVTT,
		SAMI,
		YoutubeXml
	}

	/// <summary>
	/// This class is used to store informations about the supported parsers and their parsing instances.
	/// </summary>
	public class SubtitleFormat
	{
        // Properties -----------------------------------------

        /// <summary>
        /// The format name
        /// </summary>
        public string Name { get; }

        /// <summary>
        /// The list of extension name used by the format, if it exists, else null
        /// </summary>
        public string[]? Extensions { get; }

        /// <summary>
        /// Contains a instance of the current format parser
        /// </summary>
        public ISubtitlesParser ParserInstance { get; }

		//public ISubtitlesWriter WriterInstance { get; }

		/// <summary>
		/// Get the SubtitleFormatType of this SubtitleFormat instance
		/// </summary>
		public SubtitleFormatType FormatType => GetFormatTypeSubtitleFromFormatInstance(this);


		/// <summary>
		/// Private CTOR prevent the initialisation of a new SubtitlesFormat instance outside of
		/// this class.
		/// </summary>
		private SubtitleFormat(string name, string[]? extensions, ISubtitlesParser parserInstance)
        {
            Name = name;
            Extensions = extensions;
            ParserInstance = parserInstance;
        }

		/// <summary>
		/// This dictionary contains all of the supported formats and their parsers/writer instances.
		/// <code>
		/// // Access a specific parser by doing this
		/// SubtitleFormat.GetFormat();
		/// </code>
		/// </summary>
		public static readonly Dictionary<SubtitleFormatType, SubtitleFormat> Formats = new Dictionary<SubtitleFormatType, SubtitleFormat>()
		{
			{ SubtitleFormatType.SubRip, new SubtitleFormat("SubRip", new string[] {"srt"}, new SrtParser()) },
			{ SubtitleFormatType.LRC, new SubtitleFormat("LRC", new string[] {"lrc"}, new LrcParser()) },
			{ SubtitleFormatType.MicroDvd, new SubtitleFormat("MicroDvd", new string[] {"sub"}, new MicroDvdParser()) },
			{ SubtitleFormatType.SubViewer, new SubtitleFormat("SubViewer", new string[] {"sbv"}, new SubViewerParser()) },
			{ SubtitleFormatType.SubStationAlpha, new SubtitleFormat("SubStationAlpha", new string[] {"ssa", "ass"}, new SsaParser()) },
			{ SubtitleFormatType.TTML, new SubtitleFormat("TTML", new string[] {"ttml", "dfxp"}, new TtmlParser()) },
			{ SubtitleFormatType.WebVTT, new SubtitleFormat("WebVTT", new string[] {"vtt"}, new VttParser()) },
			{ SubtitleFormatType.SAMI, new SubtitleFormat("Synchronized Accessible Media Interchange", new string[] {"smi", "sami"}, new SamiParser()) },
			{ SubtitleFormatType.YoutubeXml, new SubtitleFormat("YouTube Timed Text (YoutubeXml)", new string[] {"ytt", "srv3", "srv2", "srv1"}, new YttXmlParser()) }
		};

		/// <summary>
		/// Get a list of all of the <see cref="SubtitleFormat"/> supported.
		/// </summary>
		public static IEnumerable<SubtitleFormat> AllFormats => Formats.Values;

		/// <summary>
		/// Get the <see cref="SubtitleFormatType"/> of the format using the same file extension.
		/// Not case sensitive.
		/// </summary>
		/// <remarks>
		/// You should fallback to parsing with all other parsers if parsing with the result
		/// failed. File extension might not always represent the real subtitle format.
		/// </remarks>
		/// <param name="extension">The file extension name (Ex: srt, sub, ssa)</param>
		/// <returns>The SubtitleFormatType that matched the extension name or null</returns>
		// Compare both string, ignoring upper and lowercase
		public static SubtitleFormatType? GetFormatTypeByFileExtensionName(string extension) 
		{
			// We get the formatType as a IEnumerable because "FirstOrDefault" return the default value of a enum (0), the first element
			// insead of null, even if the enum is nullable.
			IEnumerable<SubtitleFormatType>? formatType = Formats.Where(format => format.Value?.Extensions?.Any(currFormatExtension => extension.Equals(currFormatExtension, StringComparison.InvariantCultureIgnoreCase)) ?? false)
			.Select(format => format.Key);

			if (formatType.Any()) 
			{
				return formatType.First();
			}
			return null;
		}

		/// <summary>
		/// Get the <see cref="SubtitleFormatType"/> of the format using the format name.
		/// Not case sensitive.
		/// </summary>
		/// <param name="name">The file extension name (Ex: SubRip, MicroDvd, WebVTT)</param>
		/// <returns>The SubtitleFormatType that matched the name or null</returns>
		// Compare both string, ignoring upper and lowercase
		public static SubtitleFormatType? GetFormatTypeByName(string name) 
		{
			// We get the formatType as a IEnumerable because "FirstOrDefault" return the default value of a enum (0), the first element
			// insead of null, even if the enum is nullable.
			IEnumerable<SubtitleFormatType>? formatType = Formats.Where(format => name.Equals(format.Value.Name, StringComparison.InvariantCultureIgnoreCase))
			.Select(format => format.Key);

			if (formatType.Any())
			{
				return formatType.First();
			}
			return null;
		}

		/// <summary>
		/// Get the <see cref="SubtitleFormatType"/> of your <see cref="SubtitleFormat"/> instance.
		/// </summary>
		/// <param name="SubtitleFormat">The subtitle format instance</param>
		/// <returns>The SubtitleFormatType if your instance</returns>
		// Compare both string, ignoring upper and lowercase
		public static SubtitleFormatType GetFormatTypeSubtitleFromFormatInstance(SubtitleFormat SubtitleFormat) => Formats.First(format => SubtitleFormat == format.Value).Key;

		/// <summary>
		/// Get the instance of a specific <see cref="SubtitleFormat"/> by selecting a <see cref="SubtitleFormatType"/>.
		/// </summary>
		/// <param name="formatType">The format you want</param>
		/// <returns>The SubtitleFormat</returns>
		public static SubtitleFormat GetFormat(SubtitleFormatType formatType)
		{
			// If this throw a error, it mean a parser was not well implemented into the enum & dictionary
			return GetFormat(new SubtitleFormatType[] { formatType }).First();
		}
		/// <summary>
		/// Get the instance of a specific <see cref="SubtitleFormat"/> by selecting a <see cref="SubtitleFormatType"/>.
		/// Can handle multiple <see cref="SubtitleFormatType"/>.
		/// </summary>
		/// <param name="formatsType">The formats you want</param>
		/// <returns>The SubtitleFormat</returns>
		public static IEnumerable<SubtitleFormat> GetFormat(IEnumerable<SubtitleFormatType> formatsType)
		{
			return Formats.Where(format => formatsType.Any(formatType => format.Key == formatType))
				.Select(format => format.Value);
		}
	}
}

