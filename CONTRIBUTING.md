# Contributing to AgentTrace Studio

Thank you for wanting to help! This document summarizes what you need. By participating you agree to the [Code of Conduct](CODE_OF_CONDUCT.md).

## Before you start

AgentTrace Studio is a **viewer** of what AI coding agents do: it is not an IDE, it does not edit projects and it
includes no agent of its own. These constraints are not negotiable and are worth reading before proposing a feature:

- **Offline**: no feature may depend on the network, telemetry, CDNs or remote services.
- **Independent of the observed project's language**: no language-specific logic in the core.
- **Independent of the agent**: the core uses a neutral event model; each agent (Claude Code, Codex CLI, Gemini CLI, Cursor,
  Aider, Copilot…) is integrated through an **isolated adapter**. How to add one: [`docs/ADAPTERS.md`](docs/ADAPTERS.md). The formats of each
  agent are hypotheses until verified with real traces.
- The UI contains no domain logic.

Code principles: KISS, DRY (of knowledge, not of text) and SOLID; no speculative abstractions. Follow the style of the existing
code. Documentation, code identifiers, comments and commit messages are in **English**. UI texts live in the `.resx`
resources (English by default, Spanish in the `.es.resx` files); to add or change a UI language, see the
[Languages](README.md#languages) section of the README.

## What you can do

- **Report a bug** or **propose an improvement**: open an issue with the matching template. For large changes,
  discuss them in an issue first.
- **Add an adapter** for another agent: it is the most valuable contribution. Attach a **synthetic** trace (see below).
- **Vulnerabilities**: not through a public issue, follow [SECURITY.md](SECURITY.md).

## Setting up the environment

Requirements: **.NET 10 SDK** and `git`. All commands run from the repository root:

```bash
dotnet build AgentTrace.slnx
dotnet test --solution AgentTrace.slnx --filter-not-trait "Category=Integration" --filter-not-trait "Category=Acceptance"
dotnet run --project src/AgentTrace.Desktop -- <project>
```

Integration and acceptance tests are skipped if you do not set their environment variables (see `CLAUDE.md`, "Commands and stack" section).
If you have the application open, close it before building: it locks `artifacts/bin`.

## Workflow

1. *Fork* the repository and create a branch from `development`: `feature/<short-description>` (or `fix/<short-description>`).
2. Make small, atomic commits with clear messages (in English).
3. A task is **done** only if the unit tests and the e2e tests pass, and you have run the application and tested it by hand.
   State in the pull request what you did for each item and, if something could not be done, say so.
4. Open the pull request **against `development`**. `main` is only updated by the maintainer's decision.
5. Review your own diff before asking for review: correctness, simplicity, duplication and security.

Add or update tests with every behavior change. If you change a UI option or a shortcut, update `README.md`.

## Sample traces and privacy

The application shows traces **without hiding secrets**. Therefore:

- **Never** upload a real trace to an issue, a pull request or `tests/` without reviewing it: it may contain keys, personal
  paths or private code.
- For tests use **synthetic** traces, or reduced and cleaned ones (examples in `tests/**/TestData`).

## License of contributions

The project is distributed under the [Apache License 2.0](LICENSE). Under its section 5, any contribution you submit
for inclusion in the project is licensed under those same terms, unless you expressly state otherwise. No additional
agreement needs to be signed. Make sure you have the right to contribute what you submit (your own code, or code under a compatible license) and
do not include third-party code without listing it in [THIRD_PARTY_NOTICES.md](THIRD_PARTY_NOTICES.md).
