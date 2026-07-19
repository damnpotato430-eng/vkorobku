using System.Resources;

[assembly: NeutralResourcesLanguage("en")]

namespace vKOROBKU.App.Resources;

/// <summary>Strongly typed access to the localized UI strings. The properties are
/// written by hand because the classic resx code generator only runs inside Visual
/// Studio; the resource parity test keeps the keys, the English base and the Russian
/// satellite in sync.</summary>
public static class Strings
{
    public static ResourceManager ResourceManager { get; } =
        new("vKOROBKU.App.Resources.Strings", typeof(Strings).Assembly);

    private static string Get(string key) => ResourceManager.GetString(key) ?? key;

    public static string App_ErrorRepeated => Get(nameof(App_ErrorRepeated));
    public static string App_ErrorContinues => Get(nameof(App_ErrorContinues));
    public static string Sidebar_Tagline => Get(nameof(Sidebar_Tagline));
    public static string Nav_Library => Get(nameof(Nav_Library));
    public static string Nav_Operations => Get(nameof(Nav_Operations));
    public static string Nav_Settings => Get(nameof(Nav_Settings));
    public static string Nav_About => Get(nameof(Nav_About));
    public static string Sidebar_AnalysisSection => Get(nameof(Sidebar_AnalysisSection));
    public static string Sidebar_AnalysisHint => Get(nameof(Sidebar_AnalysisHint));
    public static string Sidebar_SystemSection => Get(nameof(Sidebar_SystemSection));
    public static string Sidebar_CoversSource => Get(nameof(Sidebar_CoversSource));
    public static string Update_OpenPageTooltip => Get(nameof(Update_OpenPageTooltip));
    public static string Header_Title => Get(nameof(Header_Title));
    public static string Header_Subtitle => Get(nameof(Header_Subtitle));
    public static string Header_RefreshCovers => Get(nameof(Header_RefreshCovers));
    public static string Header_RefreshCoversTooltip => Get(nameof(Header_RefreshCoversTooltip));
    public static string Header_AddFolder => Get(nameof(Header_AddFolder));
    public static string Drives_Section => Get(nameof(Drives_Section));
    public static string Watcher_RecompressAll => Get(nameof(Watcher_RecompressAll));
    public static string Watcher_RecompressAllTooltip => Get(nameof(Watcher_RecompressAllTooltip));
    public static string Watcher_CheckNow => Get(nameof(Watcher_CheckNow));
    public static string Search_Placeholder => Get(nameof(Search_Placeholder));
    public static string MultiSelect_Tooltip => Get(nameof(MultiSelect_Tooltip));
    public static string DegradedFilter_Tooltip => Get(nameof(DegradedFilter_Tooltip));
    public static string Sort_Tooltip => Get(nameof(Sort_Tooltip));
    public static string Queue_MethodTooltip => Get(nameof(Queue_MethodTooltip));
    public static string Queue_Start => Get(nameof(Queue_Start));
    public static string Card_OpenFolder => Get(nameof(Card_OpenFolder));
    public static string Card_AddToQueue => Get(nameof(Card_AddToQueue));
    public static string Card_RefreshCover => Get(nameof(Card_RefreshCover));
    public static string Card_RecheckState => Get(nameof(Card_RecheckState));
    public static string Card_RemoveFromLibrary => Get(nameof(Card_RemoveFromLibrary));
    public static string Card_DirectStorageTooltip => Get(nameof(Card_DirectStorageTooltip));
    public static string Panel_Section => Get(nameof(Panel_Section));
    public static string Panel_NoGameSelected => Get(nameof(Panel_NoGameSelected));
    public static string Panel_ExpertMode => Get(nameof(Panel_ExpertMode));
    public static string Panel_ReviewIdentity => Get(nameof(Panel_ReviewIdentity));
    public static string Panel_DirectStorageWarning => Get(nameof(Panel_DirectStorageWarning));
    public static string Panel_AnalysisAccuracy => Get(nameof(Panel_AnalysisAccuracy));
    public static string Queue_Skip => Get(nameof(Queue_Skip));
    public static string Queue_SkipTooltip => Get(nameof(Queue_SkipTooltip));
    public static string Queue_StopAfterCurrent => Get(nameof(Queue_StopAfterCurrent));
    public static string Queue_StopAfterCurrentTooltip => Get(nameof(Queue_StopAfterCurrentTooltip));
    public static string Queue_StopAll => Get(nameof(Queue_StopAll));
    public static string Queue_StopAllTooltip => Get(nameof(Queue_StopAllTooltip));
    public static string Estimate_SavingsLabel => Get(nameof(Estimate_SavingsLabel));
    public static string Estimate_ConfidenceLabel => Get(nameof(Estimate_ConfidenceLabel));
    public static string Info_AutoTitle => Get(nameof(Info_AutoTitle));
    public static string Info_AutoBody => Get(nameof(Info_AutoBody));
    public static string Info_AutoConfirm => Get(nameof(Info_AutoConfirm));
    public static string Info_ActiveTitle => Get(nameof(Info_ActiveTitle));
    public static string Info_ActiveLocked => Get(nameof(Info_ActiveLocked));
    public static string Info_PartialTitle => Get(nameof(Info_PartialTitle));
    public static string Info_PartialBody => Get(nameof(Info_PartialBody));
    public static string Info_CompressedTitle => Get(nameof(Info_CompressedTitle));
    public static string Info_CompressedBody => Get(nameof(Info_CompressedBody));
    public static string Action_Optimize => Get(nameof(Action_Optimize));
    public static string Action_Stop => Get(nameof(Action_Stop));
    public static string Action_Decompress => Get(nameof(Action_Decompress));
    public static string Action_DecompressPartialTooltip => Get(nameof(Action_DecompressPartialTooltip));
    public static string Action_CompressSelected => Get(nameof(Action_CompressSelected));
    public static string Action_FinishTooltip => Get(nameof(Action_FinishTooltip));
    public static string Settings_Title => Get(nameof(Settings_Title));
    public static string Settings_Subtitle => Get(nameof(Settings_Subtitle));
    public static string Settings_LanguageSection => Get(nameof(Settings_LanguageSection));
    public static string Settings_LanguageAuto => Get(nameof(Settings_LanguageAuto));
    public static string Settings_LanguageRestartHint => Get(nameof(Settings_LanguageRestartHint));
    public static string Settings_WatcherEnabled => Get(nameof(Settings_WatcherEnabled));
    public static string Settings_DecayThreshold => Get(nameof(Settings_DecayThreshold));
    public static string Settings_MinimumSavings => Get(nameof(Settings_MinimumSavings));
    public static string Settings_SkipSection => Get(nameof(Settings_SkipSection));
    public static string Settings_SkipEnabled => Get(nameof(Settings_SkipEnabled));
    public static string Settings_ExtensionExample => Get(nameof(Settings_ExtensionExample));
    public static string Settings_AddExtension => Get(nameof(Settings_AddExtension));
    public static string Settings_RemoveSelected => Get(nameof(Settings_RemoveSelected));
    public static string Settings_ResetList => Get(nameof(Settings_ResetList));
    public static string Settings_ResetListTooltip => Get(nameof(Settings_ResetListTooltip));
    public static string Settings_Save => Get(nameof(Settings_Save));
    public static string Settings_Cancel => Get(nameof(Settings_Cancel));
    public static string Settings_DefaultListFormat => Get(nameof(Settings_DefaultListFormat));
    public static string Settings_ExtensionFormatError => Get(nameof(Settings_ExtensionFormatError));
    public static string Settings_ExtensionInDefault => Get(nameof(Settings_ExtensionInDefault));
    public static string Settings_ExtensionExists => Get(nameof(Settings_ExtensionExists));
    public static string Settings_DecayError => Get(nameof(Settings_DecayError));
    public static string Settings_SavingsError => Get(nameof(Settings_SavingsError));
    public static string Settings_ResetConfirm => Get(nameof(Settings_ResetConfirm));
}
