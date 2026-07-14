namespace vKOROBKU.App.Services;

/// <summary>
/// Known-incompressible extensions skipped during compression and estimation.
/// The default list lives in code so application updates can evolve it without
/// touching user additions stored in preferences.
/// </summary>
public static class CompressionSkipList
{
    // .pak deliberately excluded: engine containers often compress well.
    public static readonly IReadOnlyList<string> DefaultExtensions =
    [
        ".dl_", ".gif", ".jpg", ".jpeg", ".png", ".webp", ".wmf",
        ".mkv", ".mp4", ".wmv", ".avi", ".bik", ".bk2", ".flv", ".mpg", ".m2v", ".m4v", ".vob", ".webm",
        ".ogg", ".mp3", ".aac", ".wma", ".flac", ".opus", ".m4a",
        ".zip", ".xap", ".rar", ".7z", ".cab", ".lzx", ".tar", ".gz", ".bz2", ".tgz", ".lz", ".xz", ".txz",
        ".docx", ".xlsx", ".pptx"
    ];

    public static string[]? BuildEffectiveExtensions(UserPreferences preferences) =>
        preferences.SkipNonCompressable
            ? DefaultExtensions
                .Concat(preferences.UserSkipExtensions ?? [])
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .ToArray()
            : null;

    public static HashSet<string>? BuildEffectiveSet(UserPreferences preferences)
    {
        var extensions = BuildEffectiveExtensions(preferences);
        return extensions is null ? null : new HashSet<string>(extensions, StringComparer.OrdinalIgnoreCase);
    }

    public static bool TryNormalizeExtension(string? input, out string normalized)
    {
        normalized = string.Empty;
        var candidate = input?.Trim().ToLowerInvariant() ?? string.Empty;
        if (candidate.Length == 0)
            return false;
        if (!candidate.StartsWith('.'))
            candidate = "." + candidate;
        if (candidate.Length < 2 || candidate.Length > 16 ||
            candidate.LastIndexOf('.') != 0 ||
            candidate.IndexOfAny([' ', '\t', '\\', '/', '*', '?', '"', '<', '>', '|', ':']) >= 0)
            return false;

        normalized = candidate;
        return true;
    }
}
