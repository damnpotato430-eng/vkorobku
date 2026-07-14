using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Diagnostics;
using System.Net.Http;
using System.Windows;
using Microsoft.Win32;
using vKOROBKU.App.Models;
using vKOROBKU.App.Services;
using vKOROBKU.Protocol;

namespace vKOROBKU.App.ViewModels;

public sealed class MainViewModel : ObservableObject
{
    private const int CurrentIdentityVersion = 4;
    private readonly SteamLibraryScanner _steamScanner = new();
    private readonly ComputerInfoService _computerInfoService = new();
    private readonly FileTreeService _fileTreeService = new();
    private readonly GameAnalysisService _analysisService = new();
    private readonly AnalysisWorkspaceCleaner _analysisWorkspaceCleaner = new();
    private readonly IgdbCredentialStore _igdbCredentialStore = new();
    private readonly IgdbCoverService _coverService;
    private readonly CompressionWorkerClient _workerClient = new();
    private readonly AnalysisCacheStore _analysisCache = new();
    private readonly CompressionStatusStore _compressionStatusStore = new();
    private readonly GameCompressionDetector _compressionDetector = new();
    private readonly OperationJournalStore _operationJournal = new();
    private readonly UserPreferencesStore _preferences = new();
    private readonly ManualGameStore _manualGameStore = new();
    private readonly GameIdentityService _gameIdentityService = new();
    private ComputerInfo _computer = null!;
    private GameInfo? _selectedGame;
    private CompressionEstimate? _selectedEstimate;
    private OperationJournalEntry? _currentOperation;
    private AnalysisModeOption? _selectedAnalysisMode;
    private CancellationTokenSource? _analysisCancellation;
    private CancellationTokenSource? _compressionCheckCancellation;
    private string _statusText = "Готово к поиску игр";
    private string _scanButtonText = "Найти игры Steam";
    private string _analysisButtonText = "Рассчитать экономию";
    private string _analysisSummary = "Выберите игру и запустите безопасный анализ выборки.";
    private bool _isAnalyzing;
    private bool _isOperating;
    private bool _isCheckingCompression;
    private bool _isExpertMode;
    private double _operationProgress;
    private string _operationSummary = "Сжатие изменяет только способ хранения файлов на NTFS.";
    private string _totalSavingsText = string.Empty;
    private string? _activeOperationPath;
    private string _activeOperationDescription = string.Empty;
    private string? _activeCompressionAlgorithm;
    private string? _activeCompressionSavings;

    public MainViewModel()
    {
        _computer = _computerInfoService.GetComputerInfo();
        _isExpertMode = _preferences.LoadExpertMode();
        _coverService = new IgdbCoverService(_igdbCredentialStore);
        AnalysisModes.Add(new AnalysisModeOption("Авто", "512 МБ–2 ГБ по размеру игры", 0));
        AnalysisModes.Add(new AnalysisModeOption("Быстрый", "до 512 МБ", 512L * 1024 * 1024));
        AnalysisModes.Add(new AnalysisModeOption("Точный", "до 1 ГБ", 1024L * 1024 * 1024));
        AnalysisModes.Add(new AnalysisModeOption("Максимальный", "до 2 ГБ", 2L * 1024 * 1024 * 1024));
        SelectedAnalysisMode = AnalysisModes[2];
        ScanSteamCommand = new AsyncRelayCommand(RefreshSteamLibraryAsync);
        AddFolderCommand = new AsyncRelayCommand(AddFolderAsync);
        ConfigureIgdbCommand = new AsyncRelayCommand(ConfigureIgdbAsync);
        ShowOperationsCommand = new RelayCommand(ShowOperations);
        ReviewIdentityCommand = new AsyncRelayCommand(ReviewSelectedGameIdentityAsync,
            () => SelectedGame?.NeedsIdentityReview == true);
        RemoveGameCommand = new RelayCommand(RemoveSelectedGame,
            () => SelectedGame?.Source == "Добавлено вручную" && !IsAnalyzing && !IsOperating && !IsCheckingCompression);
        RecheckCompressionCommand = new AsyncRelayCommand(RecheckSelectedGameCompressionAsync,
            () => SelectedGame is not null && !IsAnalyzing && !IsOperating && !IsCheckingCompression);
        OpenGameFolderCommand = new RelayCommand(OpenSelectedGameFolder, () => SelectedGame is not null);
        RefreshCoversCommand = new AsyncRelayCommand(() => LoadCoversAsync(true), () => Games.Count > 0 && _coverService.HasCredentials);
        AnalyzeCommand = new AsyncRelayCommand(AnalyzeSelectedGameAsync,
            () => SelectedGame is { CompressionState: not GameCompressionState.Compressed } && !IsAnalyzing && !IsOperating && !IsCheckingCompression);
        OptimizeCommand = new AsyncRelayCommand(OptimizeSelectedGameAsync,
            () => SelectedGame is { CompressionState: not GameCompressionState.Compressed } && !IsAnalyzing && !IsOperating && !IsCheckingCompression);
        CancelAnalysisCommand = new RelayCommand(CancelAnalysis, () => IsAnalyzing);
        CompressCommand = new AsyncRelayCommand(CompressSelectedGameAsync,
            () => SelectedGame is { CompressionState: not GameCompressionState.Compressed, IsAnalysisStale: false } && SelectedEstimate is not null && !IsAnalyzing && !IsOperating && !IsCheckingCompression);
        DecompressCommand = new AsyncRelayCommand(DecompressSelectedGameAsync,
            () => SelectedGame is { CompressionState: GameCompressionState.Compressed or GameCompressionState.PartiallyCompressed } && !IsAnalyzing && !IsOperating && !IsCheckingCompression);
        CancelOperationCommand = new AsyncRelayCommand(_workerClient.CancelAsync, () => IsOperating);
        CancelCurrentCommand = new AsyncRelayCommand(CancelCurrentAsync, () => IsAnalyzing || IsOperating);
    }

    public ObservableCollection<GameInfo> Games { get; } = [];
    public ObservableCollection<CompressionEstimate> Estimates { get; } = [];
    public ObservableCollection<OperationJournalEntry> Operations { get; } = [];
    public ObservableCollection<AnalysisModeOption> AnalysisModes { get; } = [];
    public ComputerInfo Computer
    {
        get => _computer;
        private set => SetProperty(ref _computer, value);
    }
    public AsyncRelayCommand ScanSteamCommand { get; }
    public AsyncRelayCommand AddFolderCommand { get; }
    public AsyncRelayCommand ConfigureIgdbCommand { get; }
    public RelayCommand ShowOperationsCommand { get; }
    public AsyncRelayCommand ReviewIdentityCommand { get; }
    public RelayCommand RemoveGameCommand { get; }
    public AsyncRelayCommand RecheckCompressionCommand { get; }
    public RelayCommand OpenGameFolderCommand { get; }
    public AsyncRelayCommand RefreshCoversCommand { get; }
    public AsyncRelayCommand AnalyzeCommand { get; }
    public AsyncRelayCommand OptimizeCommand { get; }
    public RelayCommand CancelAnalysisCommand { get; }
    public AsyncRelayCommand CompressCommand { get; }
    public AsyncRelayCommand DecompressCommand { get; }
    public AsyncRelayCommand CancelOperationCommand { get; }
    public AsyncRelayCommand CancelCurrentCommand { get; }

