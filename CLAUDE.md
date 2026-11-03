# CLAUDE.md — Working instructions

Project: **AgentTrace Studio**. An offline desktop viewer of what AI coding agents do. See `README.md` for the product definition.

## At the start of every session (mandatory)

1. Read `DEVLOG.md` (the "Current status" section and the latest entries).
2. List `PLANS/` and read the open plans.
3. Check the current branch and the git state (`git status`, `git branch`).
4. Summarize for the user: where we left off, what is pending and the proposed next step. Then wait for confirmation or instructions.

## Continuity across interruptions

The session can be cut at any moment. To be able to resume the work:
- Update "Current status" in `DEVLOG.md` after each significant step, not at the end of the session.
- Record in the development log the branch, the active plan, the last step done and the next step, before starting a long task and when closing it.
- Keep the active plan as a living document: checked boxes, decisions and blockers noted.
- Make small, frequent commits on the feature branch, so partial work is saved.
- Nothing important should exist only in the conversation. If it is not in the development log, in a plan or in the code, it is considered lost.

## Product and constraints

- A traceability and agent-monitoring viewer. It is **not** an IDE, nor an AI IDE. Projects are not edited and there is no agent of its own: they are observed. Files are shown in a read-only viewer. A terminal (plan 005, later and low priority) would only be a channel to talk to existing terminal agents or to run console actions.
- C# with .NET (the current LTS version) and modern APIs (records, sealed types, pattern matching, async/Task). Do not use preview features without a reason noted in the development log.
- Native desktop on Windows, Linux and macOS. Lightweight, with robust file-system integration.
- **Works without an internet connection.** No functionality may depend on the network, nor on outgoing telemetry, CDNs or remote services.
- **Independent of the language or framework of the observed project.** No language-specific logic in the core.
- **Independent of any specific agent.** The core works with its own neutral event model. Each supported agent (Claude Code, Codex CLI, Gemini CLI, Cursor, Aider, Copilot, etc.) is integrated through an isolated adapter. Each agent's formats are hypotheses until verified with real traces; note this in the development log.

## Dependency versions

Always prefer the latest and LTS versions (.NET, Avalonia, xUnit, NuGet packages). Check the current version on NuGet (api.nuget.org) before pinning it and record the date of the check in the development log. Do not use preview or EA versions.

## Code principles

