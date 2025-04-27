using SubtitlesParserV2.Models;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using Xunit;

namespace SubtitlesParserV2.Tests
{
    /// <summary>
    /// Class with method used to test the parsing of files.
    /// </summary>
    public class Test_Parsers
    {
		/// <summary>
		/// Get a list of all files the selected parser should support.
		/// </summary>
		/// <param name="parserFormatType">The selected parser</param>
		/// <returns>A lazy list with the files the parser should be able to parse</returns>
        private IEnumerable<string> GetFilesRelatedToParser(SubtitleFormatType parserFormatType) 
        {
			// File extensions of a specific parser
			string[] parserExtensions = SubtitleFormat.GetFormat(parserFormatType).Extensions;
			// Only keep files that have extension marked as supposed extensions file
			return TestFilesManager.TestFilesPath.Where(filePath => parserExtensions.Any(extension => extension == Path.GetExtension(filePath).Replace(".", "")));
		}

		/// <summary>
		/// Loop trought every subtitles and verify the times values for invalid times.
		/// </summary>
		/// <param name="subtitles"></param>
		/// <param name="firstCanBeInvalid">Ignore if the first subtitle is invalid</param>
		/// <param name="middleCanBeInvalid">Ignore all subtitles between the first and last one if invalid</param>
		/// <param name="lastCanBeInvalid">Ignore if the last subtitle is invalid</param>
		/// <returns>The number of invalid subtitles found.</returns>
		private int CountInvalidTimestamps(List<SubtitleModel> subtitles, bool firstCanBeInvalid = false, bool middleCanBeInvalid = false, bool lastCanBeInvalid = false) 
        {
            int invalidSubtitles = 0;
			// We assume here that the first subtitle time can start at 0 and still be valid
			SubtitleModel firstSubtitle = subtitles.First();
			if ((firstSubtitle.StartTime < 0 || firstSubtitle.EndTime <= 0) && !firstCanBeInvalid) invalidSubtitles++;
			// Check the rest of the subtitles except the last one
            if (!middleCanBeInvalid) invalidSubtitles += subtitles.Skip(1).SkipLast(1).Count(subtitle => subtitle.StartTime <= 0 || subtitle.EndTime <= 0);
			// Verify the last subtitle
			SubtitleModel lastSubtitle = subtitles.Last();
			if ((lastSubtitle.StartTime <= 0 || lastSubtitle.EndTime <= 0) && !lastCanBeInvalid ) invalidSubtitles++;
            return invalidSubtitles;
		}

		// --------------------------------------------------------------------------------------------------------------------------

		[Fact]
        private void Parse_SubRip()
        {
			SubtitleFormatType targetFormatType = SubtitleFormatType.SubRip;

            Dictionary<string, int> invalidTimestamps = new Dictionary<string, int>();
			foreach (string filePath in GetFilesRelatedToParser(targetFormatType))
            {
				using FileStream fileStream = new FileStream(filePath, FileMode.Open, FileAccess.Read);
				SubtitleParserResultModel parsingResult = SubtitleParser.ParseStream(fileStream, Encoding.UTF8, targetFormatType);

                // Verify for timestamp
                invalidTimestamps.Add(filePath, CountInvalidTimestamps(parsingResult.Subtitles));
			}
            // Verify that every timestamp is valid
            Assert.Contains(invalidTimestamps, entry => entry.Value == 0);
		}

		[Fact]
		private void Parse_LRC()
		{
			SubtitleFormatType targetFormatType = SubtitleFormatType.LRC;

			Dictionary<string, int> invalidTimestamps = new Dictionary<string, int>();
			foreach (string filePath in GetFilesRelatedToParser(targetFormatType))
			{
				using FileStream fileStream = new FileStream(filePath, FileMode.Open, FileAccess.Read);
				SubtitleParserResultModel parsingResult = SubtitleParser.ParseStream(fileStream, Encoding.UTF8, targetFormatType);

				// Verify for timestamp
				// NOTE: LRC last subtitle does not have a valid timestamp as per by file format
				invalidTimestamps.Add(filePath, CountInvalidTimestamps(parsingResult.Subtitles, false, false, true));
			}
			// Verify that every timestamp is valid
			Assert.Contains(invalidTimestamps, entry => entry.Value == 0);
		}

