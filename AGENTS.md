# AGENTS.md

Guidance for AI coding agents working in this repository.

## Project

Fetchling is a [TanStack Query](https://tanstack.com/query)-inspired async data fetching and state management library for .NET and Blazor: automatic caching, background refetching, stale-while-revalidate, reactive state, built on [Rx.NET](https://github.com/dotnet/reactive). Packages multi-target `net10.0` and `net11.0`; versions come from `MinVer` (git tags, prefix `v`).

| Project (`src/`) | Purpose |
|---|---|
| `Fetchling` | Code-less metapackage: `Core` + `Extensions.DependencyInjection` |
| `Fetchling.Core` | `IQueryClient`, `IQuery`, `IInfiniteQuery`, `IMutation`, cache, observability |
| `Fetchling.Extensions.DependencyInjection` | `AddFetchling()`; Singleton (CSR) or Scoped (SSR) |
| `Fetchling.Blazor` | `<Suspense>`, `<Transition>`, `<InfiniteSuspense>`, `<InfiniteTransition>`, `<QueryRefreshMonitor>` |
| `Fetchling.Blazor.DevTools` | `<QueryDevTools>` live cache inspector |
| `Fetchling.Mvvm` | INPC view models for MAUI/WPF/WinUI/UNO/Avalonia |

Layer-specific rules live next to the code: `src/Fetchling.Core/AGENTS.md`, `src/Fetchling.Mvvm/AGENTS.md`.

## Commands

Tasks are defined in `mise.toml` (`mise tasks` lists them). Each wraps a plain `dotnet` command. Prefer `mise run <task>` over calling `dotnet` directly: mise also provisions the SDK pinned in `global.json`, so a bare `dotnet` fails when that SDK isn't installed system-wide (use `mise exec -- dotnet ...` for anything without a task).

```bash
mise run restore-tools                  # dotnet tool restore
mise run build-release                  # dotnet build --configuration Release
mise run test-release                   # dotnet test --configuration Release
mise run test-filter Core               # one test project
mise run test-filter Core QueryCache    # ... only classes matching *QueryCache* (TUnit --treenode-filter)
mise run format-check                   # dotnet csharpier check .
mise run format-fix                     # dotnet csharpier format .  (the `format` subcommand is required)
mise run ci                             # format check, Release build, tests + coverage, pack
mise run public-api                     # regenerate PublicAPI.Unshipped.txt entries
mise run suppressions                   # regenerate CompatibilitySuppressions.xml
```

Git hooks (Husky: CSharpier on commit, tests on push) call plain `dotnet`, so run `git commit` and `git push` as `mise exec -- git ...` unless mise is activated in your shell. Never bypass the hooks with `--no-verify`.

CSharpier is a CI gate: run `format-fix` before committing. Indentation is 4 spaces for C#/Razor/JS, 2 for XML/config (`.editorconfig`).

## Build gates (Release build, `src/` only)

- **Public API tracking.** Any public member change fails the build (`RS0016`/`RS0017`) until `PublicAPI.Unshipped.txt` is updated; run `mise run public-api`. `dotnet format` can't fix `.razor` files: paste the `RS0016` message text into `src/Fetchling.Blazor/PublicAPI.Unshipped.txt` by hand. `Shipped.txt` is the 3.0.0 surface; `Unshipped.txt` is the delta of the upcoming release (additions plus `*REMOVED*` lines). `Fetchling.Blazor.DevTools` is untracked on purpose.
- **Banned symbols (`IScheduler` invariant).** `DateTime.Now/UtcNow/Today`, `DateTimeOffset.Now/UtcNow`, `Thread.Sleep` and `Task.Delay` fail the build (`RS0030`, list in `src/BannedSymbols.txt`). All time goes through the injected `IScheduler` so tests can use `TestScheduler`. Tests are exempt.
- **Trim/AOT analyzers.** `src/` projects are trim- and AOT-clean (DevTools is the one documented exception). Fix a new `IL` diagnostic; don't suppress it.
- **Explicit per-project switches.** Every `src/` `.csproj` must set `TrackPublicApi` and `IsAotCompatible` itself; `RequireExplicitProjectProperties` fails the build otherwise. To add a gate of this kind, extend that target.
- **Package validation** runs on `dotnet pack` against the last stable release. The baseline is `PackageValidationBaselineVersion` in `src/Directory.Build.props` (currently `3.0.0`). Record an intentional break with `mise run suppressions` and in the migration guide.

## Breaking changes

A PR owes an entry in the upcoming major's migration guide `docs/doc/migrating-to-v<N>.md` (same PR, not at release prep; there is none until the first v4 break lands, so create it then) if it adds a `*REMOVED*` line to a `PublicAPI.Unshipped.txt`, adds a `CompatibilitySuppressions.xml` entry, or silently changes behaviour or telemetry (names, units, tags). Format and tone: follow `migrating-to-v2.md`. There is deliberately no `CHANGELOG.md`; GitHub generates release notes from PR titles.

Release-prep steps (cutting a stable release, docs wording for an unreleased major, Context7 re-index): see `docs/doc/contributing.md`.

## Testing

TUnit (not xUnit/NUnit), bUnit for Blazor components, `TUnit.Mocks` / `TUnit.Mocks.Logging` for mocks, `Microsoft.Reactive.Testing.TestScheduler` for virtual time. CI runs `dotnet test --configuration Release --results-directory ./coverage -- --coverage --coverage-output-format cobertura`; `./coverage` and `./TestResults` are gitignored.

## Other

- DI registration: `services.AddFetchling(o => { o.ExecutionMode = QueryExecutionMode.Ssr; })` (Scoped for SSR, Singleton for CSR).
- `AGENTS.md` and `contributing.md` are excluded from Context7 indexing (`context7.json`); keep its `rules` to consumer-facing gotchas.
