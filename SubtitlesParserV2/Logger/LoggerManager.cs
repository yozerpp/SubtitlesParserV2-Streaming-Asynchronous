using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;

namespace SubtitlesParserV2.Logger
{
    /// <summary>
    /// This class implement methods and fields that uses <see cref="ILoggerFactory"/> log support
    /// </summary>
    public static class LoggerManager
    {
        /// <summary>
        /// The logger factory for this library
        /// If null (default), logging is disabled
        /// </summary>
        public static ILoggerFactory? LoggerFactory { get; set; } = null;

        /// <summary>
        /// Get a logger with the given name
        /// </summary>
        /// <param name="loggerName">Name of the logger</param>
        /// <returns>A <see cref="ILogger"/> instance</returns>
        internal static ILogger GetLogger(string loggerName)
        {
            return LoggerFactory?.CreateLogger(loggerName) ?? NullLogger.Instance;
        }

		/// <summary>
		/// Get a logger with the name of the current class
		/// </summary>
		/// <param name="currentClass">The class that will use the logger</param>
		/// <returns>A <see cref="ILogger"/> instance</returns>
		internal static ILogger GetCurrentClassLogger(this object currentClass)
        {
            return GetLogger(currentClass.GetType()?.FullName ?? currentClass.GetType().Name);
        }
    }
}