		[Fact]
		private void Parse_TMPlayer()
		{
			SubtitleFormatType targetFormatType = SubtitleFormatType.TMPlayer;

			Dictionary<string, int> invalidTimestamps = new Dictionary<string, int>();
			foreach (string filePath in GetFilesRelatedToParser(targetFormatType))
			{
				using FileStream fileStream = new FileStream(filePath, FileMode.Open, FileAccess.Read);
				SubtitleParserResultModel parsingResult = SubtitleParser.ParseStream(fileStream, Encoding.UTF8, targetFormatType);

				// Verify for timestamp
				// NOTE: TMPlayer last subtitle does not have a valid timestamp as per by file format
				invalidTimestamps.Add(filePath, CountInvalidTimestamps(parsingResult.Subtitles, false, false, true));
			}
			// Verify that every timestamp is valid
			Assert.Contains(invalidTimestamps, entry => entry.Value == 0);
		}

		[Fact]
		private void Parse_MicroDvd()
		{
			SubtitleFormatType targetFormatType = SubtitleFormatType.MicroDvd;

			Dictionary<string, int> invalidTimestamps = new Dictionary<string, int>();
			foreach (string filePath in GetFilesRelatedToParser(targetFormatType))
			{
				using FileStream fileStream = new FileStream(filePath, FileMode.Open, FileAccess.Read);
				SubtitleParserResultModel parsingResult = SubtitleParser.ParseStream(fileStream, Encoding.UTF8, targetFormatType);

				// Verify for timestamp
				invalidTimestamps.Add(filePath, CountInvalidTimestamps(parsingResult.Subtitles));
			}
			// Verify that every timestamp is valid
			Assert.Contains(invalidTimestamps, entry => entry.Value == 0);
		}

		[Fact]
		private void Parse_SubViewer()
		{
			SubtitleFormatType targetFormatType = SubtitleFormatType.SubViewer;

			Dictionary<string, int> invalidTimestamps = new Dictionary<string, int>();
			foreach (string filePath in GetFilesRelatedToParser(targetFormatType))
			{
				using FileStream fileStream = new FileStream(filePath, FileMode.Open, FileAccess.Read);
				SubtitleParserResultModel parsingResult = SubtitleParser.ParseStream(fileStream, Encoding.UTF8, targetFormatType);

				// Verify for timestamp
				invalidTimestamps.Add(filePath, CountInvalidTimestamps(parsingResult.Subtitles));
			}
			// Verify that every timestamp is valid
			Assert.Contains(invalidTimestamps, entry => entry.Value == 0);
		}

		[Fact]
		private void Parse_SubStationAlpha()
		{
			SubtitleFormatType targetFormatType = SubtitleFormatType.SubStationAlpha;

			Dictionary<string, int> invalidTimestamps = new Dictionary<string, int>();
			foreach (string filePath in GetFilesRelatedToParser(targetFormatType))
			{
				using FileStream fileStream = new FileStream(filePath, FileMode.Open, FileAccess.Read);
				SubtitleParserResultModel parsingResult = SubtitleParser.ParseStream(fileStream, Encoding.UTF8, targetFormatType);

				// Verify for timestamp
				invalidTimestamps.Add(filePath, CountInvalidTimestamps(parsingResult.Subtitles));
			}
			// Verify that every timestamp is valid
			Assert.Contains(invalidTimestamps, entry => entry.Value == 0);
		}

