using vKOROBKU.App.Services;

namespace vKOROBKU.Tests;

public sealed class GamePathTests
{
    [Theory]
    [InlineData(@"C:\Games\Demo", @"C:\Games\Demo")]
    [InlineData(@"C:\Games\Demo\", @"C:\Games\Demo")]
    [InlineData(@"C:\Games\Demo\\", @"C:\Games\Demo")]
    [InlineData("C:/Games/Demo/", @"C:\Games\Demo")]
    [InlineData(@"C:\Games\Sub\..\Demo", @"C:\Games\Demo")]
    public void Normalize_ProducesCanonicalPath(string input, string expected)
    {
        Assert.Equal(expected, GamePath.Normalize(input));
    }

    [Fact]
    public void Normalize_ManualAndScannerFormsMatch()
    {
        // A manually added path (with trailing separator) and a scanner path (without)
        // for the same folder must compare equal after normalization.
        var manual = GamePath.Normalize(@"G:\Games\TheSilentAge\");
        var scanner = GamePath.Normalize(@"G:\Games\TheSilentAge");

        Assert.Equal(scanner, manual);
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    public void Normalize_BlankInput_ReturnedAsIs(string input)
    {
        Assert.Equal(input, GamePath.Normalize(input));
    }
}