    public OperationJournalEntry? CurrentOperation
    {
        get => _currentOperation;
        private set => SetProperty(ref _currentOperation, value);
    }

    public GameInfo? SelectedGame
    {
        get => _selectedGame;
        set
        {
            var sameGame = _selectedGame is not null && value is not null &&
                           string.Equals(_selectedGame.InstallPath, value.InstallPath, StringComparison.OrdinalIgnoreCase);
            if (!SetProperty(ref _selectedGame, value))
                return;
            NotifyCompressionPanelVisibility();
            NotifyActiveOperationLabel();
            OnPropertyChanged(nameof(IdentityReviewVisibility));
            OnPropertyChanged(nameof(ManualGameVisibility));
            ReviewIdentityCommand.RaiseCanExecuteChanged();
            RemoveGameCommand.RaiseCanExecuteChanged();
            if (sameGame)
                return;
            Estimates.Clear();
            SelectedEstimate = null;
            AnalysisButtonText = "Рассчитать экономию";
            AnalysisSummary = value is null
                ? "Выберите игру и запустите безопасный анализ выборки."
                : "Автоматический режим оценит игру и подберёт оптимальный способ экономии места.";
            if (value is not null)
                RestoreSavedAnalysis(value);
            AnalyzeCommand.RaiseCanExecuteChanged();
            RaiseActionCommands();
            _compressionCheckCancellation?.Cancel();
            _compressionCheckCancellation?.Dispose();
            _compressionCheckCancellation = null;
            if (value is not null)
            {
                if (HasFreshCompressionStatus(value))
                {
                    IsCheckingCompression = false;
                    StatusText = DescribeCompressionState(value);
                }
                else
                {
                    _compressionCheckCancellation = new CancellationTokenSource();
                    _ = RefreshCompressionStatusAsync(value, _compressionCheckCancellation.Token);
                }
            }
            else
            {
                IsCheckingCompression = false;
            }
        }
    }

    public CompressionEstimate? SelectedEstimate
    {
        get => _selectedEstimate;
        set
        {
            if (SetProperty(ref _selectedEstimate, value))
                CompressCommand.RaiseCanExecuteChanged();
        }
    }

    public AnalysisModeOption? SelectedAnalysisMode
    {
        get => _selectedAnalysisMode;
        set => SetProperty(ref _selectedAnalysisMode, value);
    }

    public string StatusText
    {
        get => _statusText;
        private set => SetProperty(ref _statusText, value);
    }

    public string ScanButtonText
    {
        get => _scanButtonText;
        private set => SetProperty(ref _scanButtonText, value);
    }

    public string AnalysisButtonText
    {
        get => _analysisButtonText;
        private set => SetProperty(ref _analysisButtonText, value);
    }

    public string AnalysisSummary
    {
        get => _analysisSummary;
        private set => SetProperty(ref _analysisSummary, value);
    }

    public bool IsAnalyzing
    {
        get => _isAnalyzing;
        private set
        {
            if (!SetProperty(ref _isAnalyzing, value))
                return;
            AnalyzeCommand.RaiseCanExecuteChanged();
            CancelAnalysisCommand.RaiseCanExecuteChanged();
            CompressCommand.RaiseCanExecuteChanged();
            DecompressCommand.RaiseCanExecuteChanged();
            OptimizeCommand.RaiseCanExecuteChanged();
            CancelCurrentCommand.RaiseCanExecuteChanged();
            RemoveGameCommand.RaiseCanExecuteChanged();
            RecheckCompressionCommand.RaiseCanExecuteChanged();
        }
    }

    public bool IsOperating
    {
        get => _isOperating;
        private set
        {
            if (!SetProperty(ref _isOperating, value))
                return;
            AnalyzeCommand.RaiseCanExecuteChanged();
            CompressCommand.RaiseCanExecuteChanged();
            DecompressCommand.RaiseCanExecuteChanged();
            CancelOperationCommand.RaiseCanExecuteChanged();
            OptimizeCommand.RaiseCanExecuteChanged();
            CancelCurrentCommand.RaiseCanExecuteChanged();
            RemoveGameCommand.RaiseCanExecuteChanged();
            RecheckCompressionCommand.RaiseCanExecuteChanged();
            NotifyCompressionPanelVisibility();
        }
    }

    public bool IsExpertMode
    {
        get => _isExpertMode;
        set
        {
            if (!SetProperty(ref _isExpertMode, value))
                return;
            try { _preferences.SaveExpertMode(value); } catch { }
            NotifyCompressionPanelVisibility();
        }
    }

    public Visibility IdentityReviewVisibility =>
        SelectedGame?.NeedsIdentityReview == true ? Visibility.Visible : Visibility.Collapsed;

    public Visibility ManualGameVisibility =>
        SelectedGame?.Source == "Добавлено вручную" ? Visibility.Visible : Visibility.Collapsed;

    public Visibility UncompressedPanelVisibility =>
        SelectedGame?.CompressionState == GameCompressionState.Compressed ? Visibility.Collapsed : Visibility.Visible;

    public Visibility AutoOptimizationVisibility =>
        SelectedGame?.CompressionState == GameCompressionState.Compressed || IsExpertMode
            ? Visibility.Collapsed
            : Visibility.Visible;

    public Visibility ExpertOptimizationVisibility =>
        SelectedGame?.CompressionState == GameCompressionState.Compressed || !IsExpertMode
            ? Visibility.Collapsed
            : Visibility.Visible;

    public Visibility CompressedPanelVisibility =>
        SelectedGame?.CompressionState == GameCompressionState.Compressed ? Visibility.Visible : Visibility.Collapsed;

    public Visibility PartiallyCompressedVisibility =>
        SelectedGame?.CompressionState == GameCompressionState.PartiallyCompressed ? Visibility.Visible : Visibility.Collapsed;

    public bool IsCheckingCompression
    {
        get => _isCheckingCompression;
        private set
        {
            if (SetProperty(ref _isCheckingCompression, value))
                RaiseActionCommands();
        }
    }

    public double OperationProgress
    {
        get => _operationProgress;
        private set
        {
            if (SetProperty(ref _operationProgress, value))
                OnPropertyChanged(nameof(OperationProgressText));
        }
    }

    public string OperationProgressText => $"{OperationProgress:0}%";

    public string OperationSummary
    {
        get => _operationSummary;
        private set => SetProperty(ref _operationSummary, value);
    }

    public string TotalSavingsText
    {
        get => _totalSavingsText;
        private set => SetProperty(ref _totalSavingsText, value);
    }

    // Shown above the progress bar only while the user is browsing a different card,
    // so the target of the running operation stays visible.
    public string ActiveOperationLabel =>
        _activeOperationPath is not null &&
        SelectedGame?.InstallPath.Equals(_activeOperationPath, StringComparison.OrdinalIgnoreCase) != true
            ? _activeOperationDescription
            : string.Empty;

    public Visibility ActiveOperationLabelVisibility =>
        ActiveOperationLabel.Length > 0 ? Visibility.Visible : Visibility.Collapsed;

    public string ActiveCompressionModeText => _activeCompressionAlgorithm ?? string.Empty;

    public string ActiveCompressionDetailsText =>
        _activeCompressionSavings is null ? string.Empty : $"Ожидаемая экономия: {_activeCompressionSavings}";

