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
| CSharpier | 1.2.6 (installed as a local dotnet tool) |

After cloning, restore the local tools and packages:

```bash
dotnet tool restore
dotnet restore
```

## Common Commands

```bash
# Build
dotnet build

# Run all tests
dotnet test

# Run tests with coverage
dotnet test --collect:"XPlat Code Coverage"

# Check formatting
dotnet csharpier check .

# Fix formatting
dotnet csharpier .

# Build the documentation site
docfx docs/docfx.json
```

## Code Style

The project uses [CSharpier](https://csharpier.com/) for formatting. It runs automatically as a pre-commit check in CI. Run `dotnet csharpier .` locally before pushing to avoid the CI failing on formatting.

There is no extensive style guide beyond what CSharpier enforces — just try to follow the patterns already present in the codebase.

## Project Structure

See the [Project Structure](project-structure.md) page for a full map of the repository.

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

```bash
# Install DocFX if you do not have it
dotnet tool install -g docfx

# Build and serve locally
docfx docs/docfx.json --serve
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

By contributing to DotNet Query, you agree that your contributions will be licensed under the [MIT License](https://github.com/psachmann/dotnet-query/blob/main/LICENSE).
