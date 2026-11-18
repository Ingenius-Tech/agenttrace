# Areas map

The **Areas map** tab (`Ctrl+7`) shows what the agent works on at a higher level than files: which **layer**, **module** and
**kind of change** each file belongs to, per agent turn, and how a change spreads across layers during a task. The application
knows nothing about languages or frameworks, so an *area* is only a name plus a list of path patterns that you (or your agent)
write in a file. Without that file the tab still works with suggested rules (see "Without a rules file").

## The rules file

Each repository has at most one rules file, at `<repository root>/.agenttrace/areas.json` (a project folder that holds several
repositories can have one in each; a path is classified with the file of the deepest repository that contains it). It is plain
JSON, version 1:

```json
{
  "version": 1,
  "areas": [
    { "id": "data",   "name": "Data",   "dimension": "layer",  "include": ["src/**/Migrations/**", "src/Data/**"], "exclude": ["**/*.md"] },
    { "id": "api",    "name": "API",    "dimension": "layer",  "include": ["src/Api/**"] },
    { "id": "orders", "name": "Orders", "dimension": "module", "include": ["src/**/Orders*/**"], "shared": false },
    { "id": "test",   "name": "Tests",  "dimension": "kind",   "include": ["tests/**"] }
  ]
}
```

| Field | Required | Meaning |
|---|---|---|
| `version` | yes | Must be `1`. |
| `areas` | yes | The list of areas; its order matters (see "First match wins"). |
| `id` | yes | Matches `^[a-z0-9][a-z0-9_-]*$` and is unique across the **whole file** (not per dimension). |
| `name` | yes | Non-empty label shown as it is (it is your data, it is not translated). |
| `dimension` | yes | `layer`, `module` or `kind`. |
| `include` | yes | At least one glob pattern (see "Glob contract"). |
| `exclude` | no | Glob patterns that remove files from the area. |
| `shared` | no | `true` on a module that other modules depend on (used by one badge). |

Unknown properties are ignored, so a newer file still opens in an older application.

### Dimensions

- **layer**: the architectural layer a file is in (data, domain, application, API, UI...). Rows follow the order of the file, so
  list them in the order you want to read them (for example Data, Domain, Application, API).
- **module**: a package, service or library that is built or deployed by itself.
- **kind**: what sort of file it is (tests, documentation, migrations, configuration, dependencies...).

A file belongs to at most one area per dimension. The tab groups the rows by one dimension at a time (the "Group by" box); files
that match no area of that dimension go to an **Unclassified** row, so a misconfiguration shows up where the work happens.

### First match wins

Within each dimension the **first** area, in file order, whose `include` matches the file and whose `exclude` does not, wins.
Put the more specific areas before the general ones. The order of the file is also the order of the rows.

### Reserved kind ids

Two kind ids have a meaning for the task badges; every other kind id is free.

- `test`: tests. Without a `test` kind the "code changed but no tests were touched" badge is **not computed** (never guessed).
- `contract`: public contracts or schemas shared between modules. A change in it raises the "shared or contract code changed"
  badge, as does a change in a module marked `"shared": true`.

The third badge, "changes in N modules", needs no convention (it is raised from two modules on).

### Errors

An invalid file is **never applied partially**. If anything is wrong (unreadable file, invalid JSON with its line, wrong version,
a missing field, an unknown dimension, an invalid or duplicate id, an empty `include`, a rejected pattern) every error is listed
in red under the toolbar, and the tab falls back to the suggested rules until the file is fixed.

### Without a rules file

The tab is never empty. Without a valid file it uses:

- **Modules** inferred from build manifests (`*.csproj`, `package.json`, `pom.xml`, `go.mod`, `Cargo.toml`, `pyproject.toml`...):
  each file belongs to the module of its nearest ancestor folder that holds a manifest, or to a module for the repository root.
- **Kinds** from a generic preset (tests, documentation, migrations, contracts, dependencies, configuration) based on widely used
  names.
- No layers (they depend on how each team names them), so the first view groups by module.

The strip above the matrix says "Rules: suggested" and offers **Copy setup prompt**. The button puts a prompt in the clipboard (only
when you click it) that you paste into your coding agent: it explores the repository, writes `.agenttrace/areas.json` in this
format and checks that each dimension classifies at least 90 % of the files. When the file is written, a running application
picks it up by itself (the file is watched explicitly, although paths that start with a dot are not tracked) and switches the strip
to "Rules: .agenttrace/areas.json". A valid file replaces the suggested rules entirely.

### Coverage

The strip also shows, per dimension, the **coverage**: the files that dimension classifies divided by the listed files, as a whole
percentage rounded down (so a partial coverage never reads as 100 %). The listed files are the same as the Heatmap's: the files on
disk, minus what git ignores, minus paths with a segment that starts with a dot, up to 20 000 files. If the project has more, the
strip says "Partial listing" and the coverage covers only the listed ones. The setup prompt asks the agent to measure the same
thing (`git ls-files` without dot-paths), so the agent and the application agree.

