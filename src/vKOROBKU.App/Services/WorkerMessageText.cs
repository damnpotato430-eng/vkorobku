using vKOROBKU.App.Models;
using vKOROBKU.App.Resources;
using vKOROBKU.Protocol;

namespace vKOROBKU.App.Services;

/// <summary>Renders localized text for a worker message. The elevated worker sends a
/// language-agnostic code plus arguments; the app owns the wording. A message without a
/// code (a genuinely unexpected exception) falls back to its raw text.</summary>
public static class WorkerMessageText
{
    public static string Describe(WorkerMessage message) => message.Code switch
    {
        WorkerCodes.ScanningFiles => Strings.Worker_ScanningFiles,
        WorkerCodes.CheckingProcessed => Strings.Worker_CheckingProcessed,
        WorkerCodes.Prepared => DescribePrepared(message),
        WorkerCodes.Compressing => Strings.Worker_Compressing,
        WorkerCodes.Decompressing => Strings.Worker_Decompressing,
        WorkerCodes.CompactError => string.Format(Strings.Worker_CompactError, message.CodeValue),
        WorkerCodes.Verifying => Strings.Worker_Verifying,
        WorkerCodes.VerifyingWithErrors => string.Format(Strings.Worker_VerifyingWithErrors, message.CodeValue),
        WorkerCodes.CompletedOk => Strings.Worker_Completed,
        WorkerCodes.CompletedWithSkipped => Strings.Worker_CompletedWithSkipped,
        WorkerCodes.Cancelled => Strings.Worker_CancelledDetail,
        WorkerCodes.NoJob => Strings.Worker_NoJob,
        WorkerCodes.UnknownOperation => Strings.Worker_UnknownOperation,
        WorkerCodes.UnknownAlgorithm => Strings.Worker_UnknownAlgorithm,
        WorkerCodes.BadPath => Strings.Worker_BadPath,
        WorkerCodes.DirNotFound => Strings.Worker_DirNotFound,
        WorkerCodes.DriveRoot => Strings.Worker_DriveRoot,
        WorkerCodes.ReparseRoot => Strings.Worker_ReparseRoot,
        WorkerCodes.NotNtfs => Strings.Worker_NotNtfs,
        WorkerCodes.NoFiles => Strings.Worker_NoFiles,
        WorkerCodes.GameRunning => string.Format(Strings.Worker_GameRunning, message.CodeArg),
        WorkerCodes.CompactStartFailed => Strings.Worker_CompactStartFailed,
        _ => message.Text ?? string.Empty
    };

    private static string DescribePrepared(WorkerMessage message)
    {
        var notes = new List<string>();
        if (message.ResumeSkippedFiles > 0)
            notes.Add(string.Format(Strings.Worker_PrepAlreadyProcessed, $"{message.ResumeSkippedFiles:N0}"));
        if (message.SkipListedFiles > 0)
            notes.Add(string.Format(
                Strings.Worker_PrepSkipped, $"{message.SkipListedFiles:N0}", ByteFormatter.Format(message.SkipListedBytes)));
        return notes.Count > 0
            ? string.Format(Strings.Worker_PreparedNotes, string.Join(" · ", notes))
            : Strings.Worker_Prepared;
    }
}
