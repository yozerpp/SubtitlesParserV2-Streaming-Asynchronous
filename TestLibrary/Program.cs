using System;
using System.IO;
using System.Linq;
using System.Text;
using Microsoft.Extensions.Logging;
using SubtitlesParserV2;
using SubtitlesParserV2.Formats.Parsers;
using SubtitlesParserV2.Models;

namespace TestLibrary
{
    class Program
    {
		public static readonly ILoggerFactory _loggerFactory = LoggerFactory.Create(builder =>
		{
			builder.ClearProviders();
			builder.AddConsole(); // Add the Console logging provider
			builder.SetMinimumLevel(LogLevel.Debug); // Show all debug logs
			//builder.AddFilter("SubtitlesParserV2.*", LogLevel.Debug); // Show dll debug logs
		});

		private static readonly ILogger<Program> _logger = _loggerFactory.CreateLogger<Program>();

		static void Main(string[] args)
        {
			// Configure SubtitleParserV2 logger
			SubtitlesParserV2.Logger.LoggerManager.LoggerFactory = _loggerFactory;

			// Get the memory usage at start
			long initialMemory = GC.GetTotalMemory(true);
			_logger.LogInformation("Initial memory usage: {memory}", initialMemory / 1024.0 / 1024.0);

			while (true) 
            {
				string[] allFiles = BrowseTestSubtitlesFiles();
				_logger.LogInformation("----------------------");
				foreach (string file in allFiles)
				{
					string fileName = Path.GetFileName(file);
					using (FileStream fileStream = File.OpenRead(file))
					{
						try
						{
							SubtitleFormatType? mostLikelyFormat = SubtitleFormat.GetFormatTypeByFileExtensionName(Path.GetExtension(fileName).Replace(".",""));

							SubtitleParserResultModel parserResultModel;
							if (mostLikelyFormat != null) 
							{
								
								// Here, we select the format with a matching file extension name
								parserResultModel = SubtitleParser.ParseStream(fileStream, Encoding.UTF8, mostLikelyFormat.Value);
							} else parserResultModel = SubtitleParser.ParseStream(fileStream, Encoding.UTF8); // Try all parsers
							
							if (parserResultModel.Subtitles.Count != 0)
							{
								int invalidSubtitles = 0;
								// We assume here that the first subtitle time can start at 0 and still be valid
								SubtitleModel firstSubtitle = parserResultModel.Subtitles.First();
								if (firstSubtitle.StartTime < 0 || firstSubtitle.EndTime <= 0) invalidSubtitles++;
								// Check the rest of the subtitles except the last one
								invalidSubtitles += parserResultModel.Subtitles.Skip(1).SkipLast(1).Count(it => it.StartTime <= 0 || it.EndTime <= 0);
								// Verify the last subtitle
								SubtitleModel lastSubtitle = parserResultModel.Subtitles.Last();
								if (lastSubtitle.StartTime <= 0 || lastSubtitle.EndTime <= 0) 
								{
									invalidSubtitles++;
									if (lastSubtitle.EndTime <= 0) _logger.LogWarning("Last subtitle end time was <= 0, this could be normal depending of the file format, but can also indicate a issue with the parser.");
								}

								int invalidSubtitlesPercent = (invalidSubtitles * 100) / parserResultModel.Subtitles.Count;
								_logger.LogInformation("Parsing of file {fileName}: SUCCESS ({itemsCount} items - {invalidPercent}% time corrupted)", fileName, parserResultModel.Subtitles.Count, invalidSubtitlesPercent);
							}
							else
							{
								_logger.LogInformation("Parsing of file {filename}: SUCCESS (No items found!)", fileName);
							}

						}
						catch (Exception ex)
						{
							_logger.LogWarning("Parsing of file {fileName}: FAILURE\n{ex}", fileName, ex);
						}
					}
					_logger.LogInformation("----------------------");
				}
				// Get the memory usage after parsing
				_logger.LogInformation("Memory usage after parsing: {memory}",  GC.GetTotalMemory(true) / 1024.0 / 1024.0);
				// Force GC to run
				GC.Collect();
				GC.WaitForPendingFinalizers();
				GC.Collect();
				_logger.LogInformation("Memory usage after GC: {memory}", GC.GetTotalMemory(true) / 1024.0 / 1024.0);
				Console.ReadLine();
			}
        }

        private static string[] BrowseTestSubtitlesFiles()
        {
            const string subFilesDirectory = @"Content\TestFiles";
            var currentPath = Directory.GetCurrentDirectory();
            var completePath = Path.Combine(currentPath, subFilesDirectory);

            var allFiles = Directory.GetFiles(completePath);
            return allFiles;
        }
    }
}