### What the matrix shows

Columns are the agent's **turns** (a turn is opened by each user prompt, as in the Execution graph), grouped into **tasks**: a
prompt opens a task and the turns that continue without a new prompt belong to it. Only turns with activity appear. Cells show
reads, writes and errors with the same colors as the tree. A file the trace reports as changed and git also reports is counted
once; changes only git sees (a console command that writes) are placed in the turn of the call they are attributed to, or counted as
external. Clicking a cell lists its files; double clicking a row header lists the files of that area with activity; selecting a task
header draws the order in which the layers were first changed.

### Limitations

- **Dot-paths**: files and folders whose name starts with a dot (`.github/**`, `.editorconfig`) are not tracked in any view, so a
  rule for them never shows activity. (The rules file itself is read explicitly.)
- **Case sensitivity**: patterns are matched case-sensitively on every operating system, so `Docs` does not match `docs`.
- **Symlinks**: links are not followed, and opening a project through a symlinked path can make repository-relative paths wrong
  (a known limitation of the application in general).
- **One file per repository root**: there is no project-level file that spans repositories; each repository has its own.
- A file in the middle of being written can be invalid for a moment; the errors disappear on the next write.
- The areas are a path classification, not code analysis: two things in the same file cannot be told apart.

## Glob contract

The patterns of `.agenttrace/areas.json` (`include` and `exclude`) are matched against paths relative to the repository root,
separated by `/`, compared ordinally and **case-sensitively** on every operating system.

- `*` is any characters except `/`; `?` is exactly one character except `/`.
- `**` is only allowed as a whole segment and means zero or more segments (`**/x`, `x/**`, `a/**/b`).
- A pattern without `/` (apart from a trailing one) matches the name at any depth. A pattern with a `/` at the start or in the
  middle is anchored at the repository root; a leading `/` is allowed and only anchors.
- A pattern matches a file if it matches the file path or any of its ancestor folders, so `docs`, `docs/` and `docs/**` all match
  `docs/a/b.md`. A trailing `/` restricts the match to folders.
- Anything else is rejected with a specific error, never reinterpreted.

A test runs these tables against the implementation, so this document and the code cannot drift.

| Pattern | Path | Matches |
|---|---|---|
| `*.md` | `README.md` | yes |
| `*.md` | `docs/a/b.md` | yes |
| `*.md` | `src/a.cs` | no |
| `Migrations` | `src/Data/Migrations/001.cs` | yes |
| `Migrations` | `Migrations/001.cs` | yes |
| `Migrations` | `src/Migrations.cs` | no |
| `Migrations/` | `src/Migrations/001.cs` | yes |
| `Migrations/` | `src/Migrations` | no |
| `docs` | `docs/a/b.md` | yes |
| `docs/` | `docs/a/b.md` | yes |
| `docs/**` | `docs/a/b.md` | yes |
| `docs/**` | `docs/b.md` | yes |
| `docs/**` | `src/docs/b.md` | no |
| `/docs` | `docs/b.md` | yes |
| `/docs` | `src/docs/b.md` | no |
| `src/*.cs` | `src/a.cs` | yes |
| `src/*.cs` | `src/sub/a.cs` | no |
| `src/**/*.cs` | `src/a.cs` | yes |
| `src/**/*.cs` | `src/x/y/a.cs` | yes |
| `src/**/*.cs` | `lib/a.cs` | no |
| `**/test` | `test/a.cs` | yes |
| `**/test` | `src/test/a.cs` | yes |
| `a/**/b` | `a/b` | yes |
| `a/**/b` | `a/x/y/b` | yes |
| `a/**/b` | `a/x/y/c` | no |
| `**` | `anything/at/all.txt` | yes |
| `?.md` | `a.md` | yes |
| `?.md` | `ab.md` | no |
| `a?c` | `a/c` | no |
| `*` | `a/b.txt` | yes |
| `Src` | `src/a.cs` | no |
| `a.b` | `axb` | no |
| `*.test.*` | `x/foo.test.ts` | yes |

Rejected patterns and the error each one produces:

| Pattern | Error |
|---|---|
| (empty string) | Empty |
| `a//b` | EmptySegment |
| `a\b` | Backslash |
| `src/{a,b}` | Braces |
| `src/[ab].cs` | CharacterClass |
| `!src` | Negation |
| `C:/src` | Absolute |
| `//server/share` | Absolute |
| `./src` | DotSegment |
| `src/../x` | DotSegment |
| `a/***/b` | TripleStar |
| `a**b` | MixedDoubleStar |
| `**.cs` | MixedDoubleStar |
