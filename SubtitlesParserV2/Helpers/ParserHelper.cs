using System;
using System.Collections.Generic;
using System.Globalization;
using System.Text;
using System.Xml;

namespace SubtitlesParserV2.Helpers
{
	/// <summary>
	/// This class contains helper methods for parsing subtitles.
	/// </summary>
	internal static class ParserHelper
    {
		/// <summary>
		/// Store a list of xml elements that are allowed as child elements of the current element when reading with <see cref="XmlReader"/>.
		/// Theses element will get their text value read and appended together when using <see cref="XmlReadCurrentElementInnerText(XmlReader)"/>
		/// </summary>
		private static readonly string[] XmlReaderAllowedChildElements = new string[] { "span", "font", "b", "u", "i", "p", "br", "string", "text", "karaoke", "k" };

		/// <summary>
		/// Takes an string, prase it as a <see cref="TimeSpan"/> timecode and turn it into milliseconds.
		/// </summary>
		/// <remarks>
		/// <strong>Only uses this method when the line you are reading does not require format specific formatting.</strong>
		/// </remarks>
		/// <param name="s">The string to parse</param>
		/// <returns>The parsed timecode in milliseconds. If the parsing was unsuccessful, -1 is returned.</returns>
		internal static int ParseTimeSpanLineAsMilliseconds(string s)
		{
			TimeSpan result;
			if (TimeSpan.TryParse(s, CultureInfo.InvariantCulture, out result))
			{
				int nbOfMs = (int)result.TotalMilliseconds;
				return nbOfMs;
			}
			else
			{
				return -1;
			}
		}

		/// <summary>
		/// Takes an xml reader and reads the inner elements (childs) to get all of the text values.
		/// All texte will be appended together without adding or removing spaces, the final returned result is trimmed (per lines).
		/// </summary>
		/// 
		/// <remarks>
		/// <strong>Will only read child elements with localnames in the <see cref="XmlReaderAllowedChildElements"/> array.</strong>.
		/// <para>Break elements (br) are treated as the start of a new line.</para>
		/// </remarks>
		/// <param name="reader">The xml reader</param>
		/// <returns>All child elements text appended together, trimmed and separated in a list per new lines</returns>
		internal static List<string> XmlReadCurrentElementInnerText(XmlReader reader)
		{
			// Store all of the lines part of the current element (break <br> elements)
			List<string> lineList = new List<string>();
			// Store the current text value
			StringBuilder currLineBuilder = new StringBuilder();

			// Local internal method to clear existing line builder into list and start a new one
			void StartNewLine() 
			{
				// Ensure our current line is not empty before starting a new line
				if (currLineBuilder.Length >= 1) 
				{
					currLineBuilder.Replace("\r", string.Empty).Replace("\n", string.Empty).Replace("\t", string.Empty);
					lineList.Add(currLineBuilder.ToString().Trim());
					currLineBuilder.Clear();
				}
			}

			// Ensure our current element has at least one child
			if (!reader.IsEmptyElement)
			{
				// Store the informations of your current element to know when we reach the end of it
				string rootElementName = reader.LocalName;
				int rootElementDepth = reader.Depth;

				// We store the previous element name to perform a check when we reach a text node, allowing us to allow or deny a text node
				// if it's not in the allowed elements name list.
				string previousElementName = rootElementName;
				while (reader.Read())
				{
					// Check for <br> elements (start a new line)
					if (reader.NodeType == XmlNodeType.Element && reader.LocalName == "br") StartNewLine();
					// Check for text (append the text to the current line) *only if previously read element was a allowed child element or the root element itself
					// For example, if the previous element was something like "<image>", then we will not append the text to the current line, as it is not allowed.
					else if (reader.NodeType == XmlNodeType.Text && (previousElementName == rootElementName || Array.Exists(XmlReaderAllowedChildElements, allowedElementName => previousElementName == allowedElementName)))
					{
						currLineBuilder.Append(reader.Value);
					}
					// Read specific child elements text
					else if (reader.NodeType == XmlNodeType.Element && Array.Exists(XmlReaderAllowedChildElements, allowedElementName => reader.Name == allowedElementName))
					{
						// Read the content of (<span> / other name) and it's childs
						List<string> childLines = XmlReadCurrentElementInnerText(reader);
						for (int i = 0; i < childLines.Count; i++)
						{
							// If we are not at the first child line, we need to start a new current line
							// as we are processing a new child line
							if (i >= 1) StartNewLine();
							// Append the child line to our current line
							currLineBuilder.Append(childLines[i]);
						}
					}
					// If we reach the end of the current element block (no more childs inside the element), we stop reading
					else if (reader.NodeType == XmlNodeType.EndElement && reader.LocalName == rootElementName && rootElementDepth == reader.Depth) break;
					
					// Update the previous element name to the current one before we read next element
					previousElementName = reader.LocalName;
				}
			}

			// We call as if we are at the end of the parsing and our current line still has parsed content inside (this happen when there was only 1 line)
			// we need to have it added to the list.
			StartNewLine();

			return lineList;
		}
	}
}
