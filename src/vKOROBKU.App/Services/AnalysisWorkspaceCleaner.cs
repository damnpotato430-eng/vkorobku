using System.Diagnostics;

namespace vKOROBKU.App.Services;

public sealed class AnalysisWorkspaceCleaner
{
    private static readonly TimeSpan MinimumAge = TimeSpan.FromHours(1);
    internal const string ActiveMarkerName = ".analysis-active";

    public void CleanupOldWorkspaces()
    {
        foreach (var root in FindWorkspaceRoots())
            CleanupRoot(root);
    }

    private static IReadOnlyCollection<string> FindWorkspaceRoots()
    {
        var roots = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
        {
            Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                "vKOROBKU", "Analysis")
        };

        foreach (var drive in DriveInfo.GetDrives())
        {
            try
            {
                if (drive.IsReady && drive.DriveType == DriveType.Fixed)
                    roots.Add(Path.Combine(drive.RootDirectory.FullName, "vKOROBKU-Analysis"));
            }
            catch (IOException) { }
            catch (UnauthorizedAccessException) { }
        }
        return roots;
    }

    private static void CleanupRoot(string root)
    {
        try
        {
            if (!Directory.Exists(root))
                return;
            foreach (var workspace in Directory.EnumerateDirectories(root))
            {
                try
                {
                    if (!Guid.TryParseExact(Path.GetFileName(workspace), "N", out _) ||
                        Directory.GetLastWriteTimeUtc(workspace) > DateTime.UtcNow - MinimumAge ||
                        IsActive(workspace))
                        continue;
                    Directory.Delete(workspace, true);
                }
                catch (IOException) { }
                catch (UnauthorizedAccessException) { }
            }
        }
        catch (IOException) { }
        catch (UnauthorizedAccessException) { }
    }

    private static bool IsActive(string workspace)
    {
        try
        {
            var marker = Path.Combine(workspace, ActiveMarkerName);
            if (!File.Exists(marker) || !int.TryParse(File.ReadAllText(marker).Trim(), out var processId))
                return false;
            using var process = Process.GetProcessById(processId);
            return !process.HasExited;
        }
        catch (ArgumentException) { return false; }
        catch (InvalidOperationException) { return false; }
        catch (IOException) { return false; }
        catch (UnauthorizedAccessException) { return true; }
    }
}
