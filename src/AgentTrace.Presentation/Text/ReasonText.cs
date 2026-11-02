using System.Globalization;
using AgentTrace.Core;
using AgentTrace.Core.Flow;
using AgentTrace.Git;
using AgentTrace.Ingest;
using AgentTrace.Presentation.Resources;

namespace AgentTrace.Presentation.Text;

/// <summary>
/// User text for what the lower layers report as data (enums and raw messages): they never produce UI text, this class
/// maps them to the resources of the current UI language.
/// </summary>
public static class ReasonText
{
    public static string Describe(UnavailableReason reason) => reason switch
    {
        UnavailableReason.Binary => TextStrings.UnavailableBinary,
        UnavailableReason.TooLarge => TextStrings.UnavailableTooLarge,
        UnavailableReason.NoBaseline => TextStrings.UnavailableNoBaseline,
        UnavailableReason.SubmoduleOrFolder => TextStrings.UnavailableSubmoduleOrFolder,
        UnavailableReason.NoRepository => TextStrings.UnavailableNoRepository,
        UnavailableReason.OutsideObservedRepositories => TextStrings.UnavailableOutsideObservedRepositories,
        _ => throw new System.Diagnostics.UnreachableException(),
    };

    /// <summary>A note about a file's content; <paramref name="detail"/> is the raw message of the notes that carry one.</summary>
    public static string Describe(ContentNote note, string? detail) => note switch
    {
        ContentNote.Missing => TextStrings.ContentMissing,
        ContentNote.IsFolder => TextStrings.ContentIsFolder,
        ContentNote.CannotReadNow => string.Format(CultureInfo.CurrentCulture, TextStrings.ContentCannotReadNowFormat, detail),
        ContentNote.CannotRead => string.Format(CultureInfo.CurrentCulture, TextStrings.ContentCannotReadFormat, detail),
        ContentNote.NotUtf8 => TextStrings.ContentNotUtf8,
        _ => throw new System.Diagnostics.UnreachableException(),
    };

    /// <summary>The encoding label as shown: only the UTF-8 with BOM label is translated, the rest are standard names.</summary>
    public static string Encoding(string label) => label == FileContentReader.Utf8BomLabel ? TextStrings.EncodingUtf8Bom : label;

    /// <summary>The title of a lane: the main one has none and is called "Main agent".</summary>
    public static string LaneTitle(FlowGraph.Lane lane) => lane.Title ?? (lane.IsMain ? TextStrings.MainAgentTitle : lane.Id);

    /// <summary>The message to show for a failed observation call: git's dubious ownership gets an actionable hint.</summary>
    public static string ErrorMessage(Exception error) =>
        error is GitException { IsDubiousOwnership: true } ? ShellStrings.GitDubiousOwnershipHint : error.Message;
}