		[Fact]
		private void Parse_TTML()
		{
			SubtitleFormatType targetFormatType = SubtitleFormatType.TTML;

			Dictionary<string, int> invalidTimestamps = new Dictionary<string, int>();
			foreach (string filePath in GetFilesRelatedToParser(targetFormatType))
			{
				using FileStream fileStream = new FileStream(filePath, FileMode.Open, FileAccess.Read);
				SubtitleParserResultModel parsingResult = SubtitleParser.ParseStream(fileStream, Encoding.UTF8, targetFormatType);

				// Verify for timestamp
				invalidTimestamps.Add(filePath, CountInvalidTimestamps(parsingResult.Subtitles));
			}
			// Verify that every timestamp is valid
			Assert.Contains(invalidTimestamps, entry => entry.Value == 0);
		}

		[Fact]
		private void Parse_WebVTT()
		{
			SubtitleFormatType targetFormatType = SubtitleFormatType.WebVTT;

			Dictionary<string, int> invalidTimestamps = new Dictionary<string, int>();
			foreach (string filePath in GetFilesRelatedToParser(targetFormatType))
			{
				using FileStream fileStream = new FileStream(filePath, FileMode.Open, FileAccess.Read);
				SubtitleParserResultModel parsingResult = SubtitleParser.ParseStream(fileStream, Encoding.UTF8, targetFormatType);

				// Verify for timestamp
				invalidTimestamps.Add(filePath, CountInvalidTimestamps(parsingResult.Subtitles));
			}
			// Verify that every timestamp is valid
			Assert.Contains(invalidTimestamps, entry => entry.Value == 0);
		}

		[Fact]
		private void Parse_SAMI()
		{
			SubtitleFormatType targetFormatType = SubtitleFormatType.SAMI;

			Dictionary<string, int> invalidTimestamps = new Dictionary<string, int>();
			foreach (string filePath in GetFilesRelatedToParser(targetFormatType))
			{
				using FileStream fileStream = new FileStream(filePath, FileMode.Open, FileAccess.Read);
				SubtitleParserResultModel parsingResult = SubtitleParser.ParseStream(fileStream, Encoding.UTF8, targetFormatType);

				// Verify for timestamp
				// NOTE: SAMI last subtitle does not have a valid timestamp as per by file format
				invalidTimestamps.Add(filePath, CountInvalidTimestamps(parsingResult.Subtitles, false, false, true));
			}
			// Verify that every timestamp is valid
			Assert.Contains(invalidTimestamps, entry => entry.Value == 0);
		}

		[Fact]
		private void Parse_YoutubeXml()
		{
			SubtitleFormatType targetFormatType = SubtitleFormatType.YoutubeXml;

			Dictionary<string, int> invalidTimestamps = new Dictionary<string, int>();
			foreach (string filePath in GetFilesRelatedToParser(targetFormatType))
			{
				using FileStream fileStream = new FileStream(filePath, FileMode.Open, FileAccess.Read);
				SubtitleParserResultModel parsingResult = SubtitleParser.ParseStream(fileStream, Encoding.UTF8, targetFormatType);

				// Verify for timestamp
				invalidTimestamps.Add(filePath, CountInvalidTimestamps(parsingResult.Subtitles));
			}
			// Verify that every timestamp is valid
			Assert.Contains(invalidTimestamps, entry => entry.Value == 0);
		}

		[Fact]
		private void Parse_MPL2()
		{
			SubtitleFormatType targetFormatType = SubtitleFormatType.MPL2;

			Dictionary<string, int> invalidTimestamps = new Dictionary<string, int>();
			foreach (string filePath in GetFilesRelatedToParser(targetFormatType))
			{
				using FileStream fileStream = new FileStream(filePath, FileMode.Open, FileAccess.Read);
				SubtitleParserResultModel parsingResult = SubtitleParser.ParseStream(fileStream, Encoding.UTF8, targetFormatType);

				// Verify for timestamp
				invalidTimestamps.Add(filePath, CountInvalidTimestamps(parsingResult.Subtitles));
			}
			// Verify that every timestamp is valid
			Assert.Contains(invalidTimestamps, entry => entry.Value == 0);
		}
	}
}
