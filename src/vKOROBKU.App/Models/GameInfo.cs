using System.ComponentModel;
using System.Runtime.CompilerServices;

namespace vKOROBKU.App.Models;

public enum GameCompressionState
{
    Unknown,
    Uncompressed,
    Compressed
}

public sealed class GameInfo : INotifyPropertyChanged
{
    private string? _coverPath;
    private GameCompressionState _compressionState;
    private string? _compressionAlgorithm;
    private long _compressionSavedBytes;
    private long _compressedPhysicalBytes;
    private int _compressedFileCount;
    private DateTimeOffset? _compressionCheckedAt;

    public GameInfo(
        string name,
        string installPath,
        long logicalSizeBytes,
        string source,
        string? steamAppId = null,
        string? coverPath = null,
        GameCompressionState compressionState = GameCompressionState.Unknown,
        string? compressionAlgorithm = null)
    {
        Name = name;
        InstallPath = installPath;
        LogicalSizeBytes = logicalSizeBytes;
        Source = source;
        SteamAppId = steamAppId;
        _coverPath = coverPath;
        _compressionState = compressionState;
        _compressionAlgorithm = compressionAlgorithm;
    }

    public event PropertyChangedEventHandler? PropertyChanged;

    public string Name { get; }
    public string InstallPath { get; }
    public long LogicalSizeBytes { get; }
    public string Source { get; }
    public string? SteamAppId { get; }

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

    public string SizeText => LogicalSizeBytes <= 0 ? "Размер не рассчитан" : FormatBytes(LogicalSizeBytes);

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
                OnPropertyChanged(nameof(CompressionInfoText));
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

    public string CompressionStatusText => CompressionState switch
    {
        GameCompressionState.Compressed => $"Сжата · {CompressionAlgorithm ?? "Windows"}",
        GameCompressionState.Uncompressed => "Не сжата",
        _ => "Статус не проверен"
    };

    public string CompressionInfoText =>
        $"Алгоритм: {CompressionAlgorithm ?? "Windows"}\n" +
        $"Физический размер: {(CompressedPhysicalBytes > 0 ? FormatBytes(CompressedPhysicalBytes) : "уточняется")}\n" +
        $"Освобождено: {(CompressionSavedBytes > 0 ? FormatBytes(CompressionSavedBytes) : "не определено")}\n" +
        $"Сжатых файлов: {CompressedFileCount:N0}" +
        (CompressionCheckedAt is null ? string.Empty : $"\nПроверено: {CompressionCheckedAt:dd.MM.yyyy HH:mm}");

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

    private static string FormatBytes(long bytes)
    {
        string[] units = ["Б", "КБ", "МБ", "ГБ", "ТБ"];
        var value = (double)bytes;
        var unit = 0;
        while (value >= 1024 && unit < units.Length - 1)
        {
            value /= 1024;
            unit++;
        }
        return $"{value:0.#} {units[unit]}";
    }
}