    // While a compression runs, the estimate list is replaced with a read-only card of
    // the chosen mode, so the selection cannot be toyed with mid-operation.
    public Visibility ActiveCompressionInfoVisibility =>
        IsOperating && _activeCompressionAlgorithm is not null &&
        SelectedGame?.CompressionState != GameCompressionState.Compressed
            ? Visibility.Visible
            : Visibility.Collapsed;

    public Visibility EstimateListVisibility =>
        !IsOperating && ExpertOptimizationVisibility == Visibility.Visible
            ? Visibility.Visible
            : Visibility.Collapsed;

    public Visibility AutoInfoVisibility =>
        !IsOperating && AutoOptimizationVisibility == Visibility.Visible
            ? Visibility.Visible
            : Visibility.Collapsed;

    public async Task InitializeAsync()
    {
        _ = Task.Run(() =>
        {
            try { _analysisWorkspaceCleaner.CleanupOldWorkspaces(); }
            catch (IOException) { }
            catch (UnauthorizedAccessException) { }
        });

        var interrupted = 0;
        try { interrupted = _operationJournal.MarkInterrupted(); } catch { }
        LoadOperationHistory();
        await RefreshSteamLibraryAsync();
        if (interrupted > 0)
            StatusText = "Предыдущая операция была прервана. Состояние игры будет проверено при выборе.";
        await OfferToResumeInterruptedCompressionAsync();
    }

    private async Task OfferToResumeInterruptedCompressionAsync()
    {
        var latest = Operations.FirstOrDefault();
        if (latest is not { State: OperationJournalState.Interrupted, Operation: "compress" } ||
            string.IsNullOrWhiteSpace(latest.Algorithm) || IsAnalyzing || IsOperating)
            return;

        var game = Games.FirstOrDefault(item =>
            string.Equals(item.InstallPath, latest.InstallPath, StringComparison.OrdinalIgnoreCase));
        if (game is null)
            return;

        var confirmation = MessageBox.Show(
            Application.Current.MainWindow,
            $"Сжатие игры «{game.Name}» ({latest.Algorithm}) было прервано.\n\n" +
            "Продолжить с места остановки? Уже сжатые файлы будут пропущены автоматически.",
            "Продолжить сжатие",
            MessageBoxButton.YesNo,
            MessageBoxImage.Question);
        if (confirmation != MessageBoxResult.Yes)
            return;

        SelectedGame = game;
        await ExecuteWorkerOperationAsync(new WorkerJob(game.InstallPath, "compress", latest.Algorithm));
    }

    private async Task RefreshSteamLibraryAsync()
    {
        await ScanSteamAsync();
        await LoadCoversAsync(false);
    }

    private async Task ScanSteamAsync()
    {
        ScanButtonText = "Поиск…";
        StatusText = "Сканируем библиотеки Steam";

        try
        {
            var foundGames = await _steamScanner.ScanAsync();
            var previousGames = Games.ToDictionary(
                game => game.InstallPath,
                StringComparer.OrdinalIgnoreCase);
            var selectedPath = SelectedGame?.InstallPath;

            Games.Clear();
            foreach (var foundGame in foundGames)
            {
                var game = ApplySavedCompressionStatus(foundGame);
                if (previousGames.TryGetValue(game.InstallPath, out var previous))
                    game.CoverPath = previous.CoverPath;
                Games.Add(game);
            }

            var savedManualGames = await Task.WhenAll(
                _manualGameStore.Load().Select(RefreshManualGameIdentityAsync));
            foreach (var savedManualGame in savedManualGames)
            {
                if (Games.Any(current => string.Equals(current.InstallPath, savedManualGame.InstallPath, StringComparison.OrdinalIgnoreCase)))
                    continue;
                Games.Add(ApplySavedCompressionStatus(new GameInfo(
                    savedManualGame.Name,
                    savedManualGame.InstallPath,
                    savedManualGame.LogicalSizeBytes,
                    "Добавлено вручную",
                    savedManualGame.SteamAppId)));
            }

            foreach (var manualGame in previousGames.Values.Where(game =>
                         !string.Equals(game.Source, "Steam", StringComparison.OrdinalIgnoreCase) &&
                         Games.All(current => !string.Equals(current.InstallPath, game.InstallPath, StringComparison.OrdinalIgnoreCase))))
                Games.Add(manualGame);

            if (selectedPath is not null)
                SelectedGame = Games.FirstOrDefault(game =>
                    string.Equals(game.InstallPath, selectedPath, StringComparison.OrdinalIgnoreCase));

            StatusText = foundGames.Count == 0
                ? "Игры Steam не найдены — добавьте папку вручную"
                : $"Найдено игр: {foundGames.Count}";
            RefreshCoversCommand.RaiseCanExecuteChanged();
            RefreshSavingsSummary();
        }
        catch (Exception exception)
        {
            StatusText = $"Не удалось просканировать Steam: {exception.Message}";
        }
        finally
        {
            ScanButtonText = "Обновить Steam";
        }
    }

    private async Task AddFolderAsync()
    {
        var dialog = new OpenFolderDialog
        {
            Title = "Выберите папку с игрой",
            Multiselect = false
        };

        if (dialog.ShowDialog(Application.Current.MainWindow) != true)
            return;

        var path = dialog.FolderName;
        StatusText = "Определяем игру и рассчитываем размер…";
        var sizeTask = Task.Run(() => _fileTreeService.CalculateLogicalSize(path));
        var identityTask = _gameIdentityService.DetectAsync(path);
        await Task.WhenAll(sizeTask, identityTask);
        var size = await sizeTask;
        var identity = await identityTask;
        var game = ApplySavedCompressionStatus(
            new GameInfo(identity.Name, path, size, "Добавлено вручную", identity.SteamAppId));

        var existing = Games.FirstOrDefault(item =>
            string.Equals(item.InstallPath, path, StringComparison.OrdinalIgnoreCase));
        if (existing is not null)
            Games.Remove(existing);

        Games.Add(game);
        try
        {
            _manualGameStore.Save(new ManualGameRecord(
                game.InstallPath, game.Name, game.SteamAppId, game.LogicalSizeBytes, DateTimeOffset.Now, CurrentIdentityVersion));
        }
        catch (IOException) { }
        catch (UnauthorizedAccessException) { }

        SelectedGame = game;
        StatusText = $"Добавлена игра «{game.Name}»";
        RefreshCoversCommand.RaiseCanExecuteChanged();
        RefreshSavingsSummary();
        _ = await LoadCoverAsync(game, false);
    }

