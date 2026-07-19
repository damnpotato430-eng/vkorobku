using System.Collections.ObjectModel;
using System.Collections.Specialized;
using System.ComponentModel;
using System.Diagnostics;
using System.Net.Http;
using System.Windows;
using System.Windows.Data;
using Microsoft.Win32;
using vKOROBKU.App.Models;
using vKOROBKU.App.Resources;
using vKOROBKU.App.Services;
using vKOROBKU.Protocol;

namespace vKOROBKU.App.ViewModels;

public sealed class MainViewModel : ObservableObject
{
    private const int CurrentIdentityVersion = 4;
    private readonly IReadOnlyList<IGameScanner> _gameScanners =
        [new SteamLibraryScanner(), new EpicGamesScanner(), new GogScanner(), new UbisoftScanner(), new EaScanner()];
    private readonly ComputerInfoService _computerInfoService = new();
    private readonly FileTreeService _fileTreeService = new();
    private readonly GameAnalysisService _analysisService = new();
    private readonly AnalysisWorkspaceCleaner _analysisWorkspaceCleaner = new();
    private readonly CoverService _coverService;
    private readonly CoverBatchLoader _coverBatchLoader;
    private readonly CompressionWorkerClient _workerClient = new();
    private readonly AnalysisCacheStore _analysisCache = new();
    private readonly CompressionStatusStore _compressionStatusStore = new();
    private readonly GameCompressionDetector _compressionDetector = new();
    private readonly OperationJournalStore _operationJournal = new();
    private readonly CompressionStatsStore _statsStore = new();
    private readonly UserPreferencesStore _preferences = new();
    private readonly ManualGameStore _manualGameStore = new();
    private readonly WatchedGamesCoordinator _watcher = new();
    private readonly DirectStorageDetector _directStorageDetector = new();
    private readonly UpdateCheckService _updateCheckService = new();
    private string _updateNoticeText = string.Empty;
    private string? _updateUrl;
    private string _searchText = string.Empty;
    private ChoiceOption _selectedSortOption;
    private UserPreferences _userPreferences;
    private bool _isWatcherCheckRunning;
    private string _watcherSummaryText = string.Empty;
    private bool _watcherHasFindings;
    private bool _showOnlyDegraded;
    private HashSet<string> _degradedPaths = new(StringComparer.OrdinalIgnoreCase);
    private readonly GameIdentityService _gameIdentityService = new();
    private ComputerInfo _computer = null!;
    private GameInfo? _selectedGame;
    private CompressionEstimate? _selectedEstimate;
    private OperationJournalEntry? _currentOperation;
    private AnalysisModeOption? _selectedAnalysisMode;
    private CancellationTokenSource? _analysisCancellation;
    private CancellationTokenSource? _compressionCheckCancellation;
    private string _statusText = Strings.Status_ReadyToScan;
    private string _scanButtonText = Strings.Scan_FindGames;
    private string _analysisButtonText = Strings.Analysis_ButtonCalculate;
    private string _analysisSummary = Strings.Analysis_SelectGameHint;
    private bool _isAnalyzing;
    private bool _isOperating;
    private bool _isCheckingCompression;
    private bool _isExpertMode;
    private double _operationProgress;
    private string _operationSummary = Strings.Operation_DefaultSummary;
    private string _totalSavingsText = string.Empty;
    private string _allTimeStatsText = string.Empty;
    private string _allTimeStatsTooltip = string.Empty;
    private string? _activeOperationPath;
    private string _activeOperationDescription = string.Empty;
    private string? _activeCompressionAlgorithm;
    private string? _activeCompressionSavings;
    private bool _isMultiSelectMode;
    private ChoiceOption _selectedQueueMethod;
    private string _queueSelectionText = string.Empty;
    private string _queueTitle = string.Empty;
    private bool _queueStopAfterCurrent;
    private bool _queueStopAll;
    private bool _isQueueRunning;

    private const string AutoQueueMethod = "auto";

    /// <summary>A combo-box option whose identity is a stable id while the shown text
    /// is localized — logic must never compare display strings.</summary>
    public sealed record ChoiceOption(string Id, string DisplayText)
    {
        public override string ToString() => DisplayText;
    }

    public MainViewModel()
    {
        _computer = _computerInfoService.GetComputerInfo();
        _userPreferences = _preferences.Load();
        _isExpertMode = _userPreferences.ExpertMode;
        GamesView = CollectionViewSource.GetDefaultView(Games);
        GamesView.Filter = FilterGame;
        // Must precede ApplySort — it reads the selected option's id.
        _selectedSortOption = SortOptions[0];
        _selectedQueueMethod = QueueMethodOptions[0];
        ApplySort();
        // Live sorting keeps a card in its correct slot when its state changes in place
        // (e.g. a game finishes compressing while sorted by "uncompressed first").
        if (GamesView is ICollectionViewLiveShaping { CanChangeLiveSorting: true } liveView)
        {
            liveView.LiveSortingProperties.Add(nameof(GameInfo.Name));
            liveView.LiveSortingProperties.Add(nameof(GameInfo.LogicalSizeBytes));
            liveView.LiveSortingProperties.Add(nameof(GameInfo.CompressionState));
            liveView.IsLiveSorting = true;
        }
        _coverService = new CoverService(_gameIdentityService.FindSteamAppIdAsync);
        _coverBatchLoader = new CoverBatchLoader(_coverService);
        AnalysisModes.Add(new AnalysisModeOption(Strings.AnalysisMode_Auto, Strings.AnalysisMode_AutoHint, 0));
        AnalysisModes.Add(new AnalysisModeOption(Strings.AnalysisMode_Fast, Strings.AnalysisMode_FastHint, 512L * 1024 * 1024));
        AnalysisModes.Add(new AnalysisModeOption(Strings.AnalysisMode_Precise, Strings.AnalysisMode_PreciseHint, 1024L * 1024 * 1024));
        AnalysisModes.Add(new AnalysisModeOption(Strings.AnalysisMode_Maximum, Strings.AnalysisMode_MaximumHint, 2L * 1024 * 1024 * 1024));
        SelectedAnalysisMode = AnalysisModes[2];
        RefreshLibraryCommand = new AsyncRelayCommand(RefreshLibraryAsync);
        AddFolderCommand = new AsyncRelayCommand(AddFolderAsync);
        ShowOperationsCommand = new RelayCommand(ShowOperations);
        ReviewIdentityCommand = new AsyncRelayCommand(ReviewSelectedGameIdentityAsync,
            () => SelectedGame?.NeedsIdentityReview == true);
        RemoveGameCommand = new RelayCommand(RemoveSelectedGame,
            () => SelectedGame?.Source == GameInfo.ManualSource && !IsAnalyzing && !IsOperating && !IsCheckingCompression);
        RecheckCompressionCommand = new AsyncRelayCommand(RecheckSelectedGameCompressionAsync,
            () => SelectedGame is not null && !IsAnalyzing && !IsOperating && !IsCheckingCompression);
        OpenGameFolderCommand = new RelayCommand(OpenSelectedGameFolder, () => SelectedGame is not null);
        ShowSettingsCommand = new RelayCommand(ShowSettings);
        ShowAboutCommand = new RelayCommand(ShowAbout);
        OpenUpdateCommand = new RelayCommand(OpenUpdatePage, () => _updateUrl is not null);
        CheckWatchedGamesCommand = new AsyncRelayCommand(() => CheckWatchedGamesAsync(true),
            () => !_isWatcherCheckRunning && !IsAnalyzing && !IsOperating);
        FinishCompressionCommand = new AsyncRelayCommand(FinishCompressionAsync,
            () => SelectedGame is { CompressionState: GameCompressionState.PartiallyCompressed } game &&
                  IsResumableAlgorithm(game.CompressionAlgorithm) &&
                  !IsAnalyzing && !IsOperating && !IsCheckingCompression);
        RefreshCoversCommand = new AsyncRelayCommand(() => LoadCoversAsync(true), () => Games.Count > 0);
        RefreshSelectedCoverCommand = new AsyncRelayCommand(RefreshSelectedCoverAsync, () => SelectedGame is not null);
        AnalyzeCommand = new AsyncRelayCommand(AnalyzeSelectedGameAsync,
            () => SelectedGame is { CompressionState: not GameCompressionState.Compressed } && !IsAnalyzing && !IsOperating && !IsCheckingCompression);
        OptimizeCommand = new AsyncRelayCommand(OptimizeSelectedGameAsync,
            () => SelectedGame is { CompressionState: not GameCompressionState.Compressed } &&
                  !(SelectedGame?.HasDirectStorage == true && !IsExpertMode) &&
                  !IsAnalyzing && !IsOperating && !IsCheckingCompression);
        CancelAnalysisCommand = new RelayCommand(CancelAnalysis, () => IsAnalyzing);
        CompressCommand = new AsyncRelayCommand(CompressSelectedGameAsync,
            () => SelectedGame is { CompressionState: not GameCompressionState.Compressed, IsAnalysisStale: false } && SelectedEstimate is not null && !IsAnalyzing && !IsOperating && !IsCheckingCompression);
        DecompressCommand = new AsyncRelayCommand(DecompressSelectedGameAsync,
            () => SelectedGame is { CompressionState: GameCompressionState.Compressed or GameCompressionState.PartiallyCompressed } && !IsAnalyzing && !IsOperating && !IsCheckingCompression);
        CancelOperationCommand = new AsyncRelayCommand(_workerClient.CancelAsync, () => IsOperating);
        CancelCurrentCommand = new AsyncRelayCommand(CancelCurrentAsync, () => IsAnalyzing || IsOperating);
        ToggleMultiSelectCommand = new RelayCommand(ToggleMultiSelectMode, () => Games.Count > 0 && !IsOperating);
        StartQueueCommand = new AsyncRelayCommand(StartQueueAsync,
            () => Games.Any(game => game.IsQueueSelected) && !IsAnalyzing && !IsOperating && !IsCheckingCompression);
        SkipQueueItemCommand = new AsyncRelayCommand(_workerClient.CancelAsync, () => IsQueueRunning);
        StopQueueAfterCurrentCommand = new RelayCommand(() => _queueStopAfterCurrent = true, () => IsQueueRunning);
        StopQueueCommand = new AsyncRelayCommand(StopQueueAsync, () => IsQueueRunning);
        RecompressDegradedCommand = new AsyncRelayCommand(RecompressDegradedAsync,
            () => WatcherHasFindings && !IsAnalyzing && !IsOperating && !IsCheckingCompression);
        AddToQueueCommand = new RelayCommand(AddSelectedGameToQueue, () => SelectedGame is not null && !IsOperating);
        Games.CollectionChanged += OnGamesCollectionChanged;
        QueueItems.CollectionChanged += (_, _) => OnPropertyChanged(nameof(QueuePanelVisibility));
        RefreshAllTimeStats();
    }

