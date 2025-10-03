using System.IO;

namespace SubtitlesParserV2.Tests
{
    public static class TestFilesManager
    {
        public static readonly string[] TestFilesPath;

        static TestFilesManager()
        {
			// Get test files from the TestLibrary project
            // from debug/release folder to project root > test library
			TestFilesPath = GetFiles(Path.GetFullPath(string.Join(Path.DirectorySeparatorChar,"..","..", "..","..", "TestLibrary", "Content", "TestFiles")));
        }

        private static string[] GetFiles(string relativePath) => Directory.GetFiles(Path.GetFullPath(relativePath));

    }
}
