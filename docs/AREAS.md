# Areas map

> This file holds only the glob contract for now. The rest (rules file format, dimensions, first-match rule, reserved kind ids,
> coverage, limitations) is written in plan 028, phase 6.

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