    private void RemoveSelectedGame()
    {
        var game = SelectedGame;
        if (game?.Source != "Добавлено вручную" || IsAnalyzing || IsOperating || IsCheckingCompression)
            return;

        var message = $"Убрать игру «{game.Name}» из библиотеки?\n\n" +
                      "Игра будет убрана из библиотеки vKOROBKU. " +
                      "Файлы на диске не изменяются и не удаляются.";
        var isCompressed = game.CompressionState is GameCompressionState.Compressed or GameCompressionState.PartiallyCompressed;
        if (isCompressed)
        {
            message += "\n\nВнимание: игра останется сжатой. Чтобы вернуть файлы в исходное состояние, " +
                       "сначала выполните распаковку.";
        }

        var confirmation = MessageBox.Show(
            Application.Current.MainWindow,
            message,
            "Убрать из библиотеки",
            MessageBoxButton.YesNo,
            isCompressed ? MessageBoxImage.Warning : MessageBoxImage.Question);
        if (confirmation != MessageBoxResult.Yes)
            return;

        try
        {
            _manualGameStore.Remove(game.InstallPath);
        }
        catch (IOException exception)
        {
            ShowRemoveGameError(exception.Message);
            return;
        }
        catch (UnauthorizedAccessException exception)
        {
            ShowRemoveGameError(exception.Message);
            return;
        }

        try { _analysisCache.Remove(game.InstallPath); }
        catch (IOException) { }
        catch (UnauthorizedAccessException) { }
        try { _compressionStatusStore.Remove(game.InstallPath); }
        catch (IOException) { }
        catch (UnauthorizedAccessException) { }

        var gameName = game.Name;
        Games.Remove(game);
        SelectedGame = null;
        StatusText = $"Игра «{gameName}» убрана из библиотеки";
        RefreshCoversCommand.RaiseCanExecuteChanged();
        RaiseActionCommands();
        RefreshSavingsSummary();
    }

    private static void ShowRemoveGameError(string details) => MessageBox.Show(
        Application.Current.MainWindow,
        $"Не удалось убрать игру из библиотеки. Проверьте доступ к локальным данным vKOROBKU.\n\n{details}",
        "Ошибка удаления из библиотеки",
        MessageBoxButton.OK,
        MessageBoxImage.Error);

    private async Task<ManualGameRecord> RefreshManualGameIdentityAsync(ManualGameRecord record)
    {
        if (record.IdentityVersion >= CurrentIdentityVersion)
            return record;
        try
        {
            var identity = await _gameIdentityService.DetectAsync(record.InstallPath);
            var updated = record with
            {
                Name = identity.Name,
                SteamAppId = identity.SteamAppId,
                IdentityVersion = CurrentIdentityVersion
            };
            _manualGameStore.Save(updated);
            return updated;
        }
        catch
        {
            return record;
        }
    }

    private async Task ReviewSelectedGameIdentityAsync()
    {
        var game = SelectedGame;
        if (game is null || game.Source != "Добавлено вручную")
            return;

        var dialog = new ManualGameIdentityWindow(game.Name) { Owner = Application.Current.MainWindow };
        if (dialog.ShowDialog() != true)
            return;

        StatusText = "Ищем игру и подходящую обложку…";
        var identity = await _gameIdentityService.FindByNameAsync(dialog.GameName);
        game.Name = identity.Name;
        game.SteamAppId = identity.SteamAppId;
        game.CoverPath = null;
        try
        {
            _manualGameStore.Save(new ManualGameRecord(
                game.InstallPath, game.Name, game.SteamAppId,
                game.LogicalSizeBytes, DateTimeOffset.Now, CurrentIdentityVersion));
        }
        catch (IOException) { }
        catch (UnauthorizedAccessException) { }

        OnPropertyChanged(nameof(IdentityReviewVisibility));
        ReviewIdentityCommand.RaiseCanExecuteChanged();
        _ = await LoadCoverAsync(game, true);
        StatusText = identity.SteamAppId is null
            ? $"Название сохранено: «{game.Name}». Для обложки можно настроить IGDB."
            : $"Игра определена: «{game.Name}»";
    }

    private async Task ConfigureIgdbAsync()
    {
        var dialog = new IgdbSettingsWindow { Owner = Application.Current.MainWindow };
        if (dialog.ShowDialog() != true)
            return;

        RefreshCoversCommand.RaiseCanExecuteChanged();
        await LoadCoversAsync(true);
    }

    private void ShowOperations()
    {
        var existing = Application.Current.Windows.OfType<OperationsWindow>().FirstOrDefault();
        if (existing is not null)
        {
            existing.Activate();
            return;
        }

        var window = new OperationsWindow
        {
            Owner = Application.Current.MainWindow,
            DataContext = this
        };
        window.Show();
    }

    private void LoadOperationHistory()
    {
        try
        {
            Operations.Clear();
            foreach (var entry in _operationJournal.Load())
                Operations.Add(entry);
            CurrentOperation = Operations.FirstOrDefault(entry => entry.State == OperationJournalState.Running);
        }
        catch (IOException) { }
        catch (UnauthorizedAccessException) { }
    }

    private void UpsertOperation(OperationJournalEntry entry)
    {
        var index = -1;
        for (var current = 0; current < Operations.Count; current++)
        {
            if (Operations[current].Id == entry.Id)
            {
                index = current;
                break;
            }
        }

        if (index >= 0)
            Operations[index] = entry;
        else
            Operations.Insert(0, entry);

        if (entry.State == OperationJournalState.Running)
            CurrentOperation = entry;
        else if (CurrentOperation?.Id == entry.Id)
            CurrentOperation = null;
    }

    private async Task LoadCoversAsync(bool forceRefresh)
    {
        if (Games.Count == 0)
            return;

        const int maximumConcurrency = 4;
        var snapshot = Games.ToArray();
        using var semaphore = new SemaphoreSlim(maximumConcurrency, maximumConcurrency);
        using var cancellation = new CancellationTokenSource();
        var dispatcher = Application.Current.Dispatcher;
        var failureLock = new object();
        string? failureMessage = null;
        var completed = 0;

        void StopRemaining(string message)
        {
            lock (failureLock)
            {
                if (failureMessage is not null)
                    return;
                failureMessage = message;
                cancellation.Cancel();
            }
        }

        var tasks = snapshot.Select(async game =>
        {
            var entered = false;
            try
            {
                await semaphore.WaitAsync(cancellation.Token);
                entered = true;
                var coverPath = await _coverService.GetCoverAsync(game, forceRefresh, cancellation.Token);
                if (coverPath is not null)
                {
                    await dispatcher.InvokeAsync(() =>
                    {
                        var current = Games.FirstOrDefault(item =>
                            string.Equals(item.InstallPath, game.InstallPath, StringComparison.OrdinalIgnoreCase));
                        if (current is not null)
                            current.CoverPath = coverPath;
                    });
                }
            }
            catch (HttpRequestException exception)
            {
                StopRemaining(exception.StatusCode is System.Net.HttpStatusCode.Unauthorized or System.Net.HttpStatusCode.BadRequest
                    ? "IGDB отклонил ключи — проверьте Client ID и Client Secret"
                    : "Сервис обложек временно недоступен");
            }
            catch (TaskCanceledException) when (!cancellation.IsCancellationRequested)
            {
                StopRemaining("Сервис обложек не ответил вовремя");
            }
            catch (OperationCanceledException) when (cancellation.IsCancellationRequested) { }
            finally
            {
                if (entered)
                    semaphore.Release();
                var currentCompleted = Interlocked.Increment(ref completed);
                if (failureMessage is null)
                {
                    await dispatcher.InvokeAsync(() =>
                        StatusText = $"Загрузка обложек: {currentCompleted} из {snapshot.Length}");
                }
            }
        }).ToArray();

        await Task.WhenAll(tasks);
        if (failureMessage is not null)
        {
            StatusText = failureMessage;
            return;
        }

        StatusText = _coverService.HasCredentials
            ? $"Библиотека готова · проверено обложек: {completed}"
            : "Обложки Steam и локальный кэш загружены · IGDB доступен для остальных игр";
    }

