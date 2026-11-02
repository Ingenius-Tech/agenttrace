## What changes and why

<!-- Short summary. Link the issue if there is one: "Closes #123". -->

## How it was tested

- [ ] Unit tests pass (`dotnet test --solution AgentTrace.slnx --filter-not-trait "Category=Integration" --filter-not-trait "Category=Acceptance" --filter-not-trait "Category=Parity"`)
- [ ] Related e2e / integration tests
- [ ] I ran the application and tested the change by hand
- [ ] Added or updated tests for the new behavior

<!-- If any item could not be done, say so here. -->

## Checklist

- [ ] The pull request targets `development`
- [ ] It respects the constraints: offline, language- and agent-independent, UI without domain logic
- [ ] Documentation, code identifiers and commit messages are in English; UI texts go in the `.resx` resources (Spanish in the `.es.resx` files)
- [ ] README or `docs/` updated if an option, a shortcut or an adapter changes
- [ ] No real traces or secrets in the diff
- [ ] If I add a dependency: license compatible with Apache 2.0, listed in `THIRD_PARTY_NOTICES.md`
