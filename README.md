## SubtitlesParserV2
Universal subtitles parser which aims at supporting **parsing** for all subtitle formats.
For more info on subtitles formats, see this page: http://en.wikipedia.org/wiki/Category:Subtitle_file_formats
> [!NOTE]
> This is a fork/continuation of the original [SubtitlesParser](https://github.com/AlexPoint/SubtitlesParser) that seems to be a bit outdated / not updated anymore on nuget. Since I needed to parse subtitles in one of my projects, I decided to take this existing library, update the dependencies, and rewrite some parts of it in my own way at the same time, fixing / improving some parsers.

For now, 7 different formats are supported for parsing:
* MicroDvd	https://github.com/AlexPoint/SubtitlesParser/blob/master/SubtitlesParser/Classes/Parsers/MicroDvdParser.cs
* SubRip	https://github.com/AlexPoint/SubtitlesParser/blob/master/SubtitlesParser/Classes/Parsers/SrtParser.cs
* SubStationAlpha	https://github.com/AlexPoint/SubtitlesParser/blob/master/SubtitlesParser/Classes/Parsers/SsaParser.cs
* SubViewer	https://github.com/AlexPoint/SubtitlesParser/blob/master/SubtitlesParser/Classes/Parsers/SubViewerParser.cs
* TTML	https://github.com/AlexPoint/SubtitlesParser/blob/master/SubtitlesParser/Classes/Parsers/TtmlParser.cs
* WebVTT	https://github.com/AlexPoint/SubtitlesParser/blob/master/SubtitlesParser/Classes/Parsers/VttParser.cs
* Youtube specific XML format	https://github.com/AlexPoint/SubtitlesParser/blob/master/SubtitlesParser/Classes/Parsers/YtXmlFormatParser.cs

### Quickstart
#### Universal parser

If you don't specify the subtitle format, the SubtitlesParser will try all the registered parsers with the default configuration

```csharp
var parser = new SubtitlesParser.Classes.Parsers.SubParser();
using (var fileStream = File.OpenRead(pathToSrtFile)){
	var items = parser.ParseStream(fileStream);
}
```

#### Specific parser

You can use a specific parser if you know the format of the files you parse.
For example, for parsing an srt file:

```csharp
var parser = new SubtitlesParser.Classes.Parsers.SrtParser();
using (var fileStream = File.OpenRead(pathToSrtFile)){
	var items = parser.ParseStream(fileStream);
}
```
## Licenses / Acknowledgements
**Current code** is licensed under the **GNU Lesser General Public License v3.0** (LGPLv3).
> [!TIP]
> While this is not legal advice, a common misconception is that the [LGPL license](https://choosealicense.com/licenses/lgpl-3.0/), due to having "GPL" in its name, requires you to license your program under the GPL or the same license. This is not necessarily true. Since this project is a library,
> the requirements depend on how you use the library and the compatibility between this project's license and your project's license. However, please note that this tip does not override the specific requirements of the LGPL license. There may be
> additional obligations based on your use case. For further clarification, you can refer to the [LGPL FAQ about static vs dynamic linking requirements](https://www.gnu.org/licenses/gpl-faq.html#LGPLStaticVsDynamic) and this [Reddit post](https://www.reddit.com/r/rust/comments/fevz37/comment/fjsg393/).

⚠️ **Original version** [available here](https://github.com/AlexPoint/SubtitlesParser) is licensed under the original project made by AlexPoint, license/credits:
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