    private async Task<bool> LoadCoverAsync(GameInfo game, bool forceRefresh)
    {
        try
        {
            var coverPath = await _coverService.GetCoverAsync(game, forceRefresh);
            if (coverPath is null)
                return true;

            var index = FindGameIndex(game.InstallPath);
            if (index < 0)
                return true;

            Games[index].CoverPath = coverPath;
            return true;
        }
        catch (HttpRequestException exception)
        {
            StatusText = exception.StatusCode is System.Net.HttpStatusCode.Unauthorized or System.Net.HttpStatusCode.BadRequest
                ? "IGDB отклонил ключи — проверьте Client ID и Client Secret"
                : $"IGDB недоступен: {exception.Message}";
            return false;
        }
        catch (TaskCanceledException)
        {
            StatusText = "IGDB не ответил вовремя";
            return false;
        }
    }

    private Task AnalyzeSelectedGameAsync() =>
        AnalyzeSelectedGameAsync(SelectedAnalysisMode?.MaximumSampleBytes ?? 0);

    private async Task AnalyzeSelectedGameAsync(long maximumSampleBytes)
    {
        var game = SelectedGame;
        if (game is null)
            return;

        Estimates.Clear();
        _analysisCancellation = new CancellationTokenSource();
        IsAnalyzing = true;
        SetActiveOperation($"Анализируется: {game.Name}", game.InstallPath);
        OperationProgress = 0;
        AnalysisButtonText = "Анализ выполняется…";
        AnalysisSummary = "Инвентаризация файлов…";
        OperationSummary = AnalysisSummary;

        Guid journalId;
        var startedAt = DateTimeOffset.Now;
        try { journalId = _operationJournal.Begin(game.InstallPath, "analysis", null, AnalysisSummary); }
        catch { journalId = Guid.NewGuid(); }
        var operation = new OperationJournalEntry(
            journalId, game.InstallPath, "analysis", null, startedAt, null,
            OperationJournalState.Running, 0, 0, 0, 0, 0, AnalysisSummary);
        UpsertOperation(operation);
        var lastPersistedPercent = -5;
        var acceptAnalysisProgress = true;

        var progress = new Progress<AnalysisProgressUpdate>(update =>
        {
            if (!acceptAnalysisProgress)
                return;
            OperationProgress = Math.Clamp(update.Percent, 0, 100);
            if (IsGameSelected(game))
                AnalysisSummary = update.Stage;
            OperationSummary = update.Stage;
            StatusText = update.Stage;
            operation = operation with
            {
                ProcessedBytes = update.ProcessedBytes,
                TotalBytes = update.TotalBytes,
                ProcessedFiles = (int)Math.Round(OperationProgress),
                TotalFiles = 100,
                Message = update.Stage
            };
            UpsertOperation(operation);

            var wholePercent = (int)OperationProgress;
            if (wholePercent >= lastPersistedPercent + 5)
            {
                lastPersistedPercent = wholePercent;
                try
                {
                    _operationJournal.Update(journalId, new WorkerMessage(
                        "progress", update.Stage,
                        ProcessedBytes: update.ProcessedBytes,
                        TotalBytes: update.TotalBytes,
                        ProcessedFiles: wholePercent,
                        TotalFiles: 100));
                }
                catch { }
            }
        });

        try
        {
            var result = await _analysisService.AnalyzeAsync(
                game,
                progress,
                _analysisCancellation.Token,
                maximumSampleBytes);
            acceptAnalysisProgress = false;
            var analyzedGameSelected = IsGameSelected(game);
            if (analyzedGameSelected)
            {
                Estimates.Clear();
                foreach (var estimate in result.Estimates)
                    Estimates.Add(estimate);
                SelectedEstimate = ChooseBalancedEstimate(result.Estimates);
            }

            var analyzedAt = DateTimeOffset.Now;
            var cacheSaved = true;
            try
            {
                _analysisCache.Save(new SavedGameAnalysis(game.InstallPath, analyzedAt, result, game.SteamBuildId));
                game.IsAnalysisStale = false;
            }
            catch (IOException)
            {
                cacheSaved = false;
            }
            catch (UnauthorizedAccessException)
            {
                cacheSaved = false;
            }

            OperationProgress = 100;
            if (analyzedGameSelected)
                AnalysisSummary = BuildSavedAnalysisSummary(result, analyzedAt);
            StatusText = cacheSaved
                ? $"Анализ игры «{game.Name}» завершён и сохранён"
                : $"Анализ игры «{game.Name}» завершён, но кэш записать не удалось";
            OperationSummary = StatusText;
            operation = operation with
            {
                FinishedAt = DateTimeOffset.Now,
                State = OperationJournalState.Completed,
                ProcessedBytes = result.SampleBytes,
                TotalBytes = result.SampleBytes,
                ProcessedFiles = 100,
                TotalFiles = 100,
                Message = StatusText
            };
            UpsertOperation(operation);
            try
            {
                _operationJournal.Update(journalId, new WorkerMessage(
                    "completed", StatusText,
                    ProcessedBytes: result.SampleBytes,
                    TotalBytes: result.SampleBytes,
                    ProcessedFiles: 100,
                    TotalFiles: 100));
                _operationJournal.Finish(journalId, OperationJournalState.Completed, StatusText);
            }
            catch { }
        }
        catch (OperationCanceledException)
        {
            acceptAnalysisProgress = false;
            OperationSummary = "Анализ отменён. Временные файлы удалены.";
            if (IsGameSelected(game))
                AnalysisSummary = OperationSummary;
            StatusText = "Анализ отменён";
            operation = operation with
            {
                FinishedAt = DateTimeOffset.Now,
                State = OperationJournalState.Cancelled,
                Message = OperationSummary
            };
            UpsertOperation(operation);
            try { _operationJournal.Finish(journalId, OperationJournalState.Cancelled, OperationSummary); } catch { }
        }
        catch (Exception exception)
        {
            acceptAnalysisProgress = false;
            OperationSummary = $"Анализ не выполнен: {exception.Message}";
            if (IsGameSelected(game))
                AnalysisSummary = OperationSummary;
            StatusText = "Ошибка анализа";
            operation = operation with
            {
                FinishedAt = DateTimeOffset.Now,
                State = OperationJournalState.Failed,
                Message = OperationSummary
            };
            UpsertOperation(operation);
            try { _operationJournal.Finish(journalId, OperationJournalState.Failed, OperationSummary); } catch { }
        }
        finally
        {
            acceptAnalysisProgress = false;
            _analysisCancellation.Dispose();
            _analysisCancellation = null;
            IsAnalyzing = false;
            ClearActiveOperation();
            if (IsGameSelected(game))
                AnalysisButtonText = "Повторить анализ";
        }
    }

    private GameInfo ApplySavedCompressionStatus(GameInfo game)
    {
        var saved = _compressionStatusStore.Load(game.InstallPath);
        if (saved is not null)
        {
            if (saved.LogicalBytes > 0)
                game.LogicalSizeBytes = saved.LogicalBytes;
            game.CompressionState = saved.State;
            game.CompressionAlgorithm = saved.Algorithm;
            game.CompressionSavedBytes = saved.SavedBytes;
            game.CompressedPhysicalBytes = saved.PhysicalBytes;
            game.CompressedFileCount = saved.CompressedFiles;
            game.CompressionCheckedAt = saved.CheckedAt;
            if (saved.State == GameCompressionState.Compressed &&
                !string.IsNullOrWhiteSpace(saved.SteamBuildId) &&
                !string.IsNullOrWhiteSpace(game.SteamBuildId) &&
                !string.Equals(saved.SteamBuildId, game.SteamBuildId, StringComparison.Ordinal))
                game.CompressionState = GameCompressionState.PartiallyCompressed;
        }
        return game;
    }

