# AgentTrace Studio

A native, **100 % offline** desktop viewer to follow, simply and intuitively, what AI coding agents do on a local project.

Free software under the [Apache License 2.0](LICENSE): you can use, modify and redistribute it, **keeping the attribution notice**
([`NOTICE`](NOTICE)). Contributions are welcome: see [How to contribute](#how-to-contribute).

## What it is

Something like an IDE focused only on **traceability and monitoring**. It lets you:

* Import the projects the agents work on.
* See the workflow and the execution of tasks by autonomous agents.
* Quickly identify what changes the agent makes (files created, modified and deleted, diffs).
* Watch every process in real time (events, invoked tools, metrics).

## What it is not

* It is not an AI IDE, nor an IDE you configure AI in.
* It does not edit code or include an agent of its own: it observes. It shows the project's files in a read-only viewer and, later on, may open a terminal as a channel to talk to existing terminal agents or run console actions.
* It does not depend on a specific language, framework or agent.

## Principles

* **Offline**: no network, no outgoing telemetry.
* **Native and lightweight**: C# (.NET) on Windows, Linux and macOS.
* **Agnostic**: own event model and one adapter per agent (Claude Code, Codex CLI, Gemini CLI, Cursor, Aider, etc.).
* **Local-first**: traces are read and shown locally and never leave the machine. They are shown **as they are, without hiding secrets**
(the application is local and used like an IDE): do not share real traces in issues without reviewing them.

## Status

Early prototype. When you open a project:

* **File tree**, IDE-style: icons by type, folders first, lazy loading and live refresh; each file is colored
according to what happened to it (created `+` green, modified `~` amber, deleted `−` red and struck through, still
visible as a "ghost"), with a filled badge if the agent did it, an outlined one if it is external, and an eye if the agent
read it. Folders show the total number of changes they contain. It only expands as far as the changes; buttons to
expand, collapse and show only what changed; context menu and "Show in tree" from the Inspector.
* **Diff inspector**: live list of the files that change on disk (via `git`), with their diff, and which agent action
each change is attributed to (exact `←`, probable `≈`, ambiguous `?` or external), comparing from the start of the
observation or against `HEAD`. If the folder is not a repository but **contains several in its subfolders** (up to three
levels; without descending into a repository or into `node_modules`, `target`, `.venv`, `bin` or `obj`), it observes all of them at
once: a single list of changes, and each diff comes from its own repository. If there are none, it offers to create one (only with your
confirmation) or continues without git ("Continue without git"): only the trace actions are shown, without diffs.
* **Process telemetry**: follows the active Claude Code session live (messages, tools, results, touched files,
tokens, turns), with a summary in the status bar.
* **File** (`Ctrl+4`): **read-only** viewer with line numbers and syntax highlighting (about 45 extensions in
14 families; anything unknown is plain text). It opens with a double click or Enter in the tree, a double click in the change list or
on a telemetry event that refers to a file. It shows encoding, line ending and size, warns if the file is
truncated (over 5 MiB) or is not UTF-8, does not show binaries and **updates itself** if the file changes. The Inspector's diff
uses the same viewer, with a green/red background, `+`/`−` prefixes and double numbering.
* **Execution graph** (`Ctrl+1`): how and in what order the agent worked. By default time flows downward ("Vertical" button to toggle with horizontal). Each tool call is a card with its
icon (read, write, execute, search, delegate), its status (success, error, running or no result, also
indicated with text) and its duration; calls launched at the same time are stacked in a column, each user request
opens a band and subagents have their own lane. It follows the session live, with zoom (`Ctrl` + wheel), dragging,
keyboard (arrows, `Home`/`End`, `E` = next error, Enter = open the file) and a detail panel with the input,
the output, the preceding reasoning and the touched files (**Open** and **View diff**).
Old turns are folded into a summary card (steps, errors, duration; click its header or the card to
unfold), and the "Errors only" and "Hide reads" filters dim what you do not care about without changing the structure. A step's
detail also includes the files git attributes to it by time (marked "(git)").
* **Knowledge** (`Ctrl+5`): graph of the project files the agent has touched and how they relate, computed only
from the traces (without analyzing code, so it works for any language). Each file is a node with the same color
code as the tree (created `+` green, modified `~` amber, deleted `−` red, read with a blue eye; dashed outline if the
change was external) and its size grows with what was touched. A line joins the files the agent changed in the same turn and a
dashed arrow goes from what it read before editing to what was edited. It updates live (every second, without the drawing
jumping), with wheel zoom, dragging, search by name, "Changed only" and "Repeated relations"; clicking a
file shows its relations and lets you open it or view its diff. The **3D** button switches to a view where the vertical axis is
**time**: each file sits at the height of the turn in which it appeared (with the "Turn N" axis); dragging rotates, `Shift`+drag
pans, the wheel zooms, and "Top" (equivalent to 2D) and "Side" (the project growing) are predefined views. A vertical **trail** ("Trails" button, on by default) runs from the turn in which each file appeared to the last one in which it was touched, to see which old files are still active without moving the node. Reads and changes the agent makes with its file tools count as the agent's. What it reads with `cat` or `grep` inside a console command is not seen. Files that a console command writes (`sed -i`, a redirection, `cp`...) are not in the trace either, but git reports them, so they are added from the changes of the observed repositories (as the agent's when the timing matches a call, otherwise as external), without relations between files.
* **Heatmap** (`Ctrl+6`): treemap of the project's folders and files (with the same ignore rules as the tree)
colored by what the agent has done in the session: reads, writes and errors, which can be toggled on and off
(per-file errors depend on the agent indicating which file each call fails on; otherwise the button is
disabled and the summary counts "errors without a file"). More color, more activity (logarithmic scale); the area of each
cell is the size on disk (you see the cold zones) or the activity. A light outline marks what was touched in the last turn and a
dashed one marks external changes (red, what was deleted). Hovering gives the detail, clicking selects (with the file's
panel), double click opens the file or zooms into the folder, `Backspace`, `Alt`+`↑` or the breadcrumbs go up, and the context
menu offers open, view diff, show in tree, view in "Knowledge" and zoom in. It updates live.
* **Search** (`Ctrl+K`): modeless window that searches everything that happened in the active session (requests, answers,
reasoning, tools with their input and output, errors and files), case- and accent-insensitive, with filters by
type, thread (main or subagents) and turn. Enter or a double click takes you to the graph's step or turn, or opens the file.
* **Terminal** (`Ctrl+T`): opens the system terminal in the project folder (`AGENTTRACE_TERMINAL` to choose another one). The application
does not include a terminal of its own.