- **KISS**: the simplest solution that solves the current problem. No speculative abstractions.
- **DRY**: remove duplication of knowledge, not of text. Do not abstract until there is a real second use.
- **SOLID**: single responsibilities, dependencies toward interfaces, interchangeable adapters. The UI contains no domain logic.
- Follow the style of the existing code: names, comment density and conventions.
- Language: **English** for code, identifiers, comments, XML docs, documentation, plans, development-log entries, commit messages and PR texts. The UI is localized (English by default, Spanish) through resources.
- UI text is never a literal in code. Add the key to `src/AgentTrace.Presentation/Resources/X.resx` **and** `X.es.resx` (a parity test enforces it). Format with placeholders, not concatenation. Plurals use `_One`/`_Other` keys and `Plural.Pick`. Numbers go through `LocalNumbers`.
- `Core`, `Ingest`, `Git`, `Diff` and the adapters return data, not text; turning data into text belongs to the presentation layer.
- Adding a language: copy each `X.resx` to `X.<code>.resx`, translate it, add the code to `SupportedLanguages` and run the tests.
- Tests run under the English culture by default; use `CultureScope` to test other cultures. A repository language lint test (`LanguageLintTest`) fails on Spanish diacritics and inverted marks and on a short list of unambiguous Spanish words in tracked text files (`*.cs, *.md, *.yml, *.props, *.csproj, *.axaml, *.slnx, *.json, *.resx, *.xml, *.txt`). Allowed: `*.es.resx`, `tests/**/TestData/**`, the plan 021 file that quotes the Spanish originals, and any line carrying the marker `lang-ok` (a `// lang-ok` comment in C#, `<!-- lang-ok -->` in Markdown or XML) for deliberate non-English data.

## Workflow: plan → implement → test → review

1. **Plan**: for complex tasks, create a plan in `PLANS/`. For simple tasks an entry in the development log is enough.
2. **Implement**: on a feature branch of its own, with changes limited to the task.
3. **Test**: unit tests, e2e tests and a real run of the application.
4. **Review**: review the complete diff (correctness, simplicity, duplication, security) before closing.

**Definition of done**: a task is complete only if the unit tests pass, the e2e tests pass and the application has been run and tested manually. If any step could not be done, the task is not done: say so explicitly and note it in the development log. Never declare success without the real output of the tests.

### Models
- **Planning**: Opus model (delegate to a subagent with `model: opus`, or the user switches the model with `/model`).
- **Implementation**: Sonnet model (subagent with `model: sonnet`).
- If the assignment cannot be applied, say so and note it in the development log.

## Git: simplified gitflow

- `main`: only the user decides to merge into it. **Never** merge or push to `main` without an express order.
- `development`: integration branch.
- `feature/<id>-<short-description>`: one branch per task, created from `development`.
- When the task is complete (definition of done met), the feature is merged into `development`.
- Atomic commits with clear messages in English. Never skip hooks or rewrite shared history.

## Plans

- The plans for complex tasks live in `PLANS/`, named `NNN-short-title.md` (see `PLANS/TEMPLATE.md`). `PLANS/`, `PLANS_DONE/` and `DEVLOG.md` are local working files of the maintainer's workflow: they are git-ignored and not published.
- They are living documents: updated during the work (status, decisions, findings).
- When a plan is **completely** finished, it moves to `PLANS_DONE/` (for example `PLANS_DONE/013-csharp-migration.md`). `PLANS/` contains only what is pending or in progress.
- Record in the development log every plan created, changed in status or completed.

## Development log

`DEVLOG.md` is the chronological record (new entries go at the end). Record in it:
- Relevant actions taken.
- Tasks started, completed or blocked.
- Decisions, both mine and those the user guided me toward, with their reason.
- Test results and problems found.

Entry format: date, type (`ACTION`, `TASK`, `DECISION`, `GUIDANCE`, `TEST`, `BLOCKER`), summary and reason when applicable.

## Critical attitude

- Evaluate each user proposal with judgment. If there is a problem, say so with concrete arguments and references to industry best practices, and propose an alternative.
- Defend my position if I still think it is better after the reply. Give way when there is a better argument, not out of courtesy.
- If the user decides something different after hearing me, carry out their decision and record it in the development log as `GUIDANCE`.

## License and publication

Open source under **Apache 2.0** (`LICENSE`, `NOTICE`; third parties in `THIRD_PARTY_NOTICES.md`). A new dependency must have a compatible license and be listed in `THIRD_PARTY_NOTICES.md`. Rules for contributing: `CONTRIBUTING.md`, `CODE_OF_CONDUCT.md` and `SECURITY.md`. If an option, a shortcut or an adapter changes, update the README.

## Security and privacy

User decision: traces are shown as they are, **without hiding secrets**. The application is local and offline, and is used like an IDE. Therefore no effort is invested in secret redaction. What is kept: local reading, nothing sent to the network, and not copying traces outside the project unless the user asks.

## Commands and stack (.NET)

Stack: .NET 10 LTS, Avalonia (+ AvaloniaEdit), xUnit v3, System.Text.Json, DiffPlex. Versions checked on NuGet on 2026-10-29. Principles: those of this document (KISS/DRY/SOLID, offline, neutral core with isolated adapters, UI without logic). The Java version was retired in plan 014 (tag `java-final`); structure and decisions of the migration: `PLANS_DONE/` (`PLANS_DONE/013-csharp-migration.md`, `PLANS_DONE/014-remove-java.md`).

All .NET commands are run **from the repository root** (the solution is `AgentTrace.slnx`; the test runner is pinned in `global.json`):
- Build: `dotnet build AgentTrace.slnx`
- Unit tests: `dotnet test --solution AgentTrace.slnx --filter-not-trait "Category=Integration" --filter-not-trait "Category=Acceptance"`
- Everything (acceptance tests skip themselves without their variables): `dotnet test --solution AgentTrace.slnx`
- CI (`.github/workflows/ci.yml`, one job per operating system, its name is the required check of the branch rulesets): build, unit tests (all projects but the UI one), end-to-end tests (`tests/AgentTrace.Desktop.Tests`, headless) and integration and acceptance tests. A pull request is merged only with the three systems green.
- One project or one class: `dotnet test --project tests/AgentTrace.Presentation.Tests --filter-class "*ProjectTreeIT"`
- Run: `dotnet run --project src/AgentTrace.Desktop -- <project> [--open=<f>] [--diff=<f>] [--tab=graph|diff|telemetry|file|knowledge|heatmap] [--kg-select=<f>] [--graph-select=<callId>|turn:<n>|last|last-error] [--graph-collapse=none|old] [--graph-orientation=vertical|horizontal] [--graph-filter=errors,reads] [--graph-zoom=<0.25..2>] [--kg-view=2d|3d|top|side] [--kg-yaw=<degrees>] [--kg-pitch=<degrees>] [--heatmap-zoom=<folder>] [--heatmap-filter=reads,writes,errors] [--heatmap-area=size|activity] [--heatmap-select=<f>] [--search=<text>] [--lang=en|es]`
- Window capture (visual check without a mouse): `... --capture=<png> --capture-delay=<ms> [--capture-mode=printwindow --render=software]`. `printwindow` includes the native title bar; with software rendering it sometimes repaints only partly (white gap), and the default capture (`RenderTarget`) draws the custom content larger with a screen scale ≠ 100 %: to review visuals, use `printwindow`, or the headless tests (`artifacts/bin/AgentTrace.Desktop.Tests/debug/captures/`).
- Publish (a single self-contained executable, ~49 MB): `dotnet publish src/AgentTrace.Desktop -c Release -r win-x64 -o artifacts/publish/win-x64` (also `linux-x64` and `osx-arm64`).
- Acceptance with the real trace: `AGENTTRACE_REAL_TRACE=<file.jsonl>` (otherwise, the most recent session of this project in `~/.claude/projects`; with the variable set, the checks specific to this project are skipped). `RealClaudeTraceIT` requires `CLAUDE.md` and `DEVLOG.md` to appear as touched in that session.
- Acceptance of real changes (real git + real trace of this session; one call per message): `AGENTTRACE_ACCEPTANCE_FILES=<file>=Bash,<file>=Write`, `AGENTTRACE_ACCEPTANCE_VIEWER=<file>=LF|CRLF|MODIFIED,...` and `AGENTTRACE_ACCEPTANCE_TREE=<file>=CREATED|MODIFIED|DELETED|READ,...` (afterwards restore what was touched with `git checkout -- <files>`).
- A new agent: `docs/ADAPTERS.md` (project `AgentTrace.Adapters.<Agent>` + one line in `AdapterCatalog` + a test class that inherits from `AgentAdapterContractTest`).
- `Write`/`Edit` convert the escape sequences of the Unicode separators U+2028 and U+2029 into real characters (which C# takes for line breaks): in code they are written split up or avoided. With xUnit v3 the `Console.WriteLine` of a test is only visible by running the test executable: `artifacts/bin/<project>/debug/<project>.exe -class "*Class" -showLiveOutput`.
- Before building, close the manual-test app: an open `AgentTrace.Desktop.exe` locks `artifacts/bin`.
