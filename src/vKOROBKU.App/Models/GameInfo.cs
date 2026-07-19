using System.ComponentModel;
using System.Runtime.CompilerServices;
using vKOROBKU.App.Resources;

namespace vKOROBKU.App.Models;

public enum GameCompressionState
{
    Unknown,
    Uncompressed,
    PartiallyCompressed,
    Compressed
}

public sealed class GameInfo : INotifyPropertyChanged
{
    private string _name;
    private long _logicalSizeBytes;
    private string? _steamAppId;
    private string? _coverPath;
    private GameCompressionState _compressionState;
    private string? _compressionAlgorithm;
    private long _compressionSavedBytes;
    private long _compressedPhysicalBytes;
    private int _compressedFileCount;
    private DateTimeOffset? _compressionCheckedAt;
    private bool _isAnalysisStale;
    private bool? _hasDirectStorage;
    private bool _isQueueSelected;

    public GameInfo(
        string name,
        string installPath,
        long logicalSizeBytes,
        string source,
        string? steamAppId = null,
        string? steamBuildId = null,
        string? coverPath = null,
        GameCompressionState compressionState = GameCompressionState.Unknown,
        string? compressionAlgorithm = null,
        string? gogProductId = null)
    {
        _name = name;
        InstallPath = installPath;
        LogicalSizeBytes = logicalSizeBytes;
        Source = source;
        _steamAppId = steamAppId;
        SteamBuildId = steamBuildId;
        _coverPath = coverPath;
        _compressionState = compressionState;
        _compressionAlgorithm = compressionAlgorithm;
        GogProductId = gogProductId;
    }

    public event PropertyChangedEventHandler? PropertyChanged;

    public string Name
    {
        get => _name;
        set
        {
            if (SetProperty(ref _name, value))
                OnPropertyChanged(nameof(NeedsIdentityReview));
        }
    }

    public string InstallPath { get; }

    // Steam manifests report the size of a single app, while shared install folders can
    // hold several apps, so the value is refreshed from real directory walks.
    public long LogicalSizeBytes
    {
        get => _logicalSizeBytes;
        set
        {
            if (SetProperty(ref _logicalSizeBytes, value))
            {
                OnPropertyChanged(nameof(SizeText));
                OnPropertyChanged(nameof(OriginalSizeBracketText));
                OnPropertyChanged(nameof(HasCompressedSize));
            }
        }
    }

    public string Source { get; }
    public string? SteamAppId
    {
        get => _steamAppId;
        set
        {
            if (SetProperty(ref _steamAppId, value))
                OnPropertyChanged(nameof(NeedsIdentityReview));
        }
    }

    public string? SteamBuildId { get; }

    // GOG product id from the installer registry entry; drives the native GOG cover API.
    public string? GogProductId { get; }

    // The invariant marker of manually added games — never persisted, but compared all
    // over the app, so it must not depend on the UI language.
    public const string ManualSource = "Manual";

    public bool NeedsIdentityReview => Source == ManualSource && string.IsNullOrWhiteSpace(SteamAppId);

    /// <summary>Launcher names are shown as-is; only the manual marker is localized.</summary>
    public string SourceText => Source == ManualSource ? Strings.Source_Manual : Source;

    public string? CoverPath
    {
        get => _coverPath;
        set => SetProperty(ref _coverPath, value);
    }

    public GameCompressionState CompressionState
    {
        get => _compressionState;
        set
        {
            if (SetProperty(ref _compressionState, value))
            {
                OnPropertyChanged(nameof(CompressionStatusText));
                OnPropertyChanged(nameof(CompressionInfoText));
                OnPropertyChanged(nameof(HasCompressedSize));
            }
        }
    }

    public string? CompressionAlgorithm
    {
        get => _compressionAlgorithm;
        set
        {
            if (SetProperty(ref _compressionAlgorithm, value))
            {
                OnPropertyChanged(nameof(CompressionStatusText));
                OnPropertyChanged(nameof(CompressionInfoText));
            }
        }
    }

