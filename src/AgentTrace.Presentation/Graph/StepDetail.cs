using System.Diagnostics;
using System.Globalization;
using AgentTrace.Core;
using AgentTrace.Core.Flow;
using AgentTrace.Presentation.Localization;
using AgentTrace.Presentation.Resources;
using static AgentTrace.Core.AgentEvent;
using static AgentTrace.Core.Flow.FlowGraph;

namespace AgentTrace.Presentation.Graph;

/// <summary>
/// What the detail panel of a step or a turn shows, already as text. No Avalonia. It is a value: if it does not change
/// between two repaints, the panel is not rebuilt (so the user's text selection is not lost).
/// <c>ViaGit</c>: files of <c>Files</c> that the trace does not declare: git attributes them by time (only Exact or Probable to a single call).
/// <c>SubagentId</c>: lane launched by this step, to jump to it, or null.
/// </summary>
public sealed record StepDetail(string Title, ValueList<StepDetail.Field> Fields, ValueList<string> Notes, ValueList<FileTouch> Files,
    ValueList<FilePath> ViaGit, string InputLabel, string Input, string OutputLabel, string Output, string? SubagentId)
{
    public const int MaxOutput = 20_000;

    public sealed record Field(string Label, string Value);

    public static StepDetail? Of(FlowGraph graph, GraphSelection? selection, TimeZoneInfo zone, IReadOnlyList<ChangeEntry>? changes = null)
    {
        if (selection is null) return null;
        return selection.IsStep ? OfStep(graph, selection.CallId!, zone, changes ?? []) : OfTurn(graph, selection.TurnIndex, zone);
    }

    private static StepDetail? OfStep(FlowGraph graph, string callId, TimeZoneInfo zone, IReadOnlyList<ChangeEntry> changes)
    {
        foreach (Batch batch in graph.Batches)
        {
            foreach (Step step in batch.Steps)
            {
                if (step.CallId != callId) continue;
                var fields = new List<Field>
                {
                    new(GraphStrings.FieldTool, step.ToolName),
                    new(GraphStrings.FieldKind, KindLabel(step)),
                };
                if (!string.IsNullOrWhiteSpace(step.Summary)) fields.Add(new Field(GraphStrings.FieldSummary, step.Summary));
                fields.Add(new Field(GraphStrings.FieldState, StateLabel(step.State)));
                if (step.Start is not null) fields.Add(new Field(GraphStrings.FieldStart, Time(step.Start.Value, zone)));
                if (step.Duration is not null) fields.Add(new Field(GraphStrings.FieldDuration, StepText.FormatDuration(step.Duration)));
                if (batch.Tokens is not null)
                {
                    string tokens = FormatTokens(batch.Tokens);
                    if (batch.Steps.Count > 1) tokens = string.Format(CultureInfo.CurrentCulture, GraphStrings.TokensSharedFormat, tokens, batch.Steps.Count);
                    fields.Add(new Field(GraphStrings.FieldTokens, tokens));
                }
                var notes = new List<string>();
                if (batch.Notes.ThinkingCount > 0)
                {
                    string head = string.Format(CultureInfo.CurrentCulture,
                            Plural.Pick(batch.Notes.ThinkingCount, GraphStrings.ThinkingHead_One, GraphStrings.ThinkingHead_Other), batch.Notes.ThinkingCount)
                        + (batch.Notes.ThinkingTime == TimeSpan.Zero ? "" : " · " + StepText.FormatDuration(batch.Notes.ThinkingTime));
                    notes.Add(string.IsNullOrWhiteSpace(batch.Notes.ThinkingText) ? head : head + "\n" + batch.Notes.ThinkingText);
                }
                foreach (string t in batch.Notes.Texts) notes.Add(string.Format(CultureInfo.CurrentCulture, GraphStrings.NoteSaidFormat, t));
                var files = new List<FileTouch>(step.Files);
                var viaGit = new List<FilePath>();
                foreach (ChangeEntry entry in changes)
                {
                    Attribution a = entry.Attribution;
                    bool mine = (a.Confidence == Confidence.Exact || a.Confidence == Confidence.Probable)
                        && a.Calls.Count == 1 && a.Calls[0].CallId == step.CallId;
                    FilePath path = entry.Change.Path;
                    if (mine && !files.Any(f => f.Path == path))
                    {
                        files.Add(new FileTouch(path, entry.Change.Kind));
                        viaGit.Add(path);
                    }
                }
                return new StepDetail(step.ToolName + " · " + (string.IsNullOrWhiteSpace(step.Summary) ? step.CallId : FirstLine(step.Summary)),
                    fields.ToValueList(), notes.ToValueList(), files.ToValueList(), viaGit.ToValueList(), GraphStrings.InputLabel, step.InputJson, GraphStrings.OutputLabel,
                    Truncate(step.Output), step.SubagentId);
            }
        }
        return null;
    }

    private static StepDetail? OfTurn(FlowGraph graph, int index, TimeZoneInfo zone)
    {
        if (index < 0 || index >= graph.Turns.Count) return null;
        Turn turn = graph.Turns[index];
        int steps = 0;
        int errors = 0;
        foreach (Batch b in graph.Batches)
        {
            if (b.TurnIndex != index) continue;
            foreach (Step s in b.Steps)
            {
                steps++;
                if (s.State == StepState.Error) errors++;
            }
        }
        var fields = new List<Field>
        {
            new(GraphStrings.FieldState, turn.Closed ? GraphStrings.TurnStateClosed : GraphStrings.TurnStateOpen),
        };
        if (turn.Start is not null) fields.Add(new Field(GraphStrings.FieldStart, Time(turn.Start.Value, zone)));
        if (turn.Duration is not null) fields.Add(new Field(GraphStrings.FieldDuration, StepText.FormatDuration(turn.Duration)));
        fields.Add(new Field(GraphStrings.FieldSteps, errors > 0
            ? string.Format(CultureInfo.CurrentCulture, Plural.Pick(errors, GraphStrings.StepsWithErrors_One, GraphStrings.StepsWithErrors_Other), steps, errors)
            : steps.ToString(CultureInfo.CurrentCulture)));
        if (turn.ExternalChanges > 0)
        {
            fields.Add(new Field(GraphStrings.FieldExternalChanges, string.Format(CultureInfo.CurrentCulture,
                Plural.Pick(turn.ExternalChanges, GraphStrings.ExternalChanges_One, GraphStrings.ExternalChanges_Other), turn.ExternalChanges)));
        }
        if (turn.AnswerTokens is not null) fields.Add(new Field(GraphStrings.FieldAnswerTokens, FormatTokens(turn.AnswerTokens)));
        string title = string.Format(CultureInfo.CurrentCulture,
            turn.Prompt is null ? GraphStrings.TurnContinuationTitleFormat : GraphStrings.TurnTitleFormat, index + 1);
        return new StepDetail(title, fields.ToValueList(), ValueList<string>.Empty, ValueList<FileTouch>.Empty, ValueList<FilePath>.Empty,
            GraphStrings.PromptLabel, turn.Prompt ?? "", GraphStrings.AnswerLabel, turn.Answer ?? "", null);
    }

    private static string KindLabel(Step step) => step.Kind switch
    {
        ToolKind.Read => GraphStrings.KindRead,
        ToolKind.Write => GraphStrings.KindWrite,
        ToolKind.Execute => GraphStrings.KindExecute,
        ToolKind.Search => GraphStrings.KindSearch,
        ToolKind.Delegate => GraphStrings.KindDelegate,
        ToolKind.Other => GraphStrings.KindOther,
        _ => throw new UnreachableException(),
    };

    internal static string StateLabel(StepState state) => state switch
    {
        StepState.Running => GraphStrings.StateRunning,
        StepState.Ok => GraphStrings.StateOk,
        StepState.Error => GraphStrings.StateError,
        StepState.NoResult => GraphStrings.StateNoResult,
        _ => throw new UnreachableException(),
    };

    private static string FormatTokens(Tokens t) =>
        string.Format(CultureInfo.CurrentCulture, GraphStrings.TokensFormat, t.Input, t.Output, t.CacheRead, t.CacheCreation);

    private static string Time(DateTimeOffset instant, TimeZoneInfo zone) =>
        TimeZoneInfo.ConvertTime(instant, zone).ToString("HH:mm:ss", CultureInfo.InvariantCulture);

    private static string FirstLine(string text)
    {
        int nl = text.IndexOf('\n');
        return nl < 0 ? text : text[..nl];
    }

    private static string Truncate(string? output)
    {
        if (output is null) return "";
        if (output.Length <= MaxOutput) return output;
        return output[..MaxOutput] + "\n" + string.Format(CultureInfo.CurrentCulture, GraphStrings.TruncatedFormat, output.Length - MaxOutput);
    }
}