The window remembers the last project: it opens on its own when started without arguments, the "Open Project..." button proposes that folder and its arrow lists the
recent ones (stored in `~/.agenttrace-studio/recent-projects.txt`). The bottom bar summarizes the session (events, turns, tools, errors, files, external,
subagents and tokens) with the color code. On Windows the native title bar takes the application's color.

Supported agents: Claude Code (to add others: `docs/ADAPTERS.md`).

## Languages

The documentation and the code are in English. The application UI is available in **English** (default) and **Spanish**
(shown in the application as "Español"). The language is chosen, in this order of precedence: <!-- lang-ok -->

1. The `--lang=en|es` argument (for that run only).
2. The saved preference: pick it in the language menu of the top bar (it applies after restarting the application; the application offers to restart) or write the code (`en` or `es`) in `~/.agenttrace-studio/language.txt`.
3. The operating system's language, if it is supported.
4. English.

Formats of numbers and dates follow the language (and the OS regional settings when they use the same language); clock times keep the `HH:mm:ss` format. Text that comes from agent traces, files or git is shown as it is, without translating.

To add a language:

1. Copy each `src/AgentTrace.Presentation/Resources/X.resx` to `X.<code>.resx` (for example `ShellStrings.fr.resx`) and translate the values (keep the keys and the `{0}` placeholders).
2. Add the code and the native name of the language to `SupportedLanguages` (`src/AgentTrace.Presentation/Localization/SupportedLanguages.cs`).
3. Run the tests (there are tests that check each language has the same keys and placeholders as English).

