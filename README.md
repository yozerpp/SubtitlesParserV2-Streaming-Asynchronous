[![Publish to NuGet Gallery](https://github.com/kitsumed/SubtitlesParserV2/actions/workflows/Create%20NuGet%20release.yml/badge.svg)](https://github.com/kitsumed/SubtitlesParserV2/actions/workflows/Create%20NuGet%20release.yml) [![NuGet Version](https://img.shields.io/nuget/v/SubtitlesParserV2)](https://www.nuget.org/packages/SubtitlesParserV2/)
## SubtitlesParserV2
Universal subtitles parser which aims at supporting **parsing** for all subtitle formats.
For more info on subtitles formats, see this page: http://en.wikipedia.org/wiki/Category:Subtitle_file_formats
> [!NOTE]
> This is a fork/continuation of the original [SubtitlesParser](https://github.com/AlexPoint/SubtitlesParser) that seems to be a bit outdated / not updated anymore on nuget. Since I needed to parse subtitles in one of my projects, I decided to take this existing library, update the dependencies, and rewrite some parts of it in my own way at the same time, fixing / improving some parsers.

## Supported Parsers/Writers
| Subtitle Format                         | Parser | Writer | Extensions Detection            | Supported Specs / Versions     |
|-----------------------------------------|:------:|:------:|:-------------------------------:|--------------------------------|
| SubRip                                  | ✅     | ❌     | `.srt`                         | `srt`                          |
| LRC                                     | ✅     | ❌     | `.lrc`                         | `Core LRC`, `Enhanced LRC format (A2 extension)`|
| TMPlayer                                | ✅     | ❌     | `.tmp`                         | [`long & short codes`](https://wiki.multimedia.cx/index.php/TMPlayer)|
| MicroDvd                                | ✅     | ❌     | `.sub`                         | `MicroDVD`                     |
| SubViewer                               | ✅     | ❌     | `.sbv`                         | `SubViewer2`, `SubViewer1`     |
| SubStationAlpha                         | ✅     | ❌     | `.ssa`, `.ass`                 | `v4.00` + backward             |
| Timed Text Markup Language (TTML)       | ✅     | ❌     | `.ttml`, `.dfxp`, `.xml`, [`.itt`](https://web.archive.org/web/20250304000638/https://help.apple.com/itc/filmspec/#/itc5866cbf7c)| `TTML 1.0`, `TTML 2.0`     |
| WebVTT                                  | ✅     | ❌     | `.vtt`                         | `WebVTT`                       |
| Synchronized Accessible Media Interchange (SAMI) | ✅ | ❌| `.smi`, `.sami`                | `Only Support Time in MS`      |
| YouTube Timed Text (YoutubeXml)         | ✅     | ❌     | `.ytt`, `.srv3`, `.srv2`, `.srv1` | `srv3`, `srv2`, `srv1`      |
| MPL2                                    | ✅     | ❌     | `.mpl`, `.mpl2`                | [`MPL`](https://wiki.multimedia.cx/index.php/MPL2)|
| Universal Subtitle Format (USF)         | ✅     | ❌     | `.usf`                         | `v1.1`                         |

### Quickstart
#### Universal parser

If you don't specify the subtitle format, SubtitlesParserV2 will try all the registered parsers with the default configuration

```csharp
using (FileStream fileStream = File.OpenRead(pathToSrtFile)){
	// Try to parse with all supported parsers
	SubtitleParserResultModel? result = SubtitleParser.ParseStream(fileStream, Encoding.UTF8)
	// Access the Subtitles with: result.Subtitles
	// Access the Subtitle parser that was used with: result.FormatType
	// Note that if all parsers fail, the method will return: null
}
```

> [!TIP]
> When accessing subtitles with `result.Subtitles`, if the start or end time **value is negative** (generally `-1`), it means there was no way to determine the timestamp, or the extension failed to parse it correctly (and didn't fail).
> Having a `-1` does not mean your file is corrupted. This behavior is expected in some formats like **LRC** and **TMPlayer**, where only the start time of a subtitle is provided in the file itself. In these cases, a `-1` at the end time of the very last subtitle is normal. *(Normal because for theses formats, the library use the next subtitle start time to know the previous subtitle end time)*

#### Get Subtitle format by extension (Extensions detection)

```csharp
string fileName = Path.GetFileName(file);
// Get format enum
SubtitleFormatType? mostLikelyFormat = SubtitleFormat.GetFormatTypeByFileExtensionName(Path.GetExtension(fileName).Replace(".",""));
// Get format instance
if (mostLikelyFormat != null) 
{
	SubtitleFormat format = SubtitleFormat.GetFormat(mostLikelyFormat.Value);
	Console.WriteLine($"Matching format is : {format.Name}");
}
```

#### Specific parser (default configuration)

You can use one/multiples specific parser:
```csharp
using (FileStream fileStream = File.OpenRead(pathToSrtFile)){
	// Try to parse with one specific parser using default configuration
	SubtitleParserResultModel result = SubtitleParser.ParseStream(fileStream, Encoding.UTF8, SubtitleFormatType.SubStationAlpha)
	// Try to parse with multiple parser using default configuration
	SubtitleParserResultModel result = SubtitleParser.ParseStream(fileStream, Encoding.UTF8, new[] { SubtitleFormatType.SubStationAlpha, SubtitleFormatType.LRC });
}
```
#### Specific parser (Advanced configurations)

You can also specify advanced configurations for parsers that support it.
> [!NOTE]
> Defining advanced configurations is not possible inside the `SubtitleParser.ParseStream` method. You need to get the parser instance as a `ISubtitlesParser<TConfig>` (interface).
> **TConfig** refers to the parser name followed by `ParserConfig`. As a example, `MicroDvd` would become `MicroDvdParserConfig`.

> [!CAUTION]
> When using a parser directly from its instance, **you must handle any thrown errors using a try-catch statement**. Otherwise, your code may fail when the parser cannot parse the given stream.

```csharp
// Get the format
SubtitleFormat format = SubtitleFormat.GetFormat(SubtitleFormatType.MicroDvd);
// Get the instance as a advanced parser
ISubtitlesParser<MicroDvdParserConfig> microDvdParserInstance = format.ParserInstance as ISubtitlesParser<MicroDvdParserConfig>;
// Parse
microDvdParserInstance.ParseStream(fileStream, Encoding.UTF8, new MicroDvdParserConfig() 
{
	Framerate = 30, // Force the parser to use a framerate of 30
});

```
### Logging
SubtitlesParserV2 implements [microsoft.extensions.logging.abstractions](https://www.nuget.org/packages/microsoft.extensions.logging.abstractions/) to allow you to redirect the log output to your own logging method (using a LoggerFactory)
#### Base example
```csharp
// Assuming a LoggerFactory already exist
ILoggerFactory existingLoggerFactory = LoggerFactory.Create(builder =>
{
	builder.AddConsole(); // Adding the default console logging
});

// Set SubtitlesParserV2 LoggerFactory to the existing one
LoggerManager.LoggerFactory = existingLoggerFactory;
```
#### ASP.NET Core Websites
```csharp
// Get the app loggerFactory
ILoggerFactory loggerFactory = app.Services.GetRequiredService<ILoggerFactory>();
LoggerManager.LoggerFactory = loggerFactory; // Redirect SubtitlesParserV2 logs to our LoggerFactory
```

## Licenses / Acknowledgements
**Current code** is licensed (*sublicensed as allowed per the original project MIT license*) under the **GNU Lesser General Public License v3.0** (LGPLv3).
> [!TIP]
> While this is not legal advice, a common misconception is that the [LGPL license](https://choosealicense.com/licenses/lgpl-3.0/), due to having "GPL" in its name, requires you to license your program under the GPL or the same license. This is not necessarily true. Since this project is a library,
> the requirements depend on how you use the library and the compatibility between this project's license and your project's license. However, please note that this tip does not override the specific requirements of the LGPL license. There may be
> additional obligations based on your use case. For further clarification, you can refer to the [LGPL FAQ about static vs dynamic linking requirements](https://www.gnu.org/licenses/gpl-faq.html#LGPLStaticVsDynamic), this [Reddit post](https://www.reddit.com/r/rust/comments/fevz37/comment/fjsg393/), or this [blog post](https://coding.abel.nu/2016/10/the-lgpl-license/#:~:text=LGPL%20is%20not%20%E2%80%9Ccontagious%E2%80%9D%20in,affects%20the%20component%20under%20LGPL.). **Again**, **this is not legal advice**.

⚠️ The **original version**, [available here](https://github.com/AlexPoint/SubtitlesParser/tree/3e3b97409481dccaa5bb96391d1c066cf0f2dfef) (*link redirects you to the exact commit from which the project was forked*, the commit history was discarded due to [concerns about some files in the commit history](https://github.com/kitsumed/SubtitlesParserV2/issues/1#issuecomment-2661584721).), is licensed under the original project made by AlexPoint, license/credits:
```
The MIT License (MIT)

Copyright (c) 2015

Permission is hereby granted, free of charge, to any person obtaining a copy
of this software and associated documentation files (the "Software"), to deal
in the Software without restriction, including without limitation the rights
to use, copy, modify, merge, publish, distribute, sublicense, and/or sell
copies of the Software, and to permit persons to whom the Software is
furnished to do so, subject to the following conditions:

The above copyright notice and this permission notice shall be included in
all copies or substantial portions of the Software.

THE SOFTWARE IS PROVIDED "AS IS", WITHOUT WARRANTY OF ANY KIND, EXPRESS OR
IMPLIED, INCLUDING BUT NOT LIMITED TO THE WARRANTIES OF MERCHANTABILITY,
FITNESS FOR A PARTICULAR PURPOSE AND NONINFRINGEMENT. IN NO EVENT SHALL THE
AUTHORS OR COPYRIGHT HOLDERS BE LIABLE FOR ANY CLAIM, DAMAGES OR OTHER
LIABILITY, WHETHER IN AN ACTION OF CONTRACT, TORT OR OTHERWISE, ARISING FROM,
OUT OF OR IN CONNECTION WITH THE SOFTWARE OR THE USE OR OTHER DEALINGS IN
THE SOFTWARE.
```
