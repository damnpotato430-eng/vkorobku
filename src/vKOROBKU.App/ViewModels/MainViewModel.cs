using System.Collections.ObjectModel;
using System.Net.Http;
using System.Windows;
using Microsoft.Win32;
using vKOROBKU.App.Models;
using vKOROBKU.App.Services;
using vKOROBKU.Protocol;

namespace vKOROBKU.App.ViewModels;

public sealed class MainViewModel : ObservableObject
{
    private readonly SteamLibraryScanner _steamScanner = new();
    private readonly ComputerInfoService _computerInfoService = new();
    private readonly FileTreeService _fileTreeService = new();
    private readonly GameAnalysisService _analysisService = new();
    private readonly IgdbCredentialStore _igdbCredentialStore = new();
    private readonly IgdbCoverService _coverService;
    private readonly CompressionWorkerClient _workerClient = new();
    private readonly AnalysisCacheStore _analysisCache = new();
    private readonly CompressionStatusStore _compressionStatusStore = new();
    private readonly GameCompressionDetector _compressionDetector = new();
    private GameInfo? _selectedGame;
    private CompressionEstimate? _selectedEstimate;
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
    private double _operationProgress;
    private string _operationSummary = "Сжатие изменяет только способ хранения файлов на NTFS.";

    public MainViewModel()
    {
        Computer = _computerInfoService.GetComputerInfo();
        _coverService = new IgdbCoverService(_igdbCredentialStore);
        AnalysisModes.Add(new AnalysisModeOption("Авто", "512 МБ–2 ГБ по размеру игры", 0));
        AnalysisModes.Add(new AnalysisModeOption("Быстрый", "до 512 МБ", 512L * 1024 * 1024));
        AnalysisModes.Add(new AnalysisModeOption("Точный", "до 1 ГБ", 1024L * 1024 * 1024));
        AnalysisModes.Add(new AnalysisModeOption("Максимальный", "до 2 ГБ", 2L * 1024 * 1024 * 1024));
        SelectedAnalysisMode = AnalysisModes[2];
        ScanSteamCommand = new AsyncRelayCommand(RefreshSteamLibraryAsync);
        AddFolderCommand = new AsyncRelayCommand(AddFolderAsync);
        ConfigureIgdbCommand = new AsyncRelayCommand(ConfigureIgdbAsync);
        RefreshCoversCommand = new AsyncRelayCommand(() => LoadCoversAsync(true), () => Games.Count > 0 && _coverService.HasCredentials);
        AnalyzeCommand = new AsyncRelayCommand(AnalyzeSelectedGameAsync,
            () => SelectedGame is { CompressionState: not GameCompressionState.Compressed } && !IsAnalyzing && !IsOperating && !IsCheckingCompression);
        CancelAnalysisCommand = new RelayCommand(CancelAnalysis, () => IsAnalyzing);
        CompressCommand = new AsyncRelayCommand(CompressSelectedGameAsync,
            () => SelectedGame is { CompressionState: not GameCompressionState.Compressed } && SelectedEstimate is not null && !IsAnalyzing && !IsOperating && !IsCheckingCompression);
        DecompressCommand = new AsyncRelayCommand(DecompressSelectedGameAsync,
            () => SelectedGame is { CompressionState: GameCompressionState.Compressed } && !IsAnalyzing && !IsOperating && !IsCheckingCompression);
        CancelOperationCommand = new AsyncRelayCommand(_workerClient.CancelAsync, () => IsOperating);
    }