    public ObservableCollection<GameInfo> Games { get; } = [];
    public ObservableCollection<CompressionEstimate> Estimates { get; } = [];
    public ObservableCollection<OperationJournalEntry> Operations { get; } = [];
    public ObservableCollection<AnalysisModeOption> AnalysisModes { get; } = [];
    public ObservableCollection<CompressionQueueItem> QueueItems { get; } = [];
    public ComputerInfo Computer
    {
        get => _computer;
        private set => SetProperty(ref _computer, value);
    }
    public AsyncRelayCommand RefreshLibraryCommand { get; }
    public AsyncRelayCommand AddFolderCommand { get; }
    public RelayCommand ShowOperationsCommand { get; }
    public AsyncRelayCommand ReviewIdentityCommand { get; }
    public RelayCommand RemoveGameCommand { get; }
    public AsyncRelayCommand RecheckCompressionCommand { get; }
    public RelayCommand OpenGameFolderCommand { get; }
    public AsyncRelayCommand RefreshSelectedCoverCommand { get; }
    public AsyncRelayCommand FinishCompressionCommand { get; }
    public RelayCommand ShowSettingsCommand { get; }
    public RelayCommand ShowAboutCommand { get; }
    public AsyncRelayCommand CheckWatchedGamesCommand { get; }
    public RelayCommand OpenUpdateCommand { get; }
    public RelayCommand ToggleMultiSelectCommand { get; }
    public AsyncRelayCommand StartQueueCommand { get; }
    public AsyncRelayCommand SkipQueueItemCommand { get; }
    public RelayCommand StopQueueAfterCurrentCommand { get; }
    public AsyncRelayCommand StopQueueCommand { get; }
    public AsyncRelayCommand RecompressDegradedCommand { get; }
    public RelayCommand AddToQueueCommand { get; }

    public ICollectionView GamesView { get; }
    public IReadOnlyList<ChoiceOption> SortOptions { get; } =
    [
        new("name", Strings.Sort_ByName),
        new("largest", Strings.Sort_LargestFirst),
        new("uncompressed", Strings.Sort_UncompressedFirst)
    ];

    public ChoiceOption SelectedSortOption
    {
        get => _selectedSortOption;
        set { if (SetProperty(ref _selectedSortOption, value)) ApplySort(); }
    }

    public string SearchText
    {
        get => _searchText;
        set { if (SetProperty(ref _searchText, value)) GamesView.Refresh(); }
    }

    public bool ShowOnlyDegraded
    {
        get => _showOnlyDegraded;
        set { if (SetProperty(ref _showOnlyDegraded, value)) GamesView.Refresh(); }
    }

    public string DegradedFilterLabel => string.Format(Strings.Watcher_NeedsFinishingCount, _degradedPaths.Count);

    public bool IsMultiSelectMode
    {
        get => _isMultiSelectMode;
        private set
        {
            if (!SetProperty(ref _isMultiSelectMode, value))
                return;
            OnPropertyChanged(nameof(MultiSelectVisibility));
            OnPropertyChanged(nameof(MultiSelectToggleText));
            if (!value)
                ClearQueueSelection();
            UpdateQueueSelectionSummary();
        }
    }

    public Visibility MultiSelectVisibility => IsMultiSelectMode ? Visibility.Visible : Visibility.Collapsed;
    public string MultiSelectToggleText => IsMultiSelectMode ? Strings.MultiSelect_Cancel : Strings.MultiSelect_Start;

    public IReadOnlyList<ChoiceOption> QueueMethodOptions { get; } =
    [
        new(AutoQueueMethod, Strings.Queue_MethodAuto),
        new("XPRESS4K", "XPRESS4K"),
        new("XPRESS8K", "XPRESS8K"),
        new("XPRESS16K", "XPRESS16K"),
        new("LZX", "LZX")
    ];

    public ChoiceOption SelectedQueueMethod
    {
        get => _selectedQueueMethod;
        set => SetProperty(ref _selectedQueueMethod, value);
    }

    public string QueueSelectionText
    {
        get => _queueSelectionText;
        private set => SetProperty(ref _queueSelectionText, value);
    }

    public string QueueTitle
    {
        get => _queueTitle;
        private set => SetProperty(ref _queueTitle, value);
    }

    public bool IsQueueRunning
    {
        get => _isQueueRunning;
        private set
        {
            if (!SetProperty(ref _isQueueRunning, value))
                return;
            OnPropertyChanged(nameof(QueueControlsVisibility));
            SkipQueueItemCommand.RaiseCanExecuteChanged();
            StopQueueAfterCurrentCommand.RaiseCanExecuteChanged();
            StopQueueCommand.RaiseCanExecuteChanged();
        }
    }

