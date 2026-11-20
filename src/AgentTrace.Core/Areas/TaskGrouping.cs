using AgentTrace.Core.Flow;

namespace AgentTrace.Core.Areas;

/// <summary>
/// A task: a turn opened by a user prompt plus the continuation turns (no prompt) that follow it. <c>FirstTurn</c> and <c>LastTurn</c> are turn
/// indexes of the <see cref="FlowGraph"/>. <c>HasPrompt</c> is false only for a task that starts with a continuation turn.
/// </summary>
public sealed record AreaTask(int Index, int FirstTurn, int LastTurn, bool HasPrompt);

/// <summary>Groups the turns of a flow into tasks. There is no heuristic on the prompt text: every prompt opens a task.</summary>
public static class TaskGrouping
{
    public static ValueList<AreaTask> Of(IReadOnlyList<FlowGraph.Turn> turns)
    {
        List<AreaTask> tasks = [];
        foreach (FlowGraph.Turn turn in turns)
        {
            if (turn.Prompt is null && tasks.Count > 0) tasks[^1] = tasks[^1] with { LastTurn = turn.Index };
            else tasks.Add(new AreaTask(tasks.Count, turn.Index, turn.Index, turn.Prompt is not null));
        }
        return tasks.ToValueList();
    }
}