    public ObservableCollection<GameInfo> Games { get; } = [];
    public ObservableCollection<CompressionEstimate> Estimates { get; } = [];
    public ObservableCollection<AnalysisModeOption> AnalysisModes { get; } = [];
    public ComputerInfo Computer { get; }
    public AsyncRelayCommand ScanSteamCommand { get; }
    public AsyncRelayCommand AddFolderCommand { get; }
    public AsyncRelayCommand ConfigureIgdbCommand { get; }
    public AsyncRelayCommand RefreshCoversCommand { get; }
    public AsyncRelayCommand AnalyzeCommand { get; }
    public RelayCommand CancelAnalysisCommand { get; }
    public AsyncRelayCommand CompressCommand { get; }
    public AsyncRelayCommand DecompressCommand { get; }
    public AsyncRelayCommand CancelOperationCommand { get; }

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
            if (sameGame)
                return;
            Estimates.Clear();
            SelectedEstimate = null;
            AnalysisButtonText = "Рассчитать экономию";
            AnalysisSummary = value is null
                ? "Выберите игру и запустите безопасный анализ выборки."
                : $"Будет исследована репрезентативная выборка без изменения файлов игры «{value.Name}».";
            if (value is not null)
                RestoreSavedAnalysis(value);
            AnalyzeCommand.RaiseCanExecuteChanged();
            RaiseActionCommands();
            _compressionCheckCancellation?.Cancel();
            _compressionCheckCancellation?.Dispose();
            _compressionCheckCancellation = null;
            if (value is not null)
            {
                _compressionCheckCancellation = new CancellationTokenSource();
                _ = RefreshCompressionStatusAsync(value, _compressionCheckCancellation.Token);
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
        }
    }

    public Visibility UncompressedPanelVisibility =>
        SelectedGame?.CompressionState == GameCompressionState.Compressed ? Visibility.Collapsed : Visibility.Visible;

    public Visibility CompressedPanelVisibility =>
        SelectedGame?.CompressionState == GameCompressionState.Compressed ? Visibility.Visible : Visibility.Collapsed;

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

    public async Task InitializeAsync() => await RefreshSteamLibraryAsync();

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
        StatusText = "Рассчитываем размер выбранной игры…";
        var size = await Task.Run(() => _fileTreeService.CalculateLogicalSize(path));
        var game = ApplySavedCompressionStatus(
            new GameInfo(new DirectoryInfo(path).Name, path, size, "Добавлено вручную"));

        var existing = Games.FirstOrDefault(item =>
            string.Equals(item.InstallPath, path, StringComparison.OrdinalIgnoreCase));
        if (existing is not null)
            Games.Remove(existing);

        Games.Add(game);
        SelectedGame = game;
        StatusText = $"Добавлена игра «{game.Name}»";
        RefreshCoversCommand.RaiseCanExecuteChanged();
        _ = await LoadCoverAsync(game, false);
    }

    private async Task ConfigureIgdbAsync()
    {
        var dialog = new IgdbSettingsWindow { Owner = Application.Current.MainWindow };
        if (dialog.ShowDialog() != true)
            return;

        RefreshCoversCommand.RaiseCanExecuteChanged();
        await LoadCoversAsync(true);
    }

    private async Task LoadCoversAsync(bool forceRefresh)
    {
        if (Games.Count == 0)
            return;

        var snapshot = Games.ToArray();
        var loaded = 0;
        foreach (var game in snapshot)
        {
            StatusText = $"Загрузка обложек: {loaded + 1} из {snapshot.Length}";
            if (!await LoadCoverAsync(game, forceRefresh))
                break;
            loaded++;
        }

        if (loaded < snapshot.Length)
            return;

        StatusText = _coverService.HasCredentials
            ? $"Библиотека готова · проверено обложек: {loaded}"
            : "Обложки Steam и локальный кэш загружены · IGDB доступен для остальных игр";
    }

    private async Task<bool> LoadCoverAsync(GameInfo game, bool forceRefresh)
    {
        try
        {
            var coverPath = await _coverService.GetCoverAsync(game, forceRefresh);
            if (coverPath is null)
                return true;

            var index = Games.ToList().FindIndex(item =>
                string.Equals(item.InstallPath, game.InstallPath, StringComparison.OrdinalIgnoreCase));
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

    private async Task AnalyzeSelectedGameAsync()
    {
        var game = SelectedGame;
        if (game is null)
            return;

        Estimates.Clear();
        _analysisCancellation = new CancellationTokenSource();
        IsAnalyzing = true;
        AnalysisButtonText = "Анализ выполняется…";
        AnalysisSummary = "Инвентаризация файлов…";
        var progress = new Progress<string>(message =>
        {
            AnalysisSummary = message;
            StatusText = message;
        });

        try
        {
            var result = await _analysisService.AnalyzeAsync(
                game,
                progress,
                _analysisCancellation.Token,
                SelectedAnalysisMode?.MaximumSampleBytes ?? 0);
            foreach (var estimate in result.Estimates)
                Estimates.Add(estimate);
            SelectedEstimate = Estimates.FirstOrDefault(estimate => estimate.Algorithm == CompressionAlgorithm.Xpress16K);

            var analyzedAt = DateTimeOffset.Now;
            var cacheSaved = true;
            try
            {
                _analysisCache.Save(new SavedGameAnalysis(game.InstallPath, analyzedAt, result));
            }
            catch (IOException)
            {
                cacheSaved = false;
            }
            catch (UnauthorizedAccessException)
            {
                cacheSaved = false;
            }

            AnalysisSummary = BuildSavedAnalysisSummary(result, analyzedAt);
            StatusText = cacheSaved
                ? $"Анализ игры «{game.Name}» завершён и сохранён"
                : $"Анализ игры «{game.Name}» завершён, но кэш записать не удалось";
        }
        catch (OperationCanceledException)
        {
            AnalysisSummary = "Анализ отменён. Временные файлы удалены.";
            StatusText = "Анализ отменён";
        }
        catch (Exception exception)
        {
            AnalysisSummary = $"Анализ не выполнен: {exception.Message}";
            StatusText = "Ошибка анализа";
        }
        finally
        {
            _analysisCancellation.Dispose();
            _analysisCancellation = null;
            IsAnalyzing = false;
            AnalysisButtonText = "Повторить анализ";
        }
    }

    private GameInfo ApplySavedCompressionStatus(GameInfo game)
    {
        var saved = _compressionStatusStore.Load(game.InstallPath);
        if (saved is not null)
        {
            game.CompressionState = saved.State;
            game.CompressionAlgorithm = saved.Algorithm;
            game.CompressionSavedBytes = saved.SavedBytes;
            game.CompressedPhysicalBytes = saved.PhysicalBytes;
            game.CompressedFileCount = saved.CompressedFiles;
            game.CompressionCheckedAt = saved.CheckedAt;
        }
        return game;
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

            UpdateGameCompressionStatus(
                game.InstallPath, detected.State, detected.Algorithm,
                detected.SavedBytes, detected.PhysicalBytes, detected.CompressedFiles, DateTimeOffset.Now);
            TrySaveCompressionStatus(
                game.InstallPath, detected.State, detected.Algorithm,
                detected.SavedBytes, detected.PhysicalBytes, detected.LogicalBytes, detected.CompressedFiles);
            StatusText = detected.State == GameCompressionState.Compressed
                ? $"Игра «{game.Name}» уже сжата: {detected.Algorithm ?? "Windows"}"
                : $"Игра «{game.Name}» не сжата";
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
        DateTimeOffset? checkedAt = null)
    {
        var index = Games.ToList().FindIndex(item =>
            string.Equals(item.InstallPath, installPath, StringComparison.OrdinalIgnoreCase));
        if (index >= 0)
        {
            Games[index].CompressionState = state;
            Games[index].CompressionAlgorithm = algorithm;
            Games[index].CompressionSavedBytes = savedBytes;
            Games[index].CompressedPhysicalBytes = physicalBytes;
            Games[index].CompressedFileCount = compressedFiles;
            Games[index].CompressionCheckedAt = checkedAt ?? DateTimeOffset.Now;
        }
        NotifyCompressionPanelVisibility();
        RaiseActionCommands();
    }

    private void NotifyCompressionPanelVisibility()
    {
        OnPropertyChanged(nameof(UncompressedPanelVisibility));
        OnPropertyChanged(nameof(CompressedPanelVisibility));
    }

    private void TrySaveCompressionStatus(
        string installPath,
        GameCompressionState state,
        string? algorithm,
        long savedBytes = 0,
        long physicalBytes = 0,
        long logicalBytes = 0,
        int compressedFiles = 0)
    {
        try
        {
            _compressionStatusStore.Save(new SavedCompressionStatus(
                installPath, state, algorithm, DateTimeOffset.Now,
                savedBytes, physicalBytes, logicalBytes, compressedFiles));
        }
        catch (IOException) { }
        catch (UnauthorizedAccessException) { }
    }

    private void RaiseActionCommands()
    {
        AnalyzeCommand.RaiseCanExecuteChanged();
        CompressCommand.RaiseCanExecuteChanged();
        DecompressCommand.RaiseCanExecuteChanged();
    }

    private void RestoreSavedAnalysis(GameInfo game)
    {
        var saved = _analysisCache.Load(game.InstallPath);
        if (saved is null)
            return;

        foreach (var estimate in saved.Result.Estimates)
            Estimates.Add(estimate);
        SelectedEstimate = Estimates.FirstOrDefault(estimate => estimate.Algorithm == CompressionAlgorithm.Xpress16K)
                           ?? Estimates.FirstOrDefault();
        AnalysisSummary = BuildSavedAnalysisSummary(saved.Result, saved.AnalyzedAt);
        AnalysisButtonText = "Повторить анализ";
        StatusText = $"Загружен сохранённый анализ игры «{game.Name}»";
    }

    private static string BuildSavedAnalysisSummary(GameAnalysisResult result, DateTimeOffset analyzedAt) =>
        $"Анализ от {analyzedAt:dd.MM.yyyy HH:mm} · {result.FileCount:N0} файлов · " +
        $"выборка {ByteFormatter.Format(result.SampleBytes)} · физически занято {ByteFormatter.Format(result.CurrentPhysicalBytes)}";

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
        IsOperating = true;
        OperationProgress = 0;
        OperationSummary = "Ожидаем подтверждение прав администратора…";
        StatusText = OperationSummary;

        var progress = new Progress<WorkerMessage>(message =>
        {
            if (message.TotalBytes > 0)
                OperationProgress = Math.Clamp(message.ProcessedBytes * 100d / message.TotalBytes, 0, 100);
            var counters = message.TotalFiles > 0
                ? $" · {message.ProcessedFiles:N0} из {message.TotalFiles:N0} файлов"
                : string.Empty;
            OperationSummary = $"{message.Text}{counters}";
            StatusText = OperationSummary;
        });

        try
        {
            var result = await _workerClient.ExecuteAsync(job, progress);
            if (result.Type == "cancelled")
            {
                OperationSummary = result.Text ?? "Операция отменена";
                StatusText = OperationSummary;
                return;
            }

            OperationProgress = 100;
            var newState = job.Operation == "compress"
                ? GameCompressionState.Compressed
                : GameCompressionState.Uncompressed;
            var newAlgorithm = job.Operation == "compress" ? job.Algorithm : null;
            var difference = result.PhysicalBefore - result.PhysicalAfter;
            var savedBytes = job.Operation == "compress" ? Math.Max(0, difference) : 0;
            var compressedFiles = job.Operation == "compress" ? result.ProcessedFiles : 0;
            UpdateGameCompressionStatus(
                job.RootPath, newState, newAlgorithm,
                savedBytes, result.PhysicalAfter, compressedFiles, DateTimeOffset.Now);
            TrySaveCompressionStatus(
                job.RootPath, newState, newAlgorithm,
                savedBytes, result.PhysicalAfter, result.TotalBytes, compressedFiles);

            OperationSummary = job.Operation == "compress"
                ? $"Готово · освобождено {ByteFormatter.Format(Math.Max(0, difference))} · ошибок: {result.ErrorCount}"
                : $"Готово · распаковано {result.ProcessedFiles:N0} файлов · ошибок: {result.ErrorCount}";
            StatusText = OperationSummary;
        }
        catch (OperationCanceledException exception)
        {
            OperationSummary = exception.Message;
            StatusText = "Операция отменена";
        }
        catch (Exception exception)
        {
            OperationSummary = $"Операция не выполнена: {exception.Message}";
            StatusText = "Ошибка системной операции";
        }
        finally
        {
            IsOperating = false;
        }
    }

    private void CancelAnalysis() => _analysisCancellation?.Cancel();
}