    public Visibility QueueControlsVisibility => IsQueueRunning ? Visibility.Visible : Visibility.Collapsed;
    public Visibility QueuePanelVisibility => QueueItems.Count > 0 ? Visibility.Visible : Visibility.Collapsed;
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
            AnalysisButtonText = Strings.Analysis_ButtonCalculate;
            AnalysisSummary = value is null
                ? Strings.Analysis_SelectGameHint
                : Strings.Analysis_AutoModeHint;
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
            FinishCompressionCommand.RaiseCanExecuteChanged();
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
            FinishCompressionCommand.RaiseCanExecuteChanged();
            ToggleMultiSelectCommand.RaiseCanExecuteChanged();
            StartQueueCommand.RaiseCanExecuteChanged();
            RecompressDegradedCommand.RaiseCanExecuteChanged();
            AddToQueueCommand.RaiseCanExecuteChanged();
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
            _userPreferences = _userPreferences with { ExpertMode = value };
            try { _preferences.Save(_userPreferences); }
            catch (Exception exception) { AppLog.Error("Не удалось сохранить настройки", exception); }
            NotifyCompressionPanelVisibility();
            RaiseActionCommands();
        }
    }

    public Visibility IdentityReviewVisibility =>
        SelectedGame?.NeedsIdentityReview == true ? Visibility.Visible : Visibility.Collapsed;

    public Visibility ManualGameVisibility =>
        SelectedGame?.Source == GameInfo.ManualSource ? Visibility.Visible : Visibility.Collapsed;

    // A partially compressed game with a known algorithm gets a dedicated "finish"
    // panel instead of the mode selection, so algorithms are not mixed within one game.
    private bool IsPartialResumeAvailable =>
        SelectedGame is { CompressionState: GameCompressionState.PartiallyCompressed } game &&
        IsResumableAlgorithm(game.CompressionAlgorithm);

    public Visibility UncompressedPanelVisibility =>
        SelectedGame?.CompressionState == GameCompressionState.Compressed || IsPartialResumeAvailable
            ? Visibility.Collapsed
            : Visibility.Visible;

    public Visibility AutoOptimizationVisibility =>
        SelectedGame?.CompressionState == GameCompressionState.Compressed || IsPartialResumeAvailable || IsExpertMode
            ? Visibility.Collapsed
            : Visibility.Visible;

    public Visibility ExpertOptimizationVisibility =>
        SelectedGame?.CompressionState == GameCompressionState.Compressed || IsPartialResumeAvailable || !IsExpertMode
            ? Visibility.Collapsed
            : Visibility.Visible;

    public Visibility PartialPanelVisibility =>
        IsPartialResumeAvailable ? Visibility.Visible : Visibility.Collapsed;

    public Visibility PartialInfoVisibility =>
        IsPartialResumeAvailable && !IsOperating ? Visibility.Visible : Visibility.Collapsed;

    public string FinishCompressionButtonText =>
        SelectedGame is { } selected && IsResumableAlgorithm(selected.CompressionAlgorithm)
            ? $"{Strings.Action_Finish} · {selected.CompressionAlgorithm}"
            : Strings.Action_Finish;

    public Visibility DirectStorageWarningVisibility =>
        SelectedGame?.HasDirectStorage == true && UncompressedPanelVisibility == Visibility.Visible
            ? Visibility.Visible
            : Visibility.Collapsed;

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

    public string AllTimeStatsText
    {
        get => _allTimeStatsText;
        private set => SetProperty(ref _allTimeStatsText, value);
    }

    public string AllTimeStatsTooltip
    {
        get => _allTimeStatsTooltip;
        private set => SetProperty(ref _allTimeStatsTooltip, value);
    }

    public string WatcherSummaryText
    {
        get => _watcherSummaryText;
        private set
        {
            if (SetProperty(ref _watcherSummaryText, value))
                OnPropertyChanged(nameof(WatcherSummaryVisibility));
        }
    }

    public bool WatcherHasFindings
    {
        get => _watcherHasFindings;
        private set => SetProperty(ref _watcherHasFindings, value);
    }

    public Visibility WatcherSummaryVisibility =>
        WatcherSummaryText.Length > 0 ? Visibility.Visible : Visibility.Collapsed;

    public string UpdateNoticeText
    {
        get => _updateNoticeText;
        private set
        {
            if (SetProperty(ref _updateNoticeText, value))
                OnPropertyChanged(nameof(UpdateNoticeVisibility));
        }
    }

    public Visibility UpdateNoticeVisibility =>
        UpdateNoticeText.Length > 0 ? Visibility.Visible : Visibility.Collapsed;

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
        _activeCompressionSavings is null ? string.Empty : string.Format(Strings.Active_ExpectedSavings, _activeCompressionSavings);

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
            AppLog.CleanupOldLogs();
            try { _analysisWorkspaceCleaner.CleanupOldWorkspaces(); }
            catch (IOException) { }
            catch (UnauthorizedAccessException) { }
        });

        var interrupted = 0;
        try { interrupted = _operationJournal.MarkInterrupted(); } catch { }
        LoadOperationHistory();
        await RefreshLibraryAsync();
        if (interrupted > 0)
            StatusText = Strings.Status_PreviousInterrupted;
        await OfferToResumeInterruptedCompressionAsync();
        _ = CheckWatchedGamesAsync(false);
        _ = CheckForUpdatesAsync();
    }

    private async Task CheckForUpdatesAsync()
    {
        try
        {
            var currentVersion = typeof(MainViewModel).Assembly.GetName().Version ?? new Version(0, 0);
            var newer = await _updateCheckService.CheckForNewerReleaseAsync(currentVersion);
            if (newer is null)
                return;

            _updateUrl = newer.Value.Url;
            UpdateNoticeText = string.Format(Strings.Update_Available, newer.Value.Version.ToString(3));
            OpenUpdateCommand.RaiseCanExecuteChanged();
            AppLog.Info($"Найдено обновление: {newer.Value.Version}");
        }
        catch (Exception exception)
        {
            AppLog.Error("Проверка обновлений не удалась", exception);
        }
    }

    private void OpenUpdatePage()
    {
        if (_updateUrl is null)
            return;
        try
        {
            using var browser = Process.Start(new ProcessStartInfo { FileName = _updateUrl, UseShellExecute = true });
        }
        catch (Win32Exception exception)
        {
            StatusText = string.Format(Strings.Status_BrowserFailed, exception.Message);
        }
    }

    private async Task CheckWatchedGamesAsync(bool force)
    {
        if (_isWatcherCheckRunning)
            return;
        if (!_userPreferences.WatcherEnabled)
        {
            UpdateWatcherSummary(new WatchedGamesCoordinator.CheckOutcome(0, []));
            return;
        }

        _isWatcherCheckRunning = true;
        CheckWatchedGamesCommand.RaiseCanExecuteChanged();
        try
        {
            _watcher.SeedFromLibrary(Games, _compressionStatusStore.Load);
            var outcome = await _watcher.CheckAsync(
                _userPreferences,
                FindGameByPath,
                MarkWatchedGameAsDegraded,
                MarkWatchedGameAsCompressed,
                message => WatcherSummaryText = message,
                force);
            UpdateWatcherSummary(outcome);
            if (outcome.Degraded.Count > 0)
                AppLog.Info($"Деградация сжатия: {string.Join(", ", outcome.Degraded.Select(item => item.DisplayName))}");
        }
        catch (Exception exception)
        {
            AppLog.Error("Проверка наблюдаемых игр не удалась", exception);
            WatcherHasFindings = false;
            ShowOnlyDegraded = false;
            WatcherSummaryText = Strings.Watcher_CheckFailed;
        }
        finally
        {
            _isWatcherCheckRunning = false;
            CheckWatchedGamesCommand.RaiseCanExecuteChanged();
        }
    }

    private GameInfo? FindGameByPath(string installPath) =>
        Games.FirstOrDefault(item =>
            string.Equals(item.InstallPath, installPath, StringComparison.OrdinalIgnoreCase));

    private void UpdateWatcherSummary(WatchedGamesCoordinator.CheckOutcome outcome)
    {
        // "Needs finishing" unites two sources: watched games whose saving decayed
        // and library cards left partially compressed (an interrupted or cancelled
        // run) — the latter are not watched yet, but they need the same action.
        var partialPaths = Games
            .Where(game => game.CompressionState == GameCompressionState.PartiallyCompressed &&
                           IsResumableAlgorithm(game.CompressionAlgorithm))
            .Select(game => game.InstallPath);
        _degradedPaths = new HashSet<string>(
            outcome.Degraded.Select(item => item.FolderPath).Concat(partialPaths),
            StringComparer.OrdinalIgnoreCase);
        OnPropertyChanged(nameof(DegradedFilterLabel));

        WatcherHasFindings = _degradedPaths.Count > 0;
        if (_degradedPaths.Count > 0)
        {
            var reclaimable = outcome.Degraded.Sum(item => item.PotentialSavingsBytes);
            var savingsNote = reclaimable > 0
                ? string.Format(Strings.Watcher_ReclaimableNote, ByteFormatter.Format(reclaimable))
                : string.Empty;
            WatcherSummaryText = string.Format(Strings.Watcher_NeedsFinishingCount, _degradedPaths.Count) + savingsNote;
        }
        else
        {
            WatcherSummaryText = outcome.WatchedCount == 0
                ? string.Empty
                : string.Format(Strings.Watcher_NoDecay, outcome.WatchedCount);
        }

        // The filter cannot stay on with nothing to show — that would leave an
        // empty library with the toggle already hidden.
        if (_degradedPaths.Count == 0)
            ShowOnlyDegraded = false;
        else if (_showOnlyDegraded)
            GamesView.Refresh();
    }

    private void MarkWatchedGameAsDegraded(WatchedGame entry, GameInfo? libraryGame)
    {
        if (libraryGame is null || libraryGame.CompressionState != GameCompressionState.Compressed)
            return;

        var savedBytes = Math.Max(0, entry.LastUncompressedSize - entry.LastCheckedSize);
        UpdateGameCompressionStatus(
            entry.FolderPath, GameCompressionState.PartiallyCompressed, entry.Algorithm,
            savedBytes, entry.LastCheckedSize, libraryGame.CompressedFileCount, DateTimeOffset.Now);
        TrySaveCompressionStatus(
            entry.FolderPath, GameCompressionState.PartiallyCompressed, entry.Algorithm,
            savedBytes, entry.LastCheckedSize, entry.LastUncompressedSize,
            libraryGame.CompressedFileCount, libraryGame.SteamBuildId);
    }

    // Inverse of MarkWatchedGameAsDegraded: once a check confirms the saving is intact,
    // a card previously demoted to the partial state returns to "compressed" — the
    // library must not offer recompression while the banner says there is nothing to
    // recompress. The algorithm guard keeps a genuinely half-switched game (a new
    // algorithm applied partway) out of the promotion.
    private void MarkWatchedGameAsCompressed(WatchedGame entry, GameInfo? libraryGame)
    {
        if (libraryGame is null ||
            libraryGame.CompressionState != GameCompressionState.PartiallyCompressed ||
            !string.Equals(libraryGame.CompressionAlgorithm, entry.Algorithm, StringComparison.Ordinal))
            return;

        var savedBytes = Math.Max(0, entry.LastUncompressedSize - entry.LastCheckedSize);
        UpdateGameCompressionStatus(
            entry.FolderPath, GameCompressionState.Compressed, entry.Algorithm,
            savedBytes, entry.LastCheckedSize, libraryGame.CompressedFileCount, DateTimeOffset.Now);
        TrySaveCompressionStatus(
            entry.FolderPath, GameCompressionState.Compressed, entry.Algorithm,
            savedBytes, entry.LastCheckedSize, entry.LastUncompressedSize,
            libraryGame.CompressedFileCount, libraryGame.SteamBuildId);
    }

    private bool FilterGame(object item)
    {
        if (item is not GameInfo game)
            return true;
        if (_showOnlyDegraded && !_degradedPaths.Contains(game.InstallPath))
            return false;
        return string.IsNullOrWhiteSpace(_searchText) ||
               game.Name.Contains(_searchText.Trim(), StringComparison.OrdinalIgnoreCase);
    }

    private void ApplySort()
    {
        using (GamesView.DeferRefresh())
        {
            GamesView.SortDescriptions.Clear();
            switch (_selectedSortOption.Id)
            {
                case "largest":
                    GamesView.SortDescriptions.Add(new SortDescription(nameof(GameInfo.LogicalSizeBytes), ListSortDirection.Descending));
                    GamesView.SortDescriptions.Add(new SortDescription(nameof(GameInfo.Name), ListSortDirection.Ascending));
                    break;
                case "uncompressed":
                    GamesView.SortDescriptions.Add(new SortDescription(nameof(GameInfo.CompressionState), ListSortDirection.Ascending));
                    GamesView.SortDescriptions.Add(new SortDescription(nameof(GameInfo.LogicalSizeBytes), ListSortDirection.Descending));
                    break;
                default:
                    GamesView.SortDescriptions.Add(new SortDescription(nameof(GameInfo.Name), ListSortDirection.Ascending));
                    break;
            }
        }
    }

    private static void ShowAbout()
    {
        var dialog = new AboutWindow { Owner = Application.Current.MainWindow };
        dialog.ShowDialog();
    }

    private void ShowSettings()
    {
        var dialog = new SettingsWindow(_userPreferences) { Owner = Application.Current.MainWindow };
        if (dialog.ShowDialog() != true)
            return;

        _userPreferences = dialog.Result with { ExpertMode = IsExpertMode };
        try { _preferences.Save(_userPreferences); }
        catch (Exception exception) { AppLog.Error("Не удалось сохранить настройки", exception); }
        StatusText = Strings.Status_SettingsSaved;
        _ = CheckWatchedGamesAsync(false);
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
            string.Format(Strings.Resume_Prompt, game.Name, latest.Algorithm),
            Strings.Resume_Title,
            MessageBoxButton.YesNo,
            MessageBoxImage.Question);
        if (confirmation != MessageBoxResult.Yes)
            return;

        SelectedGame = game;
        await ExecuteWorkerOperationAsync(new WorkerJob(
            game.InstallPath, "compress", latest.Algorithm,
            CompressionSkipList.BuildEffectiveExtensions(_userPreferences)));
    }

    private async Task RefreshLibraryAsync()
    {
        await ScanLibrariesInternalAsync();
        await LoadCoversAsync(false);
    }

    private async Task ScanLibrariesInternalAsync()
    {
        ScanButtonText = Strings.Scan_InProgress;
        StatusText = Strings.Status_ScanningLibraries;

        try
        {
            var foundGames = await ScanAllLibrariesAsync();
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
                var manualPath = GamePath.Normalize(savedManualGame.InstallPath);
                if (Games.Any(current => string.Equals(current.InstallPath, manualPath, StringComparison.OrdinalIgnoreCase)))
                {
                    // The folder is now found by a launcher scanner, so the manual
                    // record is redundant — drop it to keep manual-games.json clean.
                    try { _manualGameStore.Remove(savedManualGame.InstallPath); }
                    catch (IOException) { }
                    catch (UnauthorizedAccessException) { }
                    continue;
                }
                Games.Add(ApplySavedCompressionStatus(new GameInfo(
                    savedManualGame.Name,
                    manualPath,
                    savedManualGame.LogicalSizeBytes,
                    GameInfo.ManualSource,
                    savedManualGame.SteamAppId)));
            }

            foreach (var manualGame in previousGames.Values.Where(game =>
                         string.Equals(game.Source, GameInfo.ManualSource, StringComparison.OrdinalIgnoreCase) &&
                         Games.All(current => !string.Equals(current.InstallPath, game.InstallPath, StringComparison.OrdinalIgnoreCase))))
                Games.Add(manualGame);

            if (selectedPath is not null)
                SelectedGame = Games.FirstOrDefault(game =>
                    string.Equals(game.InstallPath, selectedPath, StringComparison.OrdinalIgnoreCase));

            StatusText = foundGames.Count == 0
                ? Strings.Status_NoGamesFound
                : string.Format(Strings.Status_GamesFound, foundGames.Count);
            RefreshCoversCommand.RaiseCanExecuteChanged();
            RefreshSavingsSummary();
        }
        catch (Exception exception)
        {
            StatusText = string.Format(Strings.Status_ScanFailed, exception.Message);
        }
        finally
        {
            ScanButtonText = Strings.Scan_Refresh;
        }
    }

    // Each launcher scanner is isolated: a failing or absent source yields an empty
    // list and never blocks the others.
    private async Task<IReadOnlyList<GameInfo>> ScanAllLibrariesAsync()
    {
        var tasks = _gameScanners.Select(async scanner =>
        {
            try { return await scanner.ScanAsync(); }
            catch (Exception exception)
            {
                AppLog.Error($"Сканер {scanner.GetType().Name} не отработал", exception);
                return (IReadOnlyList<GameInfo>)[];
            }
        });
        var results = await Task.WhenAll(tasks);
        return results
            .SelectMany(games => games)
            .DistinctBy(game => game.InstallPath, StringComparer.OrdinalIgnoreCase)
            .OrderBy(game => game.Name, StringComparer.CurrentCultureIgnoreCase)
            .ToArray();
    }

    private async Task AddFolderAsync()
    {
        var dialog = new OpenFolderDialog
        {
            Title = Strings.AddFolder_DialogTitle,
            Multiselect = false
        };

        if (dialog.ShowDialog(Application.Current.MainWindow) != true)
            return;

        var path = GamePath.Normalize(dialog.FolderName);
        StatusText = Strings.Status_DetectingGame;
        var sizeTask = Task.Run(() => _fileTreeService.CalculateLogicalSize(path));
        var identityTask = _gameIdentityService.DetectAsync(path);
        var directStorageTask = Task.Run(() => _directStorageDetector.Detect(path));
        await Task.WhenAll(sizeTask, identityTask, directStorageTask);
        var size = await sizeTask;
        var identity = await identityTask;
        var game = ApplySavedCompressionStatus(
            new GameInfo(identity.Name, path, size, GameInfo.ManualSource, identity.SteamAppId));
        game.HasDirectStorage = await directStorageTask;

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
        StatusText = string.Format(Strings.Status_GameAdded, game.Name);
        RefreshCoversCommand.RaiseCanExecuteChanged();
        RefreshSavingsSummary();
        _ = await LoadCoverAsync(game, false);
    }

    private void RemoveSelectedGame()
    {
        var game = SelectedGame;
        if (game?.Source != GameInfo.ManualSource || IsAnalyzing || IsOperating || IsCheckingCompression)
            return;

        var message = string.Format(Strings.Remove_Prompt, game.Name);
        var isCompressed = game.CompressionState is GameCompressionState.Compressed or GameCompressionState.PartiallyCompressed;
        if (isCompressed)
        {
            message += "\n\n" + Strings.Remove_StillCompressedNote;
        }

        var confirmation = MessageBox.Show(
            Application.Current.MainWindow,
            message,
            Strings.Card_RemoveFromLibrary,
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
        StatusText = string.Format(Strings.Status_GameRemoved, gameName);
        RefreshCoversCommand.RaiseCanExecuteChanged();
        RaiseActionCommands();
        RefreshSavingsSummary();
    }

    private static void ShowRemoveGameError(string details) => MessageBox.Show(
        Application.Current.MainWindow,
        $"{Strings.Remove_Failed}\n\n{details}",
        Strings.Remove_FailedTitle,
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
        if (game is null || game.Source != GameInfo.ManualSource)
            return;

        var dialog = new ManualGameIdentityWindow(game.Name) { Owner = Application.Current.MainWindow };
        if (dialog.ShowDialog() != true)
            return;

        StatusText = Strings.Status_SearchingIdentity;
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
            ? string.Format(Strings.Status_NameSaved, game.Name)
            : string.Format(Strings.Status_GameIdentified, game.Name);
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

        var snapshot = Games.ToArray();
        var dispatcher = Application.Current.Dispatcher;
        var result = await _coverBatchLoader.LoadAsync(
            snapshot,
            forceRefresh,
            async (game, coverPath) => await dispatcher.InvokeAsync(() =>
            {
                var current = Games.FirstOrDefault(item =>
                    string.Equals(item.InstallPath, game.InstallPath, StringComparison.OrdinalIgnoreCase));
                if (current is not null)
                    current.CoverPath = coverPath;
            }),
            async completed => await dispatcher.InvokeAsync(() =>
                StatusText = string.Format(Strings.Status_LoadingCovers, completed, snapshot.Length)));

        StatusText = result.FailureMessage ?? string.Format(Strings.Status_LibraryReady, result.Completed);
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
            StatusText = string.Format(Strings.Status_CoversUnavailable, exception.Message);
            return false;
        }
        catch (TaskCanceledException)
        {
            StatusText = Strings.Covers_Timeout;
            return false;
        }
    }

    private async Task RefreshSelectedCoverAsync()
    {
        var game = SelectedGame;
        if (game is null)
            return;
        StatusText = string.Format(Strings.Status_RefreshingCover, game.Name);
        if (await LoadCoverAsync(game, true))
            StatusText = string.Format(Strings.Status_CoverChecked, game.Name);
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
        SetActiveOperation(string.Format(Strings.Active_Analyzing, game.Name), game.InstallPath);
        OperationProgress = 0;
        AnalysisButtonText = Strings.Analysis_ButtonRunning;
        AnalysisSummary = Strings.Analysis_Inventory;
        OperationSummary = AnalysisSummary;

        var tracker = OperationTracker.BeginAnalysis(
            _operationJournal, UpsertOperation, game.InstallPath, AnalysisSummary);
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

            var wholePercent = (int)Math.Round(OperationProgress);
            var persist = wholePercent >= lastPersistedPercent + 5;
            if (persist)
                lastPersistedPercent = wholePercent;
            tracker.ReportProgress(new WorkerMessage(
                "progress", update.Stage,
                ProcessedBytes: update.ProcessedBytes,
                TotalBytes: update.TotalBytes,
                ProcessedFiles: wholePercent,
                TotalFiles: 100), update.Stage, persist);
        });

        try
        {
            var result = await _analysisService.AnalyzeAsync(
                game,
                progress,
                _analysisCancellation.Token,
                maximumSampleBytes,
                CompressionSkipList.BuildEffectiveSet(_userPreferences));
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
                ? string.Format(Strings.Analysis_DoneSaved, game.Name)
                : string.Format(Strings.Analysis_DoneCacheFailed, game.Name);
            OperationSummary = StatusText;
            tracker.Finish(OperationJournalState.Completed, StatusText, new WorkerMessage(
                "completed", StatusText,
                ProcessedBytes: result.SampleBytes,
                TotalBytes: result.SampleBytes,
                ProcessedFiles: 100,
                TotalFiles: 100));
        }
        catch (OperationCanceledException)
        {
            acceptAnalysisProgress = false;
            OperationSummary = Strings.Analysis_CancelledDetail;
            if (IsGameSelected(game))
                AnalysisSummary = OperationSummary;
            StatusText = Strings.Analysis_Cancelled;
            tracker.Finish(OperationJournalState.Cancelled, OperationSummary);
        }
        catch (Exception exception)
        {
            acceptAnalysisProgress = false;
            OperationSummary = string.Format(Strings.Analysis_Failed, exception.Message);
            if (IsGameSelected(game))
                AnalysisSummary = OperationSummary;
            StatusText = Strings.Analysis_Error;
            tracker.Finish(OperationJournalState.Failed, OperationSummary);
        }
        finally
        {
            acceptAnalysisProgress = false;
            _analysisCancellation.Dispose();
            _analysisCancellation = null;
            IsAnalyzing = false;
            ClearActiveOperation();
            if (IsGameSelected(game))
                AnalysisButtonText = Strings.Analysis_ButtonRepeat;
        }
    }

    private GameInfo ApplySavedCompressionStatus(GameInfo game)
    {
        var saved = _compressionStatusStore.Load(game.InstallPath);
        if (saved is not null)
        {
            if (saved.LogicalBytes > 0)
                game.LogicalSizeBytes = saved.LogicalBytes;
            game.HasDirectStorage = saved.HasDirectStorage;
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

    internal static bool IsResumableAlgorithm(string? algorithm) =>
        WatchedGamesCoordinator.IsResumableAlgorithm(algorithm);

    private static bool ConfirmDirectStorageCompression(GameInfo game)
    {
        if (game.HasDirectStorage != true)
            return true;

        var confirmation = MessageBox.Show(
            Application.Current.MainWindow,
            string.Format(Strings.DirectStorage_ConfirmPrompt, game.Name),
            "DirectStorage",
            MessageBoxButton.YesNo,
            MessageBoxImage.Warning);
        return confirmation == MessageBoxResult.Yes;
    }

    private async Task FinishCompressionAsync()
    {
        var game = SelectedGame;
        if (game is not { CompressionState: GameCompressionState.PartiallyCompressed } ||
            !IsResumableAlgorithm(game.CompressionAlgorithm))
            return;

        if (!ConfirmDirectStorageCompression(game))
            return;

        var confirmation = MessageBox.Show(
            Application.Current.MainWindow,
            string.Format(Strings.Finish_Prompt, game.Name, game.CompressionAlgorithm),
            Strings.Finish_Title,
            MessageBoxButton.YesNo,
            MessageBoxImage.Question);
        if (confirmation != MessageBoxResult.Yes)
            return;

        await ExecuteWorkerOperationAsync(new WorkerJob(
            game.InstallPath, "compress", game.CompressionAlgorithm,
            CompressionSkipList.BuildEffectiveExtensions(_userPreferences)));
    }

    private void OpenSelectedGameFolder()
    {
        var game = SelectedGame;
        if (game is null)
            return;
        if (!Directory.Exists(game.InstallPath))
        {
            StatusText = string.Format(Strings.Status_FolderNotFound, game.InstallPath);
            return;
        }

        try
        {
            using var explorer = Process.Start(new ProcessStartInfo
            {
                FileName = game.InstallPath,
                UseShellExecute = true
            });
            StatusText = string.Format(Strings.Status_OpeningFolder, game.Name);
        }
        catch (Win32Exception exception)
        {
            StatusText = string.Format(Strings.Status_ExplorerFailed, exception.Message);
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

    private void RefreshAllTimeStats(CompressionAllTimeStats? stats = null)
    {
        var current = stats ?? _statsStore.Load();
        if (current.Operations == 0)
        {
            AllTimeStatsText = string.Empty;
            AllTimeStatsTooltip = string.Empty;
            return;
        }

        AllTimeStatsText = string.Format(Strings.Stats_AllTime, ByteFormatter.Format(current.FreedBytes));
        var since = current.FirstOperationAt is { } first
            ? string.Format(Strings.Stats_SinceDate, $"{first:dd.MM.yyyy}")
            : string.Empty;
        var driveLines = current.Drives
            .OrderByDescending(pair => pair.Value.FreedBytes)
            .Select(pair => string.Format(
                Strings.Stats_DriveLine, pair.Key, ByteFormatter.Format(pair.Value.FreedBytes), pair.Value.Operations));
        AllTimeStatsTooltip =
            string.Format(Strings.Stats_TooltipHeader, since) + "\n" +
            string.Format(Strings.Stats_OperationsCount, current.Operations) + "\n\n" +
            string.Join("\n", driveLines);
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
            ? string.Format(Strings.Stats_TotalSaved, ByteFormatter.Format(totalSavedBytes))
            : string.Empty;
    }

    private static string DescribeCompressionState(GameInfo game) => game.CompressionState switch
    {
        GameCompressionState.Compressed => string.Format(Strings.State_AlreadyCompressed, game.Name, game.CompressionAlgorithm ?? "Windows"),
        GameCompressionState.PartiallyCompressed => string.Format(Strings.State_PartiallyCompressed, game.Name, game.CompressionAlgorithm ?? "Windows"),
        GameCompressionState.Uncompressed => string.Format(Strings.State_NotCompressed, game.Name),
        _ => string.Format(Strings.State_NotChecked, game.Name)
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
        StatusText = string.Format(Strings.Status_CheckingState, game.Name);
        try
        {
            var detected = await _compressionDetector.DetectAsync(game.InstallPath, cancellationToken);
            var hasDirectStorage = await Task.Run(
                () => _directStorageDetector.Detect(game.InstallPath, cancellationToken), cancellationToken);
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
                detected.LogicalBytes, hasDirectStorage);
            TrySaveCompressionStatus(
                game.InstallPath, state, detected.Algorithm,
                detected.SavedBytes, detected.PhysicalBytes, detected.LogicalBytes, detected.CompressedFiles,
                buildChanged ? savedStatus?.SteamBuildId : game.SteamBuildId, hasDirectStorage);
            StatusText = state switch
            {
                GameCompressionState.Compressed => string.Format(Strings.State_AlreadyCompressed, game.Name, detected.Algorithm ?? "Windows"),
                GameCompressionState.PartiallyCompressed => buildChanged
                    ? string.Format(Strings.State_UpdatedNeedsWork, game.Name)
                    : string.Format(Strings.State_PartiallyCompressed, game.Name, detected.Algorithm ?? "Windows"),
                _ => string.Format(Strings.State_NotCompressed, game.Name)
            };
        }
        catch (OperationCanceledException) { }
        catch (Exception exception)
        {
            StatusText = string.Format(Strings.Status_StateCheckFailed, exception.Message);
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
        long logicalBytes = 0,
        bool? hasDirectStorage = null)
    {
        var index = FindGameIndex(installPath);
        if (index >= 0)
        {
            if (logicalBytes > 0)
                Games[index].LogicalSizeBytes = logicalBytes;
            if (hasDirectStorage is not null)
                Games[index].HasDirectStorage = hasDirectStorage;
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
        OnPropertyChanged(nameof(PartialPanelVisibility));
        OnPropertyChanged(nameof(PartialInfoVisibility));
        OnPropertyChanged(nameof(FinishCompressionButtonText));
        OnPropertyChanged(nameof(DirectStorageWarningVisibility));
    }

    private void TrySaveCompressionStatus(
        string installPath,
        GameCompressionState state,
        string? algorithm,
        long savedBytes = 0,
        long physicalBytes = 0,
        long logicalBytes = 0,
        int compressedFiles = 0,
        string? steamBuildId = null,
        bool? hasDirectStorage = null)
    {
        try
        {
            _compressionStatusStore.Save(new SavedCompressionStatus(
                installPath, state, algorithm, DateTimeOffset.Now,
                savedBytes, physicalBytes, logicalBytes, compressedFiles, steamBuildId, hasDirectStorage));
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
        RefreshSelectedCoverCommand.RaiseCanExecuteChanged();
        FinishCompressionCommand.RaiseCanExecuteChanged();
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
            ? Strings.Analysis_StaleHint
            : BuildSavedAnalysisSummary(saved.Result, saved.AnalyzedAt);
        AnalysisButtonText = Strings.Analysis_ButtonRepeat;
        StatusText = string.Format(Strings.Status_AnalysisLoaded, game.Name);
    }

    private static string BuildSavedAnalysisSummary(GameAnalysisResult result, DateTimeOffset analyzedAt) =>
        string.Format(
            Strings.Analysis_SavedSummary,
            $"{analyzedAt:dd.MM.yyyy HH:mm}",
            $"{result.FileCount:N0}",
            ByteFormatter.Format(result.SampleBytes),
            ByteFormatter.Format(result.CurrentPhysicalBytes));

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

        if (!ConfirmDirectStorageCompression(game))
            return;

        var confirmation = MessageBox.Show(
            Application.Current.MainWindow,
            string.Format(Strings.Compress_Prompt,
                game.Name, estimate.AlgorithmText, estimate.EstimatedSizeText, estimate.SavingsText),
            Strings.Compress_Title,
            MessageBoxButton.YesNo,
            MessageBoxImage.Warning);
        if (confirmation != MessageBoxResult.Yes)
            return;

        await ExecuteWorkerOperationAsync(new WorkerJob(
            game.InstallPath, "compress", estimate.AlgorithmText,
            CompressionSkipList.BuildEffectiveExtensions(_userPreferences)));
    }

    private async Task DecompressSelectedGameAsync()
    {
        var game = SelectedGame;
        if (game is null)
            return;

        var confirmation = MessageBox.Show(
            Application.Current.MainWindow,
            string.Format(Strings.Decompress_Prompt, game.Name),
            Strings.Decompress_Title,
            MessageBoxButton.YesNo,
            MessageBoxImage.Question);
        if (confirmation != MessageBoxResult.Yes)
            return;

        await ExecuteWorkerOperationAsync(new WorkerJob(game.InstallPath, "decompress", null));
    }

    // A single operation is a queue of one: the same elevated session API serves both.
    private async Task ExecuteWorkerOperationAsync(WorkerJob job)
    {
        IsOperating = true;
        try
        {
            await using var session = await _workerClient.StartSessionAsync();
            await RunJobInSessionAsync(session, job);
        }
        catch (OperationCanceledException exception)
        {
            OperationSummary = exception.Message;
            StatusText = Strings.Operation_Cancelled;
        }
        catch (Exception exception)
        {
            OperationSummary = string.Format(Strings.Operation_Failed, exception.Message);
            StatusText = Strings.Operation_SystemError;
        }
        finally
        {
            IsOperating = false;
            ClearActiveOperation();
        }
    }

    private sealed record QueueJobOutcome(
        QueueItemStatus Status,
        long FreedBytes,
        bool SessionLost,
        string? FailureReason = null);

    // Runs one job inside an already-started session and fully accounts for it:
    // journal entry, card status, watcher baseline, summary text. Never throws —
    // a lost session is reported through the outcome so a queue can stop cleanly.
    private async Task<QueueJobOutcome> RunJobInSessionAsync(WorkerSession session, WorkerJob job)
    {
        var tracker = OperationTracker.Begin(_operationJournal, UpsertOperation, job, Strings.Journal_WaitingWorker);
        var acceptWorkerProgress = true;

        var targetGame = Games.FirstOrDefault(item =>
            string.Equals(item.InstallPath, job.RootPath, StringComparison.OrdinalIgnoreCase));
        SetActiveOperation(
            string.Format(
                job.Operation == "compress" ? Strings.Active_Compressing : Strings.Active_Decompressing,
                targetGame?.Name ?? job.RootPath),
            job.RootPath);
        var selectedEstimate = SelectedEstimate;
        _activeCompressionAlgorithm = job.Operation == "compress" ? job.Algorithm : null;
        _activeCompressionSavings = job.Operation == "compress" && selectedEstimate is not null &&
                                    selectedEstimate.AlgorithmText == job.Algorithm
            ? selectedEstimate.SavingsText
            : null;
        NotifyActiveCompressionInfo();
        OperationProgress = 0;
        OperationSummary = Strings.Operation_WaitingUac;
        StatusText = OperationSummary;

        var progress = new Progress<WorkerMessage>(message =>
        {
            if (!acceptWorkerProgress)
                return;
            if (message.TotalBytes > 0)
                OperationProgress = Math.Clamp(message.ProcessedBytes * 100d / message.TotalBytes, 0, 100);
            var counters = message.TotalFiles > 0
                ? string.Format(Strings.Operation_FilesCounter, $"{message.ProcessedFiles:N0}", $"{message.TotalFiles:N0}")
                : string.Empty;
            OperationSummary = $"{message.Text}{counters}";
            StatusText = OperationSummary;
            tracker.ReportProgress(message, OperationSummary);
        });

        try
        {
            var result = await session.RunJobAsync(job, progress);
            acceptWorkerProgress = false;
            if (result.Type == "error")
            {
                OperationSummary = string.Format(Strings.Operation_Failed, result.Text ?? Strings.Worker_Error);
                StatusText = Strings.Operation_SystemError;
                tracker.Finish(OperationJournalState.Failed, OperationSummary);
                return new QueueJobOutcome(QueueItemStatus.Failed, 0, false, result.Text);
            }
            if (result.Type == "cancelled")
            {
                OperationSummary = result.Text ?? Strings.Operation_Cancelled;
                StatusText = OperationSummary;
                tracker.Finish(OperationJournalState.Cancelled, OperationSummary, result);
                // A cancelled run leaves the game part-converted at the NTFS level, so
                // the card switches to the partial state and offers "Дожать" instead of
                // silently keeping the pre-operation badge. Sizes are unknown here —
                // the next status check refreshes them.
                var cancelledAlgorithm = job.Operation == "compress"
                    ? job.Algorithm
                    : targetGame?.CompressionAlgorithm;
                if (cancelledAlgorithm is not null)
                {
                    UpdateGameCompressionStatus(
                        job.RootPath, GameCompressionState.PartiallyCompressed, cancelledAlgorithm,
                        checkedAt: DateTimeOffset.Now);
                    TrySaveCompressionStatus(
                        job.RootPath, GameCompressionState.PartiallyCompressed, cancelledAlgorithm,
                        steamBuildId: targetGame?.SteamBuildId, hasDirectStorage: targetGame?.HasDirectStorage);
                    // The card just turned partial, so the needs-finishing banner and
                    // filter reflect it immediately, same as after a completed job.
                    if (_userPreferences.WatcherEnabled && !_isWatcherCheckRunning)
                        UpdateWatcherSummary(_watcher.ReadStoredState(_userPreferences));
                }
                return new QueueJobOutcome(QueueItemStatus.Cancelled, 0, false);
            }

            OperationProgress = 100;
            var processedGame = Games.FirstOrDefault(game =>
                string.Equals(game.InstallPath, job.RootPath, StringComparison.OrdinalIgnoreCase));
            var decompressIncomplete = job.Operation == "decompress" && result.ErrorCount > 0;
            // Files that compact.exe legitimately leaves uncompressed (locked, incompressible)
            // should not pin a game to the partial state forever, so the outcome is graded
            // by the byte share of problem files, matching the detector thresholds.
            var newState = job.Operation == "compress"
                ? (result.ErrorCount == 0
                    ? GameCompressionState.Compressed
                    : GameCompressionDetector.ClassifyState(
                        Math.Max(0, result.TotalBytes - result.ErrorBytes), result.TotalBytes))
                : decompressIncomplete
                    ? GameCompressionDetector.ClassifyState(result.ErrorBytes, result.TotalBytes)
                    : GameCompressionState.Uncompressed;
            var newAlgorithm = newState == GameCompressionState.Uncompressed
                ? null
                : job.Operation == "compress" ? job.Algorithm : processedGame?.CompressionAlgorithm;
            var difference = result.PhysicalBefore - result.PhysicalAfter;
            // The stored saving is the game's CURRENT saving versus its uncompressed
            // size (detector semantics), not the delta of this run: a resumed or repeat
            // compression frees ~0 additional bytes but the game still saves gigabytes.
            var savedBytes = job.Operation == "compress"
                ? Math.Max(0, result.TotalBytes - result.PhysicalAfter)
                : 0;
            var compressedFiles = job.Operation == "compress"
                ? result.ProcessedFiles
                : newState == GameCompressionState.Uncompressed ? 0 : result.ErrorCount;
            // The worker's TotalBytes/PhysicalAfter cover only files it can process —
            // sparse and encrypted ones are outside its universe but still occupy the
            // disk. The card shows the sizes the user compares with Explorer, so the
            // excluded physical bytes are added back and the logical size keeps the
            // larger known measure.
            var displayLogicalBytes = Math.Max(processedGame?.LogicalSizeBytes ?? 0, result.TotalBytes);
            var displayPhysicalBytes = result.PhysicalAfter + result.ExcludedPhysicalBytes;
            UpdateGameCompressionStatus(
                job.RootPath, newState, newAlgorithm,
                savedBytes, displayPhysicalBytes, compressedFiles, DateTimeOffset.Now,
                displayLogicalBytes);
            TrySaveCompressionStatus(
                job.RootPath, newState, newAlgorithm,
                savedBytes, displayPhysicalBytes, displayLogicalBytes, compressedFiles,
                processedGame?.SteamBuildId, processedGame?.HasDirectStorage);
            _watcher.OnOperationCompleted(job, result, newState, processedGame);
            // The job just updated watcher.json (recompression resets the baseline,
            // decompression removes the entry), so the degradation banner refreshes
            // immediately instead of waiting for the next manual check.
            if (_userPreferences.WatcherEnabled && !_isWatcherCheckRunning)
                UpdateWatcherSummary(_watcher.ReadStoredState(_userPreferences));

            var skipNote = result.SkipListedFiles > 0
                ? string.Format(Strings.Operation_SkippedNote, $"{result.SkipListedFiles:N0}", ByteFormatter.Format(result.SkipListedBytes))
                : string.Empty;
            // ErrorCount are files compact.exe could not convert (locked by another
            // process, or no WOF benefit outside our exclusions) — the operation itself
            // succeeded, so the wording avoids the scary word "errors".
            var leftoverNote = result.ErrorCount > 0
                ? string.Format(Strings.Operation_LeftoverNote, $"{result.ErrorCount:N0}")
                : string.Empty;
            OperationSummary = job.Operation == "compress"
                ? string.Format(Strings.Operation_CompressDone,
                      ByteFormatter.Format(Math.Max(0, difference)), ByteFormatter.Format(savedBytes)) + leftoverNote + skipNote
                : decompressIncomplete
                    ? string.Format(Strings.Operation_DecompressPartial, $"{result.ErrorCount:N0}")
                    : string.Format(Strings.Operation_DecompressDone, $"{result.ProcessedFiles:N0}");
            StatusText = OperationSummary;
            Computer = _computerInfoService.GetComputerInfo();
            RefreshSavingsSummary();
            if (job.Operation == "compress")
                RefreshAllTimeStats(_statsStore.RecordCompression(
                    Path.GetPathRoot(job.RootPath) ?? string.Empty, Math.Max(0, difference)));
            tracker.Finish(OperationJournalState.Completed, OperationSummary, result);
            return new QueueJobOutcome(QueueItemStatus.Completed, Math.Max(0, difference), false);
        }
        catch (Exception exception)
        {
            // The pipe died mid-job (worker crashed or was killed) — the session is
            // unusable, which the outcome reports so a queue stops instead of feeding
            // jobs into a dead process.
            acceptWorkerProgress = false;
            OperationSummary = string.Format(Strings.Operation_Failed, exception.Message);
            StatusText = Strings.Operation_SystemError;
            tracker.Finish(OperationJournalState.Failed, OperationSummary);
            return new QueueJobOutcome(QueueItemStatus.Failed, 0, true, exception.Message);
        }
        finally
        {
            acceptWorkerProgress = false;
        }
    }

    private void ToggleMultiSelectMode() => IsMultiSelectMode = !IsMultiSelectMode;

    private void AddSelectedGameToQueue()
    {
        if (SelectedGame is null)
            return;
        IsMultiSelectMode = true;
        SelectedGame.IsQueueSelected = true;
    }

    private void ClearQueueSelection()
    {
        foreach (var game in Games)
            game.IsQueueSelected = false;
    }

    private void OnGamesCollectionChanged(object? sender, NotifyCollectionChangedEventArgs e)
    {
        if (e.NewItems is not null)
        {
            foreach (GameInfo game in e.NewItems)
                game.PropertyChanged += OnGamePropertyChanged;
        }
        UpdateQueueSelectionSummary();
        ToggleMultiSelectCommand.RaiseCanExecuteChanged();
    }

    private void OnGamePropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName != nameof(GameInfo.IsQueueSelected))
            return;
        UpdateQueueSelectionSummary();
        StartQueueCommand.RaiseCanExecuteChanged();
    }

    private void UpdateQueueSelectionSummary()
    {
        var selected = Games.Where(game => game.IsQueueSelected).ToList();
        QueueSelectionText = selected.Count == 0
            ? Strings.MultiSelect_Hint
            : string.Format(Strings.MultiSelect_Selected, selected.Count, ByteFormatter.Format(selected.Sum(game => game.LogicalSizeBytes)));
    }

    private string ResolveQueueAlgorithm(GameInfo game)
    {
        if (SelectedQueueMethod.Id != AutoQueueMethod)
            return SelectedQueueMethod.Id;
        // A partially compressed game keeps its current algorithm — mixing methods
        // inside one game is exactly the mess the queue must not create.
        if (game.CompressionState == GameCompressionState.PartiallyCompressed &&
            IsResumableAlgorithm(game.CompressionAlgorithm))
            return game.CompressionAlgorithm!;
        var saved = _analysisCache.Load(game.InstallPath);
        var balanced = saved is null ? null : ChooseBalancedEstimate(saved.Result.Estimates);
        return balanced?.AlgorithmText ?? "XPRESS16K";
    }

    private async Task StartQueueAsync()
    {
        var selected = Games.Where(game => game.IsQueueSelected).ToList();
        if (selected.Count == 0)
            return;

        var directStorage = selected.Where(game => game.HasDirectStorage == true && !IsExpertMode).ToList();
        var items = selected.Except(directStorage)
            .Select(game => new CompressionQueueItem(game, ResolveQueueAlgorithm(game)))
            .ToList();
        if (items.Count == 0)
        {
            StatusText = Strings.Queue_AllDirectStorage;
            return;
        }

        var preview = string.Join("\n", items.Take(12).Select(item => $"• {item.Title}"));
        if (items.Count > 12)
            preview += "\n" + string.Format(Strings.Queue_AndMore, items.Count - 12);
        var directStorageNote = directStorage.Count > 0
            ? "\n\n" + string.Format(Strings.Queue_DirectStorageSkipped, directStorage.Count)
            : string.Empty;
        var confirmation = MessageBox.Show(
            Application.Current.MainWindow,
            string.Format(Strings.Queue_StartPrompt, items.Count, preview + directStorageNote),
            Strings.Queue_StartTitle,
            MessageBoxButton.YesNo,
            MessageBoxImage.Warning);
        if (confirmation != MessageBoxResult.Yes)
            return;

        IsMultiSelectMode = false;
        await RunQueueAsync(items);
    }

    private async Task RecompressDegradedAsync()
    {
        var degraded = _watcher.ReadStoredState(_userPreferences).Degraded;
        var items = new List<CompressionQueueItem>();
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var entry in degraded)
        {
            var game = FindGameByPath(entry.FolderPath);
            if (game is not null && seen.Add(game.InstallPath) &&
                !(game.HasDirectStorage == true && !IsExpertMode))
                items.Add(new CompressionQueueItem(game, entry.Algorithm));
        }
        // Interrupted and cancelled runs leave partially compressed cards that are
        // not watched yet — "recompress all" must cover them too.
        foreach (var game in Games)
        {
            if (game.CompressionState == GameCompressionState.PartiallyCompressed &&
                IsResumableAlgorithm(game.CompressionAlgorithm) &&
                seen.Add(game.InstallPath) &&
                !(game.HasDirectStorage == true && !IsExpertMode))
                items.Add(new CompressionQueueItem(game, game.CompressionAlgorithm!));
        }
        if (items.Count == 0)
        {
            StatusText = Strings.Queue_NoDegradedGames;
            return;
        }

        var preview = string.Join("\n", items.Take(12).Select(item => $"• {item.Title}"));
        var confirmation = MessageBox.Show(
            Application.Current.MainWindow,
            string.Format(Strings.Queue_FinishPrompt, items.Count, preview),
            Strings.Watcher_RecompressAll,
            MessageBoxButton.YesNo,
            MessageBoxImage.Question);
        if (confirmation != MessageBoxResult.Yes)
            return;

        await RunQueueAsync(items);
    }

    private async Task StopQueueAsync()
    {
        _queueStopAll = true;
        await _workerClient.CancelAsync();
    }

    private async Task RunQueueAsync(IReadOnlyList<CompressionQueueItem> items)
    {
        QueueItems.Clear();
        foreach (var item in items)
            QueueItems.Add(item);
        _queueStopAfterCurrent = false;
        _queueStopAll = false;
        IsOperating = true;
        IsQueueRunning = true;
        QueueTitle = string.Format(Strings.Queue_TitlePreparing, items.Count);
        AppLog.Info($"Очередь запущена: {items.Count} игр — {string.Join(", ", items.Select(item => item.Title))}");
        try
        {
            await using var session = await _workerClient.StartSessionAsync();
            var position = 0;
            foreach (var item in items)
            {
                position++;
                if (_queueStopAll || _queueStopAfterCurrent)
                {
                    item.MarkSkipped(Strings.QueueItem_Stopped);
                    continue;
                }

                QueueTitle = string.Format(Strings.Queue_TitleProgress, position, items.Count);
                item.MarkRunning();
                AppLog.Info($"Очередь: {position}/{items.Count} — запускаем «{item.Game.Name}» ({item.Algorithm})");
                var outcome = await RunJobInSessionAsync(session, new WorkerJob(
                    item.Game.InstallPath, "compress", item.Algorithm,
                    CompressionSkipList.BuildEffectiveExtensions(_userPreferences)));
                AppLog.Info($"Очередь: «{item.Game.Name}» — итог {outcome.Status}" +
                            (outcome.SessionLost ? " (сессия воркера потеряна)" : string.Empty));
                switch (outcome.Status)
                {
                    case QueueItemStatus.Completed:
                        item.MarkCompleted(outcome.FreedBytes, string.Format(Strings.QueueItem_Done, ByteFormatter.Format(outcome.FreedBytes)));
                        break;
                    case QueueItemStatus.Cancelled:
                        item.MarkCancelled();
                        break;
                    default:
                        item.MarkFailed(outcome.FailureReason ?? Strings.QueueItem_Failed);
                        break;
                }

                if (outcome.SessionLost)
                    _queueStopAll = true;
            }
        }
        catch (OperationCanceledException exception)
        {
            OperationSummary = exception.Message;
            StatusText = Strings.Operation_Cancelled;
            AppLog.Info($"Очередь: отменена — {exception.Message}");
        }
        catch (Exception exception)
        {
            OperationSummary = string.Format(Strings.Queue_Aborted, exception.Message);
            StatusText = Strings.Operation_SystemError;
            AppLog.Error("Очередь прервана", exception);
        }
        finally
        {
            foreach (var item in QueueItems)
            {
                if (item.Status is QueueItemStatus.Pending or QueueItemStatus.Running)
                    item.MarkSkipped(Strings.QueueItem_Stopped);
            }
            IsQueueRunning = false;
            IsOperating = false;
            ClearActiveOperation();
            var completedCount = QueueItems.Count(item => item.Status == QueueItemStatus.Completed);
            var freedBytes = QueueItems.Sum(item => item.FreedBytes);
            QueueTitle = string.Format(Strings.Queue_TitleDone, completedCount, QueueItems.Count, ByteFormatter.Format(freedBytes));
            OperationSummary = QueueTitle;
            StatusText = QueueTitle;
            AppLog.Info(QueueTitle);
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
