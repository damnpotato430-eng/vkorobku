using vKOROBKU.App.Models;
using vKOROBKU.App.Services;

namespace vKOROBKU.Tests;

public sealed class SamplePlannerTests
{
    private const long MiB = 1024L * 1024;

    [Fact]
    public void CreatePlan_EmptyInventory_ReturnsEmptyPlan()
    {
        var result = new SamplePlanner().CreatePlan([], 512 * MiB);

        Assert.Empty(result);
    }

    [Fact]
    public void CreatePlan_SmallGame_CoversWholeGameUpToSmallGameSample()
    {
        var inventory = Enumerable.Range(0, 4)
            .Select(index => File($"C:\\Game\\small-{index}.bin", 8 * MiB))
            .ToArray();

        var result = new SamplePlanner().CreatePlan(inventory, 512 * MiB);

        Assert.Equal(32 * MiB, result.Sum(fragment => (long)fragment.Length));
        AssertFragmentsAreValidAndUnique(result, inventory);
    }

    [Fact]
    public void CreatePlan_LargeGame_DoesNotExceedMaximumSampleBytes()
    {
        var inventory = new[] { File("D:\\Game\\large.pak", 20L * 1024 * MiB) };
        var maximum = 512 * MiB;

        var result = new SamplePlanner().CreatePlan(inventory, maximum);

        Assert.Equal(maximum, result.Sum(fragment => (long)fragment.Length));
        AssertFragmentsAreValidAndUnique(result, inventory);
    }

    [Fact]
    public void CreatePlan_FragmentsAreUniqueAndStayInsideSourceFiles()
    {
        var inventory = new[]
        {
            File("E:\\Game\\first.pak", 700 * MiB),
            File("E:\\Game\\second.pak", 300 * MiB),
            File("E:\\Game\\third.bin", 100 * MiB)
        };

        var result = new SamplePlanner().CreatePlan(inventory, 256 * MiB);

        AssertFragmentsAreValidAndUnique(result, inventory);
    }

    [Fact]
    public void CreatePlan_FillsShortfallWithSmallFiles()
    {
        var inventory = Enumerable.Range(0, 100)
            .Select(index => File($"F:\\Game\\chunk-{index:D3}.bin", MiB))
            .ToArray();

        var result = new SamplePlanner().CreatePlan(inventory, 512 * MiB);

        Assert.Equal(64 * MiB, result.Sum(fragment => (long)fragment.Length));
        Assert.True(result.Select(fragment => fragment.SourcePath).Distinct(StringComparer.OrdinalIgnoreCase).Count() > 8);
        AssertFragmentsAreValidAndUnique(result, inventory);
    }

    private static FileInventoryEntry File(string path, long length) =>
        new(path, length, length, true);

    private static void AssertFragmentsAreValidAndUnique(
        IReadOnlyList<SampleFragment> fragments,
        IReadOnlyList<FileInventoryEntry> inventory)
    {
        var lengths = inventory.ToDictionary(file => file.FullPath, file => file.LogicalBytes, StringComparer.OrdinalIgnoreCase);
        Assert.Equal(
            fragments.Count,
            fragments.Select(fragment => (fragment.SourcePath.ToUpperInvariant(), fragment.Offset)).Distinct().Count());
        Assert.All(fragments, fragment =>
        {
            Assert.True(fragment.Offset >= 0);
            Assert.True(fragment.Length > 0);
            Assert.True(fragment.Offset + fragment.Length <= lengths[fragment.SourcePath]);
        });
    }
}