    // Full detection walks every file with a WOF query, which is expensive for large
    // games, so a recently saved status is trusted instead of re-walking on each selection.
    internal static readonly TimeSpan CompressionStatusTtl = TimeSpan.FromHours(6);

    internal static bool HasFreshCompressionStatus(GameInfo game) =>
        HasFreshCompressionStatus(game, DateTimeOffset.Now);

    internal static bool HasFreshCompressionStatus(GameInfo game, DateTimeOffset now) =>
        game.CompressionState != GameCompressionState.Unknown &&
        game.CompressionCheckedAt is { } checkedAt &&
        now - checkedAt < CompressionStatusTtl;

    // Balance rule: a slightly smaller saving is preferred over a noticeable read slowdown —
    // a few hundred megabytes do not matter when reads become 15+ percent slower.
    internal const double MaximumReadSlowdownPercent = -15;
    internal const long NegligibleSavingsDifferenceBytes = 400L * 1024 * 1024;

    internal static CompressionEstimate? ChooseBalancedEstimate(IReadOnlyList<CompressionEstimate> estimates)
    {
        if (estimates.Count == 0)
            return null;

        var hasBaseline = estimates.Any(estimate => estimate.BaselineReadMegabytesPerSecond > 0);
        var eligible = hasBaseline
            ? estimates.Where(estimate => estimate.ReadSpeedChangePercent >= MaximumReadSlowdownPercent).ToArray()
            : estimates.ToArray();
        if (eligible.Length == 0)
            eligible = estimates.ToArray();

        var topSavings = eligible.Max(estimate => estimate.MinimumSavingsBytes);
        return eligible
            .Where(estimate => topSavings - estimate.MinimumSavingsBytes <= NegligibleSavingsDifferenceBytes)
            .OrderByDescending(estimate => estimate.ReadSpeedChangePercent)
            .ThenByDescending(estimate => estimate.MinimumSavingsBytes)
            .First();
    }

    private bool IsGameSelected(GameInfo game) =>
        SelectedGame is not null &&
        string.Equals(SelectedGame.InstallPath, game.InstallPath, StringComparison.OrdinalIgnoreCase);

    private void OpenSelectedGameFolder()
    {
        var game = SelectedGame;
        if (game is null)
            return;
        if (!Directory.Exists(game.InstallPath))
        {
            StatusText = $"Папка игры не найдена: {game.InstallPath}";
            return;
        }

        try
        {
            using var explorer = Process.Start(new ProcessStartInfo
            {
                FileName = game.InstallPath,
                UseShellExecute = true
            });
            StatusText = $"Открываем папку игры «{game.Name}»";
        }
        catch (Win32Exception exception)
        {
            StatusText = $"Не удалось открыть проводник: {exception.Message}";
        }
    }

    private void SetActiveOperation(string description, string installPath)
    {
        _activeOperationDescription = description;
        _activeOperationPath = installPath;
        NotifyActiveOperationLabel();
    }

    private void ClearActiveOperation()
    {
        _activeOperationPath = null;
        _activeOperationDescription = string.Empty;
        _activeCompressionAlgorithm = null;
        _activeCompressionSavings = null;
        NotifyActiveOperationLabel();
        NotifyActiveCompressionInfo();
    }

    private void NotifyActiveOperationLabel()
    {
        OnPropertyChanged(nameof(ActiveOperationLabel));
        OnPropertyChanged(nameof(ActiveOperationLabelVisibility));
    }

    private void NotifyActiveCompressionInfo()
    {
        OnPropertyChanged(nameof(ActiveCompressionModeText));
        OnPropertyChanged(nameof(ActiveCompressionDetailsText));
        OnPropertyChanged(nameof(ActiveCompressionInfoVisibility));
    }

    private void RefreshSavingsSummary()
    {
        long totalSavedBytes = 0;
        var savedByRoot = new Dictionary<string, long>(StringComparer.OrdinalIgnoreCase);
        foreach (var game in Games)
        {
            if (game.CompressionSavedBytes <= 0)
                continue;
            totalSavedBytes += game.CompressionSavedBytes;
            var root = Path.GetPathRoot(game.InstallPath);
            if (string.IsNullOrEmpty(root))
                continue;
            savedByRoot.TryGetValue(root, out var currentBytes);
            savedByRoot[root] = currentBytes + game.CompressionSavedBytes;
        }

        foreach (var drive in Computer.Drives)
            drive.SavedBytes = savedByRoot.TryGetValue(drive.Name, out var savedBytes) ? savedBytes : 0;

        TotalSavingsText = totalSavedBytes > 0
            ? $"Сэкономлено всего: {ByteFormatter.Format(totalSavedBytes)}"
            : string.Empty;
    }

    private static string DescribeCompressionState(GameInfo game) => game.CompressionState switch
    {
        GameCompressionState.Compressed => $"Игра «{game.Name}» уже сжата: {game.CompressionAlgorithm ?? "Windows"}",
        GameCompressionState.PartiallyCompressed => $"Игра «{game.Name}» сжата частично: {game.CompressionAlgorithm ?? "Windows"}",
        GameCompressionState.Uncompressed => $"Игра «{game.Name}» не сжата",
        _ => $"Состояние сжатия игры «{game.Name}» не проверено"
    };

    private Task RecheckSelectedGameCompressionAsync()
    {
        var game = SelectedGame;
        if (game is null)
            return Task.CompletedTask;

        _compressionCheckCancellation?.Cancel();
        _compressionCheckCancellation?.Dispose();
        _compressionCheckCancellation = new CancellationTokenSource();
        return RefreshCompressionStatusAsync(game, _compressionCheckCancellation.Token);
    }

    private async Task RefreshCompressionStatusAsync(GameInfo game, CancellationToken cancellationToken)
    {
        IsCheckingCompression = true;
        StatusText = $"Проверяем состояние сжатия игры «{game.Name}»…";
        try
        {
            var detected = await _compressionDetector.DetectAsync(game.InstallPath, cancellationToken);
            if (SelectedGame?.InstallPath.Equals(game.InstallPath, StringComparison.OrdinalIgnoreCase) != true)
                return;

            var savedStatus = _compressionStatusStore.Load(game.InstallPath);
            var state = detected.State;
            var buildChanged = state == GameCompressionState.Compressed &&
                               savedStatus?.State is GameCompressionState.Compressed or GameCompressionState.PartiallyCompressed &&
                               !string.IsNullOrWhiteSpace(savedStatus.SteamBuildId) &&
                               !string.IsNullOrWhiteSpace(game.SteamBuildId) &&
                               !string.Equals(savedStatus.SteamBuildId, game.SteamBuildId, StringComparison.Ordinal);
            if (buildChanged)
                state = GameCompressionState.PartiallyCompressed;

            UpdateGameCompressionStatus(
                game.InstallPath, state, detected.Algorithm,
                detected.SavedBytes, detected.PhysicalBytes, detected.CompressedFiles, DateTimeOffset.Now,
                detected.LogicalBytes);
            TrySaveCompressionStatus(
                game.InstallPath, state, detected.Algorithm,
                detected.SavedBytes, detected.PhysicalBytes, detected.LogicalBytes, detected.CompressedFiles,
                buildChanged ? savedStatus?.SteamBuildId : game.SteamBuildId);
            StatusText = state switch
            {
                GameCompressionState.Compressed => $"Игра «{game.Name}» уже сжата: {detected.Algorithm ?? "Windows"}",
                GameCompressionState.PartiallyCompressed => buildChanged
                    ? $"Игра «{game.Name}» обновилась и требует дообработки"
                    : $"Игра «{game.Name}» сжата частично: {detected.Algorithm ?? "Windows"}",
                _ => $"Игра «{game.Name}» не сжата"
            };
        }
        catch (OperationCanceledException) { }
        catch (Exception exception)
        {
            StatusText = $"Не удалось определить состояние сжатия: {exception.Message}";
        }
        finally
        {
            if (SelectedGame?.InstallPath.Equals(game.InstallPath, StringComparison.OrdinalIgnoreCase) == true)
                IsCheckingCompression = false;
        }
    }

