set shell := ["bash", "-euo", "pipefail", "-c"]

solution := justfile_directory() / "DotNetQuery.slnx"
blazor_sample := justfile_directory() / "samples/DotNetQuery.Samples.Blazor/DotNetQuery.Samples.Blazor.csproj"
avalonia_sample := justfile_directory() / "samples/DotNetQuery.Samples.Avalonia/DotNetQuery.Samples.Avalonia.csproj"

# List all recipes
default:
    @just --list --unsorted

# --- Mirrors .vscode/tasks.json ---------------------------------------------

# Restore local dotnet tools (CSharpier, docfx, ...)
restore-tools:
    dotnet tool restore

# Restore NuGet packages
restore-packages:
    dotnet restore {{ solution }}

# Build the solution in Debug
build-debug: (build "Debug")

# Build the solution in Release (enables the public API / banned symbol gates)
build-release: (build "Release")

# Run all tests in Debug
test-debug: (test "Debug")

# Run all tests in Release
test-release: (test "Release")

# Check formatting (CI gate)
format-check: restore-tools
    dotnet csharpier check .

# Fix formatting
format-fix: restore-tools
    dotnet csharpier format .

# Clean Debug and Release outputs
clean:
    dotnet clean {{ solution }} --configuration Debug
    dotnet clean {{ solution }} --configuration Release

# Run the Blazor sample
run-blazor:
    dotnet run --project {{ blazor_sample }}

# Run the Avalonia sample
run-avalonia:
    dotnet run --project {{ avalonia_sample }}

# --- Extras -----------------------------------------------------------------

# Build the solution with the given configuration
build config="Debug":
    dotnet build {{ solution }} --configuration {{ config }}

# Run all tests with the given configuration
test config="Debug":
    dotnet test {{ solution }} --configuration {{ config }}

# Run tests in one project, optionally only classes whose name contains `filter`, e.g. `just test-filter Core QueryCache`
test-filter project filter="" config="Release":
    dotnet test tests/DotNetQuery.{{ project }}.Tests --configuration {{ config }} \
        {{ if filter != "" { "-- --treenode-filter \"/*/*/*" + filter + "*/*\"" } else { "" } }}

# Run all tests with cobertura coverage, as CI does (output in ./coverage)
coverage:
    dotnet test {{ solution }} --configuration Release --results-directory ./coverage -- --coverage --coverage-output-format cobertura

# Pack NuGet packages into ./artifacts (runs package validation against the baseline)
pack: build-release
    dotnet pack {{ solution }} --configuration Release --no-build --output ./artifacts

# Regenerate CompatibilitySuppressions.xml for intentional breaking changes
suppressions:
    dotnet pack {{ solution }} --configuration Release -p:ApiCompatGenerateSuppressionFile=true --output ./artifacts

# Add missing entries to PublicAPI.Unshipped.txt (`.razor` members must still be added by hand)
public-api:
    #!/usr/bin/env bash
    set -euo pipefail
    count() { cat src/*/PublicAPI.Unshipped.txt | wc -l; }
    previous=-1
    current=$(count)
    while [ "$current" != "$previous" ]; do
        previous=$current
        dotnet format analyzers {{ solution }} --diagnostics RS0016 --severity warn || true
        current=$(count)
        echo "PublicAPI.Unshipped.txt lines: $previous -> $current"
    done

# Run the same gates as the CI build workflow: format, build, test, pack
ci: format-check build-release coverage
    dotnet pack {{ solution }} --configuration Release --no-build --output ./artifacts

# Build the docfx site into docs/_site
docs: build-release restore-tools
    dotnet docfx ./docs/docfx.json

# Build and serve the docfx site locally
docs-serve: build-release restore-tools
    dotnet docfx ./docs/docfx.json --serve

# Run the Blazor sample with hot reload
watch-blazor:
    dotnet watch --project {{ blazor_sample }}

# Show outdated NuGet packages
outdated: restore-tools
    dotnet outdated

# Remove all build output, including bin/obj, coverage, artifacts and the docs site
clean-all:
    find src tests samples \( -name bin -o -name obj \) -type d -prune -exec rm -rf {} +
    rm -rf ./artifacts ./coverage ./TestResults ./docs/_site ./docs/api
