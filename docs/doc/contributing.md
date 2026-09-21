# Contributing

Contributions are very welcome! Whether it is a bug fix, a new feature, improved documentation, or just a question — please feel free to open an issue or pull request.

## Getting the Code

```bash
git clone https://github.com/psachmann/dotnet-query.git
cd dotnet-query
```

## Setting Up Your Dev Environment

### Option A: Nix (recommended for reproducibility)

A [Nix flake](https://github.com/psachmann/dotnet-query/blob/main/flake.nix) is provided with all required tools pinned to exact versions. With [Nix](https://nixos.org/) and [direnv](https://direnv.net/) installed, simply run:

```bash
direnv allow
```

The environment activates automatically whenever you enter the directory.

### Option B: Manual

You need:

| Tool | Version |
|------|---------|
| .NET SDK | 10.0 (see [global.json](https://github.com/psachmann/dotnet-query/blob/main/global.json)) |
| CSharpier | 1.3.0 (installed as a local dotnet tool) |

After cloning, restore the local tools and packages:

```bash
dotnet tool restore
dotnet restore
```

## Git Hooks

The repo ships [husky](https://alirezanet.github.io/Husky.Net/) hook definitions in `.husky/`, but no
project wires them up automatically — install them once per clone:

```bash
dotnet husky install
```

This adds a pre-commit hook that runs `dotnet csharpier format` on staged files, and a pre-push hook
that runs the full test suite (`dotnet test -c Release`). Both mirror checks CI also runs, so the
hooks just let you catch a formatting or test failure before pushing instead of after.

## Common Commands

```bash
# Build
dotnet build

# Run all tests
dotnet test

# Run tests with coverage
dotnet test --configuration Release -- --coverage --coverage-output-format cobertura

# Check formatting
dotnet csharpier check .

# Fix formatting
dotnet csharpier format .

# Build the documentation site
dotnet docfx docs/docfx.json
```

## Code Style

The project uses [CSharpier](https://csharpier.com/) for formatting, enforced as a CI gate and as a husky pre-commit hook (see [Git Hooks](#git-hooks) below). Run `dotnet csharpier format .` locally before pushing to avoid the CI failing on formatting.

There is no extensive style guide beyond what CSharpier enforces — just try to follow the patterns already present in the codebase.

## Build Gates

Four gates guard the shipping surface under `src/` (tests and samples are unaffected). Any of them can
turn a change that builds locally into a Release-build failure, so it's worth knowing what each one is
before your PR trips it:

- **Public API tracking** — every `src/` project (except DevTools) lists its entire public surface in
  `PublicAPI.Shipped.txt` / `PublicAPI.Unshipped.txt`. Adding, removing, or changing a public member
  fails the Release build (`RS0016` / `RS0017`) until you add or remove the line. Apply the IDE's "Add
  to public API" code fix, or run:

  ```bash
  dotnet format analyzers DotNetQuery.slnx --diagnostics RS0016 --severity warn
  ```

  This can't touch `.razor` files — `<Suspense>`-style component members go into
  `src/DotNetQuery.Blazor/PublicAPI.Unshipped.txt` by hand, using the exact line the `RS0016` message
  gives you.

- **Package validation** — `dotnet pack` diffs each assembly against the last stable release and fails
  on an unintentional breaking change. It only runs on `pack`, not `build`, so run
  `dotnet pack -c Release` locally if your change touches a public member. An intentional break needs a
  suppression file, not a weakened gate:

  ```bash
  dotnet pack -c Release -p:GenerateCompatibilitySuppressionFile=true
  ```

- **Banned symbols** (`src/BannedSymbols.txt`) — bans ambient time (`DateTime.Now`/`.UtcNow`, etc.) and
  blocking waits (`Thread.Sleep`, `Task.Delay`) as `RS0030`. All time in `src/` must flow through the
  injected `IScheduler` so tests can drive it with `TestScheduler` — this gate is what catches a stray
  `DateTime.UtcNow` before it ships. Test code is exempt.

- **Trim / AOT analyzers** — every `src/` project except DevTools claims trim and AOT compatibility.
  A construct that can't be statically analyzed for trimming fails the Release build rather than
  surfacing later as a runtime failure in a consumer's trimmed or AOT-published app.

## Making Changes

1. **Fork** the repository and create a branch from `main`.
2. **Write tests** for your change. All new behavior should be covered by tests.
3. **Run the full test suite** to make sure nothing is broken: `dotnet test`.
4. **Check formatting**: `dotnet csharpier check .`
5. **Open a pull request** against `main` with a clear description of what you changed and why.

### Adding a New Feature

If you are thinking about a larger feature or a change to the public API, please **open an issue first** to discuss it. That saves everyone time and avoids the frustration of a PR being declined after significant work.

### Fixing a Bug

1. Open an issue describing the bug (if one does not exist already).
2. Add a failing test that reproduces the bug.
3. Fix the bug so the test passes.
4. Open a pull request linking to the issue.

### Improving Documentation

Documentation lives in the `docs/doc/` directory as Markdown files. Changes to documentation are just as valuable as code changes — please feel free to open PRs for typos, unclear explanations, missing examples, or anything else.

## Running the Documentation Site Locally

DocFX is already restored as a local tool by `dotnet tool restore` (see [Setting Up Your Dev
Environment](#setting-up-your-dev-environment)).

```bash
# Build and serve locally
dotnet docfx docs/docfx.json --serve
```

Then open `http://localhost:8080` in your browser.

## CI / CD

The [build pipeline](https://github.com/psachmann/dotnet-query/actions/workflows/build.yaml) runs on every push and pull request to `main`:

1. Restore tools and packages
2. CSharpier format check
3. Release build (warnings are treated as errors in Release mode)
4. Tests with code coverage (Cobertura format)
5. Coverage upload to [Codecov](https://codecov.io/gh/psachmann/dotnet-query)

Pull requests must pass all CI checks before merging.

## Reporting Issues

Found a bug? Have a question? Please [open an issue](https://github.com/psachmann/dotnet-query/issues/new) with:
- a clear description of the problem or question,
- a minimal reproduction (if it is a bug),
- the .NET version and platform you are using.

## License

By contributing to DotNet Query, you agree that your contributions will be licensed under the [MIT License](https://github.com/psachmann/dotnet-query/blob/main/LICENSE.md).
