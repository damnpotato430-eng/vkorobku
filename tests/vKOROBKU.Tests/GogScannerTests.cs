using vKOROBKU.App.Services;

namespace vKOROBKU.Tests;

public sealed class GogScannerTests
{
    [Fact]
    public void CreateEntry_BaseGame_ReturnsEntryWithBuildId()
    {
        var entry = GogScanner.CreateEntry(
            "1209105161", "REANIMAL", @"G:\Games\REANIMAL",
            dependsOn: "", buildId: "59425236810917549", version: "1.0b");

        Assert.NotNull(entry);
        Assert.Equal("1209105161", entry.ProductId);
        Assert.Equal("REANIMAL", entry.Name);
        Assert.Equal(@"G:\Games\REANIMAL", entry.InstallPath);
        Assert.Equal("59425236810917549", entry.BuildId);
    }

    [Fact]
    public void CreateEntry_DlcWithDependsOn_ReturnsNull()
    {
        var entry = GogScanner.CreateEntry(
            "2106070936", "REANIMAL - Foxhead Masks", @"G:\Games\REANIMAL",
            dependsOn: "1209105161", buildId: "59425236810917549", version: "1.0b");

        Assert.Null(entry);
    }

    [Fact]
    public void CreateEntry_MissingNameOrPath_ReturnsNull()
    {
        Assert.Null(GogScanner.CreateEntry("1", null, @"G:\Games\X", "", "2", "3"));
        Assert.Null(GogScanner.CreateEntry("1", "Game", null, "", "2", "3"));
        Assert.Null(GogScanner.CreateEntry("1", " ", @"G:\Games\X", "", "2", "3"));
    }

    [Fact]
    public void CreateEntry_WithoutBuildId_FallsBackToVersion()
    {
        var entry = GogScanner.CreateEntry(
            "1436434037", "Metro 2033 Redux", @"E:\Games\Metro 2033 Redux",
            dependsOn: null, buildId: null, version: "1.03");

        Assert.NotNull(entry);
        Assert.Equal("1.03", entry.BuildId);
    }

    [Fact]
    public void CreateEntry_WithoutBuildIdAndVersion_LeavesBuildIdNull()
    {
        var entry = GogScanner.CreateEntry(
            "1", "Game", @"G:\Games\X", dependsOn: "", buildId: " ", version: null);

        Assert.NotNull(entry);
        Assert.Null(entry.BuildId);
    }
}