    public string SizeText => LogicalSizeBytes <= 0 ? Strings.Card_SizeUnknown : ByteFormatter.Format(LogicalSizeBytes);

    // Card size for compressed games: the actual on-disk weight in accent color with
    // the original size in brackets; uncompressed cards keep the plain original size.
    public bool HasCompressedSize =>
        CompressionState is GameCompressionState.Compressed or GameCompressionState.PartiallyCompressed &&
        CompressedPhysicalBytes > 0 && LogicalSizeBytes > 0;

    public string ActualSizeText => ByteFormatter.Format(CompressedPhysicalBytes);
    public string OriginalSizeBracketText => $"({ByteFormatter.Format(LogicalSizeBytes)})";

    public long CompressionSavedBytes
    {
        get => _compressionSavedBytes;
        set
        {
            if (SetProperty(ref _compressionSavedBytes, value))
                OnPropertyChanged(nameof(CompressionInfoText));
        }
    }

    public long CompressedPhysicalBytes
    {
        get => _compressedPhysicalBytes;
        set
        {
            if (SetProperty(ref _compressedPhysicalBytes, value))
            {
                OnPropertyChanged(nameof(CompressionInfoText));
                OnPropertyChanged(nameof(ActualSizeText));
                OnPropertyChanged(nameof(HasCompressedSize));
            }
        }
    }

    public int CompressedFileCount
    {
        get => _compressedFileCount;
        set
        {
            if (SetProperty(ref _compressedFileCount, value))
                OnPropertyChanged(nameof(CompressionInfoText));
        }
    }

    public DateTimeOffset? CompressionCheckedAt
    {
        get => _compressionCheckedAt;
        set
        {
            if (SetProperty(ref _compressionCheckedAt, value))
                OnPropertyChanged(nameof(CompressionInfoText));
        }
    }

    public bool IsAnalysisStale
    {
        get => _isAnalysisStale;
        set => SetProperty(ref _isAnalysisStale, value);
    }

    // Transient checkbox state of the library's multi-select mode — never persisted.
    public bool IsQueueSelected
    {
        get => _isQueueSelected;
        set => SetProperty(ref _isQueueSelected, value);
    }

    // null — not probed yet; probing happens with the compression status walk.
    public bool? HasDirectStorage
    {
        get => _hasDirectStorage;
        set => SetProperty(ref _hasDirectStorage, value);
    }

    public string CompressionStatusText => CompressionState switch
    {
        GameCompressionState.Compressed => string.Format(Strings.Card_StateCompressed, CompressionAlgorithm ?? "Windows"),
        GameCompressionState.PartiallyCompressed => string.Format(Strings.Card_StatePartial, CompressionAlgorithm ?? "Windows"),
        GameCompressionState.Uncompressed => Strings.Card_StateUncompressed,
        _ => Strings.Card_StateUnknown
    };

    public string CompressionInfoText =>
        string.Format(Strings.Card_InfoAlgorithm, CompressionAlgorithm ?? "Windows") + "\n" +
        string.Format(Strings.Card_InfoPhysical,
            CompressedPhysicalBytes > 0 ? ByteFormatter.Format(CompressedPhysicalBytes) : Strings.Card_InfoPhysicalPending) + "\n" +
        string.Format(Strings.Card_InfoFreed,
            CompressionSavedBytes > 0 ? ByteFormatter.Format(CompressionSavedBytes) : Strings.Card_InfoFreedUnknown) + "\n" +
        string.Format(Strings.Card_InfoFiles, $"{CompressedFileCount:N0}") +
        (CompressionCheckedAt is null
            ? string.Empty
            : "\n" + string.Format(Strings.Card_InfoChecked, $"{CompressionCheckedAt:dd.MM.yyyy HH:mm}"));

    private bool SetProperty<T>(ref T field, T value, [CallerMemberName] string? propertyName = null)
    {
        if (EqualityComparer<T>.Default.Equals(field, value))
            return false;
        field = value;
        OnPropertyChanged(propertyName);
        return true;
    }

    private void OnPropertyChanged([CallerMemberName] string? propertyName = null) =>
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
}
