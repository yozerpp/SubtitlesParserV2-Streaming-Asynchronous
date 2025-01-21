using System.Collections.Generic;

namespace SubtitlesParserV2.Models
{
	/// <summary>
	/// This class contain a model for the subtitle parser results.
	/// </summary>
	public class SubtitleParserResultModel
	{
		/// <summary>
		/// The subtitle format that was used to parse the subtitle data
		/// </summary>
		public SubtitleFormatType FormatType { get; } 

		/// <summary>
		/// The subtitle data
		/// </summary>
		public List<SubtitleModel> Subtitles { get; }

		// Defined as internal to prevent creation outside of the assambly
        internal SubtitleParserResultModel(SubtitleFormatType formatType, List<SubtitleModel> subtitleModel)
        {
            FormatType = formatType;
			Subtitles = subtitleModel;
        }
    }
}