## Stack and usage

The project is written in C# (.NET 10 LTS) with Avalonia (dark theme, AvaloniaEdit for the viewer), xUnit v3, System.Text.Json and DiffPlex.

```bash
dotnet run --project src/AgentTrace.Desktop -- <project>              # run (opens the last project if none is given)
dotnet test --solution AgentTrace.slnx                                # tests (integration and acceptance are skipped without their variables)
dotnet publish src/AgentTrace.Desktop -c Release -r win-x64 -o artifacts/publish/win-x64   # a single self-contained .exe (~49 MB)
```

Requirements: .NET 10 SDK and `git` (without it, only the trace actions are shown, without diffs; another executable can be given with `AGENTTRACE_GIT=<path>`).
Arguments: `<project>`, `--open`, `--diff`, `--tab`, `--kg-select`, `--graph-select`, `--graph-collapse`, `--graph-orientation`,
`--graph-filter`, `--graph-zoom`, `--kg-view=2d|3d|top|side`, `--kg-yaw`, `--kg-pitch`, `--search=<text>`, `--lang=en|es` and, to check the window without a mouse, `--capture=<png> [--capture-delay=<ms>] [--capture-mode=printwindow] [--render=software]`.
Shortcuts: `Ctrl+O` open project, `Ctrl+1`…`Ctrl+6` switch tab, `Ctrl+K` search, `Ctrl+T` open a system terminal in the project folder. Synthetic traces and tests are in `tests/`.

```
src/
  AgentTrace.Core/                  Neutral model (events, FilePath, correlation, graphs) with no dependencies
  AgentTrace.Diff/                  Diff engine (DiffPlex)
  AgentTrace.Ingest/                Following of traces, changes and the file system
  AgentTrace.Git/                   Changes according to git (read-only)
  AgentTrace.Adapters.ClaudeCode/   Claude Code adapter (one per agent)
  AgentTrace.Presentation/          Pure view logic, no Avalonia; localization and .resx resources
  AgentTrace.Desktop/               The Avalonia application and the composition root (AdapterCatalog)
tests/                            Unit, integration, acceptance and UI (headless) tests
```

## Structure

```
AgentTrace.slnx   .NET solution (global.json, Directory.*.props)
src/, tests/      Code and tests (structure in "Stack and usage")
docs/ADAPTERS.md  How to add an agent
LICENSE, NOTICE   Apache 2.0 license and attribution notice
THIRD_PARTY_NOTICES.md  Licenses of the third-party components
CONTRIBUTING.md, CODE_OF_CONDUCT.md, SECURITY.md   Collaboration, conduct and security
.github/          Continuous integration, issue and pull request templates
CLAUDE.md         The assistant's working instructions
```

## How to contribute

It is a collaborative project: issues, fixes, improvements and, above all, **adapters for other agents**
([`docs/ADAPTERS.md`](docs/ADAPTERS.md)) are appreciated. Read [`CONTRIBUTING.md`](CONTRIBUTING.md) first: it summarizes the product constraints
(offline, language- and agent-independent, observe only), the environment and the workflow. Documentation, code and commit messages are written in English; UI texts go in the `.resx` resources (see [Languages](#languages)).

Simplified gitflow: one branch per task created from `development`, pull request against `development`; `main` is only updated by
the maintainer's decision. Participating means accepting the [Code of Conduct](CODE_OF_CONDUCT.md). Vulnerabilities are
reported privately, according to [`SECURITY.md`](SECURITY.md).

## License

[Apache License 2.0](LICENSE). It requires **attribution**: anyone who redistributes the software, or a derivative work, must keep the license
and the [`NOTICE`](NOTICE) notice (section 4 of the license) and state the changes they have made. Contributions are licensed
under the same terms (section 5).

## Third-party credits

Avalonia (MIT), AvaloniaEdit (MIT), DiffPlex (Apache 2.0) and the codicons icon font (CC BY 4.0), bundled in `Assets/`, among
others. Full list and attributions in [`THIRD_PARTY_NOTICES.md`](THIRD_PARTY_NOTICES.md).

