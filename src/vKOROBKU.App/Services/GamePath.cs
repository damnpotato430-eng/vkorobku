namespace vKOROBKU.App.Services;

/// <summary>Canonicalizes install paths so the same folder always compares equal,
/// regardless of trailing separators or slash direction. Install paths are used as
/// keys across scanners, the compression/analysis stores and the watcher, so a single
/// canonical form prevents the same game appearing twice or losing its saved state.</summary>
public static class GamePath
{
    public static string Normalize(string path)
    {
        if (string.IsNullOrWhiteSpace(path))
            return path;
        try
        {
            return Path.TrimEndingDirectorySeparator(Path.GetFullPath(path));
        }
        catch (ArgumentException) { return path.TrimEnd('\\', '/'); }
        catch (NotSupportedException) { return path.TrimEnd('\\', '/'); }
        catch (PathTooLongException) { return path.TrimEnd('\\', '/'); }
    }
}
