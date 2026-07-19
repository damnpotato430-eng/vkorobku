using System.ComponentModel;
using System.Runtime.CompilerServices;
using vKOROBKU.App.Resources;

namespace vKOROBKU.App.Models;

public enum QueueItemStatus
{
    Pending,
    Running,
    Completed,
    Cancelled,
    Failed,
    Skipped
}

/// <summary>One game inside a running compression queue — pure UI state, never persisted.</summary>
public sealed class CompressionQueueItem(GameInfo game, string algorithm) : INotifyPropertyChanged
{
    private QueueItemStatus _status = QueueItemStatus.Pending;
    private string _statusText = Strings.QueueItem_Pending;

    public event PropertyChangedEventHandler? PropertyChanged;

    public GameInfo Game { get; } = game;
    public string Algorithm { get; } = algorithm;
    public string Title => $"{Game.Name} · {Algorithm}";

    public QueueItemStatus Status
    {
        get => _status;
        private set
        {
            if (_status == value)
                return;
            _status = value;
            OnPropertyChanged();
        }
    }

    public string StatusText
    {
        get => _statusText;
        private set
        {
            if (_statusText == value)
                return;
            _statusText = value;
            OnPropertyChanged();
        }
    }

    public long FreedBytes { get; private set; }

    public void MarkRunning() => Set(QueueItemStatus.Running, Strings.QueueItem_Running);

    public void MarkCompleted(long freedBytes, string statusText)
    {
        FreedBytes = freedBytes;
        Set(QueueItemStatus.Completed, statusText);
    }

    public void MarkCancelled() => Set(QueueItemStatus.Cancelled, Strings.QueueItem_Cancelled);
    public void MarkFailed(string reason) => Set(QueueItemStatus.Failed, reason);
    public void MarkSkipped(string reason) => Set(QueueItemStatus.Skipped, reason);

    private void Set(QueueItemStatus status, string statusText)
    {
        Status = status;
        StatusText = statusText;
    }

    private void OnPropertyChanged([CallerMemberName] string? propertyName = null) =>
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
}
