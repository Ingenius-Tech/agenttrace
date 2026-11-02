# How to add a coding agent

AgentTrace Studio does not know any specific agent: it works with neutral events (`AgentTrace.Core.AgentEvent`).
Each agent is integrated with an **adapter**. Adding one does not require touching `Core`, `Ingest` or the UI.

## Steps

1. **Study the agent's trace format** with real traces: where it stores them, how it names the files, how a tool call and its result are linked, where the token usage is and how file changes are marked. Record the findings in `DEVLOG.md`: the format is usually not a public contract.
2. **Create the project** `src/AgentTrace.Adapters.<Agent>` (referencing only `AgentTrace.Core`) with:
   - a public class `<Agent>Adapter : IAgentAdapter` with a parameterless constructor (`Agent`, `Owns`, `FindTraceFiles`, optional `RelatedTraceFiles` and `NewParser`);
   - an `ILineParser` (it may be internal) that turns each line into events (`IReadOnlyList<AgentEvent>`). It is stateful: one is created per file.
3. **Register it**: add `new <Agent>Adapter()` to `AdapterCatalog.All()` in `src/AgentTrace.Desktop/Composition/AdapterCatalog.cs`. It is the only place that knows concrete adapters: **there is no reflection-based discovery** (it would complicate the single-file executable).
4. **Test it**: a project `tests/AgentTrace.Adapters.<Agent>.Tests` with a synthetic trace (in `TestData/`, copied to the output directory) and a class that inherits from `AgentAdapterContractTest` (in `AgentTrace.Testing`) implementing `Adapter`, `SampleTrace` and `ProjectWithTraces`; plus the format's own tests.
5. **Validate with a real trace** (`RealClaudeTraceIT` is an example): no exceptions, no corrupt lines and a verifiable summary.

C# specifics: `AgentEvent` is an `abstract record` with sealed nested records (`switch` statements end in `_ => throw new UnreachableException()`); paths are `FilePath` (case-insensitive equality on Windows); instants are `DateTimeOffset?`; for JSON, `System.Text.Json` with the adapter's `JsonPath` extensions from the Claude Code adapter.

## Contract rules

- `Agent` returns a stable name (`claude-code`, `codex-cli`…).
- `Owns(path)` is `true` only for traces of that agent.
- `FindTraceFiles(project)` returns the project's **session** traces (not the subagent ones), most recent first, or an empty list. `RelatedTraceFiles(session)` returns the traces associated with a session (subagents); it is optional. **They only read**:
  they never create or modify anything in the agent's or the project's directory.
- `ILineParser.Parse` **never throws** and **always returns at least one event**: what it does not understand
  is returned as `Skipped` (`IGNORED` if it is known and adds nothing, `UNKNOWN` if it is new, `MALFORMED` if it is broken).
- Every event carries its `EventHeader` with the agent, the session, the subagent (if any), the timestamp
  (may be null) and the reference to the source line.
- Double counting must be avoided: if the agent repeats the token usage on several lines of the same message,
  the parser must emit a single `TokenUsage` per message.
- **Every `ToolCall` carries a non-null class (`ToolKind`) and a short summary.** The adapter decides the class of each tool
  (`Read`, `Write`, `Execute`, `Search`, `Delegate`; `Other` if it does not recognize it) and a one-line `summary` with the most
  significant part of the input (path, command, pattern, description). The core does not know any agent's tool names and does not
  interpret their inputs: the graph and the details rely on those two fields. The four-argument `ToolCall` constructor
  (class `OTHER`, no summary) exists for tests: an adapter must use the full one.
- **`ToolCall.Target` (optional): the main file the tool acts on**, if the adapter knows it (null if it does not
  know or it does not apply). With it, the knowledge graph and the heatmap attribute failing `ToolResult`s to that file. If the
  adapter does not fill it in, nothing breaks: those errors are counted as "errors without a file" and there are no per-file errors.
- **If the agent delegates to subagents, emit `SubagentStarted(parentCallId, subagentId, description)`** when it can
  link the delegating call to the subagent's thread (`subagentId` is the one the events of that thread carry in their header). It may be
  emitted more than once for the same subagent from different sources: the consumer is idempotent.
- The core depends on no agent and no Avalonia. The adapter may use `System.Text.Json`.

## Available events

`UserMessage`, `AssistantText`, `Thinking`, `ToolCall`, `ToolResult`, `FileChanged`, `FileRead`, `SubagentStarted`, `TokenUsage`, `TurnEnded`, `Skipped`.

`FileChanged` and `FileRead` carry the path as the agent gives it (the core normalizes it). `FileRead` is optional: emit it
when the agent reads a file with a read tool (the path is usually in the call's input, not in the
result). Reads done by a console command (`cat`) are not detected and there is no need to try.
`FileChanged.Kind` is `Created` only if the file did not exist: use the agent's explicit signal if it gives one and, when in doubt, `Modified` (created is the stronger claim). In Claude Code: the result's `type`; without it, `Write` creates if there is no `originalFile`, and `Edit`/`MultiEdit` only with an empty `old_string`.
If an agent provides something that does not fit, adding a new variant is discussed: it must be useful for more than one agent.
