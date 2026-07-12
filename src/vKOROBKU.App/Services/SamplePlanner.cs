using vKOROBKU.App.Models;

namespace vKOROBKU.App.Services;

public sealed class SamplePlanner
{
    private const long MiB = 1024L * 1024;
    private const int FragmentSize = 8 * 1024 * 1024;
    private const long SmallGameThreshold = 1024L * MiB;
    private const long SmallGameSample = 64L * MiB;

    public IReadOnlyList<SampleFragment> CreatePlan(
        IReadOnlyList<FileInventoryEntry> inventory,
        long maximumSampleBytes)
    {
        var eligible = inventory.Where(file => file.CanSample).OrderBy(file => file.FullPath, StringComparer.OrdinalIgnoreCase).ToArray();
        var total = eligible.Sum(file => file.LogicalBytes);
        if (total == 0)
            return [];

        if (maximumSampleBytes <= 0)
            throw new ArgumentOutOfRangeException(nameof(maximumSampleBytes));

        // Five percent gives proportional coverage, while tiny installations need
        // a practical minimum to avoid sampling only one file.
        var proportionalTarget = total <= SmallGameThreshold
            ? Math.Min(total, SmallGameSample)
            : total / 20;
        var target = Math.Min(total, Math.Min(maximumSampleBytes, proportionalTarget));
        var desiredCount = Math.Max(1, (int)Math.Ceiling(target / (double)FragmentSize));
        var cumulative = new long[eligible.Length];
        long running = 0;
        for (var i = 0; i < eligible.Length; i++)
        {
            running += eligible[i].LogicalBytes;
            cumulative[i] = running;
        }

        var result = new List<SampleFragment>(desiredCount);
        var usedFragments = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var representedFiles = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        long sampledBytes = 0;

        for (var index = 0; index < desiredCount && sampledBytes < target; index++)
        {
            var bytePosition = (long)((index + 0.5) * total / desiredCount);
            var fileIndex = Array.BinarySearch(cumulative, bytePosition);
            if (fileIndex < 0)
                fileIndex = ~fileIndex;
            fileIndex = Math.Min(fileIndex, eligible.Length - 1);

            var file = eligible[fileIndex];
            var previousEnd = fileIndex == 0 ? 0 : cumulative[fileIndex - 1];
            var positionInFile = Math.Max(0, bytePosition - previousEnd);
            var length = (int)Math.Min(Math.Min(FragmentSize, file.LogicalBytes), target - sampledBytes);
            var offset = Math.Clamp(positionInFile - length / 2L, 0, file.LogicalBytes - length);
            var key = $"{file.FullPath}\0{offset}";
            if (!usedFragments.Add(key))
                continue;

            result.Add(new SampleFragment(file.FullPath, offset, length, Path.GetExtension(file.FullPath)));
            representedFiles.Add(file.FullPath);
            sampledBytes += length;
        }

        // Systematic byte positions can land in a few large files. Fill a shortfall
        // with other files so small-file games still produce a useful sample.
        foreach (var file in eligible)
        {
            if (sampledBytes >= target)
                break;
            if (!representedFiles.Add(file.FullPath))
                continue;

            var length = (int)Math.Min(Math.Min(FragmentSize, file.LogicalBytes), target - sampledBytes);
            result.Add(new SampleFragment(file.FullPath, 0, length, Path.GetExtension(file.FullPath)));
            sampledBytes += length;
        }

        return result;
    }
}
