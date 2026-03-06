# Contributing

Thanks for your interest in contributing to Thorium Rust Mod .

## Branches

| Branch | Purpose |
|---|---|
| `main` | Stable releases only |
| `staging` | Rust Staging Branch testing |
| `develop` | Active development |

Open PRs against `develop` unless you're working directly on `staging` work.

Branches should be named as such:
`feature/my-cool-feature`
`bugfix/fixing-this-bug`
`chore/why-did-we-do-this`
`documentation/im-a-weirdo`

You should flow from these branches then take the following paths
`feature/my-thing` -> `develop` -> `main`
`bugfix/it-broke` -> `staging` -> `main`

## Code Style

- **Readability first** — write clear, self-explanatory code. If a name needs a comment to explain it, rename it instead.
- **Comments sparingly** — only comment non-obvious logic. Do not comment what the code already says.
- **Minimize allocations** — avoid unnecessary heap allocations in hot paths. Prefer structs, stackalloc, and reusing existing objects.
- **Use pooling** — use object pools (`Pool.Get<T>` / `Pool.Free`) for frequently created/destroyed objects rather than allocating new instances.

## Pull Requests

- Keep PRs focused — one concern per PR.
- Describe what changed and why in the PR body.
- Ensure the build passes before requesting review.
- Ensure the built mod runs on vanilla, Oxide, and Carbon Rust servers.