    private void UpdateGameCompressionStatus(
        string installPath,
        GameCompressionState state,
        string? algorithm,
        long savedBytes = 0,
        long physicalBytes = 0,
        int compressedFiles = 0,
        DateTimeOffset? checkedAt = null,
        long logicalBytes = 0)
    {
        var index = FindGameIndex(installPath);
        if (index >= 0)
        {
            if (logicalBytes > 0)
                Games[index].LogicalSizeBytes = logicalBytes;
            Games[index].CompressionState = state;
            Games[index].CompressionAlgorithm = algorithm;
            Games[index].CompressionSavedBytes = savedBytes;
            Games[index].CompressedPhysicalBytes = physicalBytes;
            Games[index].CompressedFileCount = compressedFiles;
            Games[index].CompressionCheckedAt = checkedAt ?? DateTimeOffset.Now;
        }
        NotifyCompressionPanelVisibility();
        RaiseActionCommands();
        RefreshSavingsSummary();
    }

    private void NotifyCompressionPanelVisibility()
    {
        OnPropertyChanged(nameof(UncompressedPanelVisibility));
        OnPropertyChanged(nameof(AutoOptimizationVisibility));
        OnPropertyChanged(nameof(ExpertOptimizationVisibility));
        OnPropertyChanged(nameof(CompressedPanelVisibility));
        OnPropertyChanged(nameof(PartiallyCompressedVisibility));
        OnPropertyChanged(nameof(EstimateListVisibility));
        OnPropertyChanged(nameof(AutoInfoVisibility));
        OnPropertyChanged(nameof(ActiveCompressionInfoVisibility));
    }

    private void TrySaveCompressionStatus(
        string installPath,
        GameCompressionState state,
        string? algorithm,
        long savedBytes = 0,
        long physicalBytes = 0,
        long logicalBytes = 0,
        int compressedFiles = 0,
        string? steamBuildId = null)
    {
        try
        {
            _compressionStatusStore.Save(new SavedCompressionStatus(
                installPath, state, algorithm, DateTimeOffset.Now,
                savedBytes, physicalBytes, logicalBytes, compressedFiles, steamBuildId));
        }
        catch (IOException) { }
        catch (UnauthorizedAccessException) { }
    }

    private void RaiseActionCommands()
    {
        AnalyzeCommand.RaiseCanExecuteChanged();
        OptimizeCommand.RaiseCanExecuteChanged();
        CompressCommand.RaiseCanExecuteChanged();
        DecompressCommand.RaiseCanExecuteChanged();
        RemoveGameCommand.RaiseCanExecuteChanged();
        RecheckCompressionCommand.RaiseCanExecuteChanged();
        OpenGameFolderCommand.RaiseCanExecuteChanged();
    }

    private void RestoreSavedAnalysis(GameInfo game)
    {
        var saved = _analysisCache.Load(game.InstallPath);
        if (saved is null)
            return;

        game.IsAnalysisStale = !string.IsNullOrWhiteSpace(saved.SteamBuildId) &&
                               !string.IsNullOrWhiteSpace(game.SteamBuildId) &&
                               !string.Equals(saved.SteamBuildId, game.SteamBuildId, StringComparison.Ordinal);

        foreach (var estimate in saved.Result.Estimates)
            Estimates.Add(estimate);
        SelectedEstimate = ChooseBalancedEstimate(saved.Result.Estimates) ?? Estimates.FirstOrDefault();
        AnalysisSummary = game.IsAnalysisStale
            ? "Игра обновилась — перед оптимизацией нужен новый быстрый анализ."
            : BuildSavedAnalysisSummary(saved.Result, saved.AnalyzedAt);
        AnalysisButtonText = "Повторить анализ";
        StatusText = $"Загружен сохранённый анализ игры «{game.Name}»";
    }

    private static string BuildSavedAnalysisSummary(GameAnalysisResult result, DateTimeOffset analyzedAt) =>
        $"Анализ от {analyzedAt:dd.MM.yyyy HH:mm} · {result.FileCount:N0} файлов · " +
        $"выборка {ByteFormatter.Format(result.SampleBytes)} · физически занято {ByteFormatter.Format(result.CurrentPhysicalBytes)}";

    private async Task OptimizeSelectedGameAsync()
    {
        var game = SelectedGame;
        if (game is null)
            return;

        if (Estimates.Count == 0 || game.IsAnalysisStale)
            await AnalyzeSelectedGameAsync(0);

        if (SelectedGame is null || Estimates.Count == 0 || SelectedGame.IsAnalysisStale)
            return;

        SelectedEstimate = ChooseBalancedEstimate(Estimates.ToArray()) ?? Estimates.FirstOrDefault();
        if (SelectedEstimate is null)
            return;

        await CompressSelectedGameAsync();
    }

    private async Task CancelCurrentAsync()
    {
        _analysisCancellation?.Cancel();
        await _workerClient.CancelAsync();
    }

    private async Task CompressSelectedGameAsync()
    {
        var game = SelectedGame;
        var estimate = SelectedEstimate;
        if (game is null || estimate is null)
            return;

        var confirmation = MessageBox.Show(
            Application.Current.MainWindow,
            $"Игра: {game.Name}\nАлгоритм: {estimate.AlgorithmText}\n" +
            $"Прогноз размера: {estimate.EstimatedSizeText}\nЭкономия: {estimate.SavingsText}\n\n" +
            "Игра должна быть закрыта. Продолжить?",
            "Подтверждение сжатия",
            MessageBoxButton.YesNo,
            MessageBoxImage.Warning);
        if (confirmation != MessageBoxResult.Yes)
            return;

        await ExecuteWorkerOperationAsync(new WorkerJob(game.InstallPath, "compress", estimate.AlgorithmText));
    }

    private async Task DecompressSelectedGameAsync()
    {
        var game = SelectedGame;
        if (game is null)
            return;

        var confirmation = MessageBox.Show(
            Application.Current.MainWindow,
            $"Полностью распаковать файлы игры «{game.Name}»?\n\nИгра должна быть закрыта.",
            "Подтверждение распаковки",
            MessageBoxButton.YesNo,
            MessageBoxImage.Question);
        if (confirmation != MessageBoxResult.Yes)
            return;

        await ExecuteWorkerOperationAsync(new WorkerJob(game.InstallPath, "decompress", null));
    }

