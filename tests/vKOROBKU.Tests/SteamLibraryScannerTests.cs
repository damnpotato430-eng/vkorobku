using vKOROBKU.App.Services;

namespace vKOROBKU.Tests;

public sealed class SteamLibraryScannerTests
{
    [Fact]
    public void ReadValue_ParsesAppManifestFieldsCaseInsensitively()
    {
        const string manifest = """
            "AppState"
            {
                "appid"       "292030"
                "name"        "The Witcher 3: Wild Hunt"
                "installdir"  "The Witcher 3"
                "buildid"     "12345678"
                "SizeOnDisk"  "61234567890"
            }
            """;

        Assert.Equal("292030", SteamLibraryScanner.ReadValue(manifest, "appid"));
        Assert.Equal("The Witcher 3: Wild Hunt", SteamLibraryScanner.ReadValue(manifest, "NAME"));
        Assert.Equal("The Witcher 3", SteamLibraryScanner.ReadValue(manifest, "installdir"));
        Assert.Equal("12345678", SteamLibraryScanner.ReadValue(manifest, "buildid"));
        Assert.Equal("61234567890", SteamLibraryScanner.ReadValue(manifest, "SizeOnDisk"));
        Assert.Null(SteamLibraryScanner.ReadValue(manifest, "missing"));
    }

    [Fact]
    public void ParseLibraryPaths_UnescapesBackslashesFromVdf()
    {
        const string libraryFolders = """
            "libraryfolders"
            {
                "0"
                {
                    "path"  "C:\\Program Files (x86)\\Steam"
                }
                "1"
                {
                    "path"  "D:\\SteamLibrary"
                }
            }
            """;

        var paths = SteamLibraryScanner.ParseLibraryPaths(libraryFolders);

        Assert.Equal(new[] { @"C:\Program Files (x86)\Steam", @"D:\SteamLibrary" }, paths);
    }
}
