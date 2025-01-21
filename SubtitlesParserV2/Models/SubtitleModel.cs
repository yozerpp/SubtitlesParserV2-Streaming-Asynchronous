using System;
using System.Collections.Generic;

namespace SubtitlesParserV2.Models
{
    /// <summary>
    /// This class is a model used to contains information about a parsed subtitle file or what to write to a subtitle file.
    /// </summary>
    public class SubtitleModel
    {

        //Properties------------------------------------------------------------------

        /// <summary>
        /// Start time in milliseconds.
        /// </summary>
        public int StartTime { get; set; }

        /// <summary>
        /// End time in milliseconds.
        /// </summary>
        public int EndTime { get; set; }

		/// <summary>
		/// The plain-text string from the file
		/// Does not include formatting
		/// </summary>
		public List<string> Lines { get; set; }

        //Constructors-----------------------------------------------------------------

        /// <summary>
        /// The empty constructor
        /// </summary>
        public SubtitleModel()
        {
            Lines = new List<string>();
        }


        // Methods --------------------------------------------------------------------------
        // Show the subtitle values
        public override string ToString()
        {
            TimeSpan startTime = new TimeSpan(0, 0, 0, 0, StartTime);
            TimeSpan endTime = new TimeSpan(0, 0, 0, 0, EndTime);
            return string.Format("{0} --> {1}: {2}", startTime.ToString("G"), endTime.ToString("G"), string.Join(Environment.NewLine, Lines));
        }

    }
}