    private async Task ExecuteWorkerOperationAsync(WorkerJob job)
    {
        Guid journalId;
        var startedAt = DateTimeOffset.Now;
        try { journalId = _operationJournal.Begin(job); }
        catch { journalId = Guid.NewGuid(); }
        var operation = new OperationJournalEntry(
            journalId, job.RootPath, job.Operation, job.Algorithm, startedAt, null,
            OperationJournalState.Running, 0, 0, 0, 0, 0, "Ожидание Worker");
        UpsertOperation(operation);
        var acceptWorkerProgress = true;

        IsOperating = true;
        var targetGame = Games.FirstOrDefault(item =>
            string.Equals(item.InstallPath, job.RootPath, StringComparison.OrdinalIgnoreCase));
        SetActiveOperation(
            $"{(job.Operation == "compress" ? "Сжимается" : "Распаковывается")}: {targetGame?.Name ?? job.RootPath}",
            job.RootPath);
        var selectedEstimate = SelectedEstimate;
        _activeCompressionAlgorithm = job.Operation == "compress" ? job.Algorithm : null;
        _activeCompressionSavings = job.Operation == "compress" && selectedEstimate is not null &&
                                    selectedEstimate.AlgorithmText == job.Algorithm
            ? selectedEstimate.SavingsText
            : null;
        NotifyActiveCompressionInfo();
        OperationProgress = 0;
        OperationSummary = "Ожидаем подтверждение прав администратора…";
        StatusText = OperationSummary;

        var progress = new Progress<WorkerMessage>(message =>
        {
            if (!acceptWorkerProgress)
                return;
            if (message.TotalBytes > 0)
                OperationProgress = Math.Clamp(message.ProcessedBytes * 100d / message.TotalBytes, 0, 100);
            var counters = message.TotalFiles > 0
                ? $" · {message.ProcessedFiles:N0} из {message.TotalFiles:N0} файлов"
                : string.Empty;
            OperationSummary = $"{message.Text}{counters}";
            StatusText = OperationSummary;
            operation = operation with
            {
                ProcessedBytes = message.ProcessedBytes,
                TotalBytes = message.TotalBytes,
                ProcessedFiles = message.ProcessedFiles,
                TotalFiles = message.TotalFiles,
                ErrorCount = message.ErrorCount,
                Message = OperationSummary
            };
            UpsertOperation(operation);
            try { _operationJournal.Update(journalId, message); } catch { }
        });

        try
        {
            var result = await _workerClient.ExecuteAsync(job, progress);
            acceptWorkerProgress = false;
            if (result.Type == "cancelled")
            {
                OperationSummary = result.Text ?? "Операция отменена";
                StatusText = OperationSummary;
                operation = operation with
                {
                    FinishedAt = DateTimeOffset.Now,
                    State = OperationJournalState.Cancelled,
                    ProcessedBytes = result.ProcessedBytes,
                    TotalBytes = result.TotalBytes,
                    ProcessedFiles = result.ProcessedFiles,
                    TotalFiles = result.TotalFiles,
                    ErrorCount = result.ErrorCount,
                    Message = OperationSummary
                };
                UpsertOperation(operation);
                try
                {
                    _operationJournal.Update(journalId, result);
                    _operationJournal.Finish(journalId, OperationJournalState.Cancelled, OperationSummary);
                }
                catch { }
                return;
            }

            OperationProgress = 100;
            var processedGame = Games.FirstOrDefault(game =>
                string.Equals(game.InstallPath, job.RootPath, StringComparison.OrdinalIgnoreCase));
            var decompressIncomplete = job.Operation == "decompress" && result.ErrorCount > 0;
            var newState = job.Operation == "compress"
                ? (result.ErrorCount == 0 ? GameCompressionState.Compressed : GameCompressionState.PartiallyCompressed)
                : decompressIncomplete
                    ? GameCompressionState.PartiallyCompressed
                    : GameCompressionState.Uncompressed;
            var newAlgorithm = job.Operation == "compress"
                ? job.Algorithm
                : decompressIncomplete ? processedGame?.CompressionAlgorithm : null;
            var difference = result.PhysicalBefore - result.PhysicalAfter;
            var savedBytes = job.Operation == "compress" ? Math.Max(0, difference) : 0;
            var compressedFiles = job.Operation == "compress"
                ? result.ProcessedFiles
                : decompressIncomplete ? result.ErrorCount : 0;
            UpdateGameCompressionStatus(
                job.RootPath, newState, newAlgorithm,
                savedBytes, result.PhysicalAfter, compressedFiles, DateTimeOffset.Now,
                result.TotalBytes);
            TrySaveCompressionStatus(
                job.RootPath, newState, newAlgorithm,
                savedBytes, result.PhysicalAfter, result.TotalBytes, compressedFiles,
                processedGame?.SteamBuildId);

            OperationSummary = job.Operation == "compress"
                ? $"Готово · освобождено {ByteFormatter.Format(Math.Max(0, difference))} · ошибок: {result.ErrorCount}"
                : decompressIncomplete
                    ? $"Распаковка завершена частично · ошибок: {result.ErrorCount}"
                    : $"Готово · распаковано {result.ProcessedFiles:N0} файлов · ошибок: {result.ErrorCount}";
            StatusText = OperationSummary;
            Computer = _computerInfoService.GetComputerInfo();
            RefreshSavingsSummary();
            operation = operation with
            {
                FinishedAt = DateTimeOffset.Now,
                State = OperationJournalState.Completed,
                ProcessedBytes = result.ProcessedBytes,
                TotalBytes = result.TotalBytes,
                ProcessedFiles = result.ProcessedFiles,
                TotalFiles = result.TotalFiles,
                ErrorCount = result.ErrorCount,
                Message = OperationSummary
            };
            UpsertOperation(operation);
            try
            {
                _operationJournal.Update(journalId, result);
                _operationJournal.Finish(journalId, OperationJournalState.Completed, OperationSummary);
            }
            catch { }
        }
        catch (OperationCanceledException exception)
        {
            acceptWorkerProgress = false;
            OperationSummary = exception.Message;
            StatusText = "Операция отменена";
            operation = operation with
            {
                FinishedAt = DateTimeOffset.Now,
                State = OperationJournalState.Cancelled,
                Message = OperationSummary
            };
            UpsertOperation(operation);
            try { _operationJournal.Finish(journalId, OperationJournalState.Cancelled, OperationSummary); } catch { }
        }
        catch (Exception exception)
        {
            acceptWorkerProgress = false;
            OperationSummary = $"Операция не выполнена: {exception.Message}";
            StatusText = "Ошибка системной операции";
            operation = operation with
            {
                FinishedAt = DateTimeOffset.Now,
                State = OperationJournalState.Failed,
                Message = OperationSummary
            };
            UpsertOperation(operation);
            try { _operationJournal.Finish(journalId, OperationJournalState.Failed, OperationSummary); } catch { }
        }
        finally
        {
            acceptWorkerProgress = false;
            IsOperating = false;
            ClearActiveOperation();
        }
    }

    private int FindGameIndex(string installPath)
    {
        for (var index = 0; index < Games.Count; index++)
        {
            if (string.Equals(Games[index].InstallPath, installPath, StringComparison.OrdinalIgnoreCase))
                return index;
        }
        return -1;
    }

    private void CancelAnalysis() => _analysisCancellation?.Cancel();
}
