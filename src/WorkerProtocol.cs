namespace vKOROBKU.Protocol;

public sealed record WorkerJob(
    string RootPath,
    string Operation,
    string? Algorithm,
    string[]? SkipExtensions = null);

public sealed record WorkerCommand(string Type);

public sealed record WorkerMessage(
    string Type,
    // Text is the legacy free-form message, kept only as a fallback for genuinely
    // unexpected exceptions. Localized messages travel as a Code the app renders, so
    // the elevated worker never carries UI language.
    string? Text = null,
    string? Token = null,
    long ProcessedBytes = 0,
    long TotalBytes = 0,
    int ProcessedFiles = 0,
    int TotalFiles = 0,
    int ErrorCount = 0,
    long ErrorBytes = 0,
    int SkipListedFiles = 0,
    long SkipListedBytes = 0,
    long SkipListedPhysicalBytes = 0,
    long PhysicalBefore = 0,
    long PhysicalAfter = 0,
    long ExcludedPhysicalBytes = 0,
    string? Code = null,
    long CodeValue = 0,
    string? CodeArg = null,
    int ResumeSkippedFiles = 0);

/// <summary>Message codes shared by the worker and the app: the worker emits them, the
/// app renders localized text. Keeping the constants in the linked protocol file makes
/// the two sides impossible to drift apart.</summary>
public static class WorkerCodes
{
    public const string ScanningFiles = "scanning_files";
    public const string CheckingProcessed = "checking_processed";
    public const string Prepared = "prepared";
    public const string Compressing = "compressing";
    public const string Decompressing = "decompressing";
    public const string CompactError = "compact_error";
    public const string Verifying = "verifying";
    public const string VerifyingWithErrors = "verifying_with_errors";
    public const string CompletedOk = "completed_ok";
    public const string CompletedWithSkipped = "completed_with_skipped";
    public const string Cancelled = "cancelled";
    public const string NoJob = "no_job";
    public const string UnknownOperation = "unknown_operation";
    public const string UnknownAlgorithm = "unknown_algorithm";
    public const string BadPath = "bad_path";
    public const string DirNotFound = "dir_not_found";
    public const string DriveRoot = "drive_root";
    public const string ReparseRoot = "reparse_root";
    public const string NotNtfs = "not_ntfs";
    public const string NoFiles = "no_files";
    public const string GameRunning = "game_running";
    public const string CompactStartFailed = "compact_start_failed";
}
