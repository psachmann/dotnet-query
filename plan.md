# v2.0.0 Release Plan

Status of the tree when this was written: `main` at `6bcd727`, clean, last stable release `v1.3.0`,
last prerelease `v2.0.0-beta.3`.

Verified before writing this plan:

- `dotnet build -c Release` — succeeds, 0 warnings
- `dotnet pack -c Release` — succeeds; package validation passes against the `1.3.0` baseline and the
  existing `CompatibilitySuppressions.xml` files cover every intentional break
- `dotnet docfx ./docs/docfx.json` — succeeds with 13 warnings (1 real, 12 cosmetic; see below)
- Both `getting-started.md` code samples compiled against the real `DotNetQuery.Core` — both fail

---

## Phase 1 — Fix the docs

One PR. Nothing here touches shipping code except one XML doc comment.

### Wrong information

- [x] **The retry claim contradicts the implementation.** `DefaultRetryHandler` is a pass-through —
      one attempt, no retry (`src/DotNetQuery.Core/Internals/DefaultRetryHandler.cs`). Fix all three:
  - [x] `README.md:21` — "exponential backoff out of the box"
  - [x] `docs/index.md:22` — same line
  - [x] `docs/doc/guides/retries.md:3` — "Out of the box it uses an exponential backoff strategy",
        contradicted by its own next section ("makes **one attempt** and does not retry")

  `docs/llms.txt:454` already states this correctly and can be used as the wording to copy.

- [x] **`docs/doc/getting-started.md:130` — the mutation sample does not compile.**
      `PostAsJsonAsync<TValue>` takes the *request* as `TValue` and returns `Task<HttpResponseMessage>`:

  ```
  error CS1503: Argument 3: cannot convert from 'CreateUserRequest' to 'UserDto'
  error CS1503: Argument 4: cannot convert from 'CancellationToken' to 'JsonSerializerOptions?'
  ```

  Replacement (compiles clean, 0 warnings):

  ```csharp
  Mutator = async (request, ct) =>
  {
      var response = await httpClient.PostAsJsonAsync("/api/users", request, ct);
      response.EnsureSuccessStatusCode();

      return await response.Content.ReadFromJsonAsync<UserDto>(ct)
          ?? throw new InvalidOperationException("The server returned no user.");
  },
  ```

- [x] **`docs/doc/getting-started.md:74-75` — the query sample warns and has dead code.** `??` applies
      to the `Task`, which is never null, so the `throw` can never fire:

  ```
  warning CS8619: Nullability of 'Task<UserDto?>' doesn't match 'Task<UserDto>'
  ```

  Replacement (compiles clean):

  ```csharp
  Fetcher = async (id, ct) =>
      await httpClient.GetFromJsonAsync<UserDto>($"/api/users/{id}", ct)
      ?? throw new InvalidOperationException("User not found."),
  ```

- [x] **Broken link — `docs/doc/getting-started.md:235`** points at `examples/queries.md`, which only
      exists in the stale `docs/_site` output. docfx flags it:
      `warning InvalidFileLink: Invalid file link:(~/doc/examples/queries.md)`.
      Remove it, or repoint at the samples (see below).

- [x] **Broken link — `[MIT](LICENSE)`**; the file is `LICENSE.md`. Two places:
  - [x] `README.md:105`
  - [x] `docs/doc/contributing.md:124` (`blob/main/LICENSE` → 404 on GitHub)

- [x] **The target framework is documented wrong.** Everything multi-targets `net9.0;net10.0`
      (`Directory.Build.props:3`), and both TFMs are present in every packed nupkg. As written, the
      docs turn away .NET 9 users who are actually supported:
  - [x] `docs/doc/getting-started.md:7` — ".NET 10.0 or later"
  - [x] `docs/llms.txt:14` — "Target framework: net10.0."
  - [x] `CLAUDE.md:266` — "NuGet packages target `net10.0`."

- [x] **`docs/doc/contributing.md` — four stale commands/versions:**
  - [x] `:49` — `dotnet test --collect:"XPlat Code Coverage"` is wrong for TUnit /
        Microsoft.Testing.Platform. CI uses
        `dotnet test -c Release -- --coverage --coverage-output-format cobertura`
  - [x] `:55` and `:64` — `dotnet csharpier .` needs the `format` subcommand; bare invocation just
        prints help
  - [x] `:31` — CSharpier pinned at `1.2.6`; `dotnet-tools.json` says `1.3.0`
  - [x] `:94` — tells contributors to `dotnet tool install -g docfx`; it is already a local tool, so
        `dotnet docfx ./docs/docfx.json`

- [x] **`src/DotNetQuery.Core/IQueryClient.cs` — `Invalidate` XML doc is incomplete.** It says
      "marks all queries matching the given key as stale and triggers a re-fetch", but
      `Query.Invalidate` returns early while the data is still within `StaleTime`
      (`src/DotNetQuery.Core/Internals/Query.cs:170-173`). `introduction.md` and the migration guide
      both document the guard correctly; the interface doc is the odd one out. This one ships in the
      XML docs, so it is worth fixing before 2.0.

### Stale / incomplete for a 2.0 launch

- [x] **`docs/index.md` is still the v1 landing page** — no infinite queries, no MVVM, no DevTools,
      no observability in its feature list. It is the first page on the docs site, and `README.md` is
      substantially ahead of it. Bring the two into sync.

- [x] **Package lists disagree across four documents.** Only `docs/llms.txt` lists all five:
  - `README.md:31-41` — omits `DotNetQuery.Blazor.DevTools`
  - `docs/doc/getting-started.md:13-26` — omits `DotNetQuery.Mvvm`
  - `docs/doc/migrating-to-v2.md:21-27` — omits `DotNetQuery.Mvvm`
  - [x] Make all four identical, and add infinite queries + DevTools to the README feature list

- [x] **`docs/doc/contributing.md` documents none of the four build gates** — no mention of
      `PublicAPI.Unshipped.txt`, package validation, `BannedSymbols.txt`, or the trim/AOT analyzers,
      and none of the husky hooks (no csproj installs them, so a contributor must run
      `dotnet husky install` by hand). These are exactly what fails a first-time contributor's PR
      with a cryptic `RS0016` / `RS0030`. `CLAUDE.md` already has the explanations to draw on.

- [x] **The samples are invisible.** `samples/DotNetQuery.Samples.Blazor` (merged in #78) is linked
      from nowhere; the Avalonia sample gets one passing link from `docs/doc/guides/mvvm.md:225`.
      Link both from `README.md` and `docs/doc/getting-started.md` — the latter is a natural
      replacement for the broken `examples/queries.md` link.

---

## Phase 2 — Cut the release surface

Second PR, after Phase 1 lands.

- [ ] Remove `While 2.0 is in preview, add --prerelease to each command or pass an explicit --version.`
      from `docs/doc/migrating-to-v2.md:28` — it is the only prerelease reference left in the docs
- [ ] Move every `PublicAPI.Unshipped.txt` addition into that project's `PublicAPI.Shipped.txt`
      (Core 119 lines, Mvvm 98, Blazor 27, Extensions.DependencyInjection 1)
- [ ] Delete the four `*REMOVED*` lines in `src/DotNetQuery.Core/PublicAPI.Unshipped.txt:116-119`
      **together with their `Shipped` counterparts** at `PublicAPI.Shipped.txt:170-173` — these are
      the `QueryState<TData>.Create*` factories, removed and re-added only because of the new
      `TData : class` constraint
- [ ] Leave each `Unshipped` file with just its `#nullable enable` header
- [ ] **Leave `PackageValidationBaselineVersion` at `1.3.0`** for this release — comparing 2.0.0
      against the last stable release is exactly the measurement you want. It gets bumped in Phase 5.

### Decision needed: `net9.0` ships untested

`tests/Directory.Build.props:5` pins the test projects to `net10.0` only, while all five packages
ship `net9.0` **and** `net10.0`. The net9 assemblies compile but no test has ever executed against
them. Before a major, pick one:

- [ ] Multi-target the test projects to `net9.0;net10.0`, or
- [ ] Drop `net9.0` from `Directory.Build.props` and ship net10-only (and then keep the TFM doc fixes
      above consistent with that), or
- [ ] Consciously accept the gap and note it in `tests/Directory.Build.props` with a comment

---

## Phase 3 — Verify locally before tagging

Run in this order from a clean tree. **Do not skip `pack`** — see the CI note below.

```bash
dotnet tool restore
dotnet csharpier check .
dotnet build --configuration Release          # expect 0 warnings
dotnet test --configuration Release
dotnet pack --configuration Release --output ./artifacts
dotnet docfx ./docs/docfx.json                # expect 0 InvalidFileLink warnings
```

- [ ] `csharpier check` clean
- [ ] Release build, 0 warnings (`TreatWarningsAsErrors` is on in Release)
- [ ] Full test suite green
- [ ] **`pack` succeeds** — package validation runs here and nowhere else
- [ ] docfx: the remaining `Duplicate source file` warnings for the `PublicAPI.*.txt` files are
      cosmetic (docfx sees the `AdditionalFiles` entry once per TFM) and can be ignored; the
      `InvalidFileLink` warning must be gone

> **Why `pack` matters here:** `.github/workflows/build.yaml` runs restore → format → build → test
> and **never packs**. `EnablePackageValidation` only fires on `dotnet pack`, which happens solely in
> `.github/workflows/release.yaml` — *after* the tag is pushed. A validation failure there leaves you
> with a public tag and no packages on NuGet. It passes today, but re-check after the Phase 2
> Shipped/Unshipped move.

---

## Phase 4 — Release

- [ ] Merge Phase 1 + Phase 2 to `main`; confirm the Build and Docs workflows are green
- [ ] Tag and push:

  ```bash
  git tag v2.0.0
  git push origin v2.0.0
  ```

- [ ] Watch `.github/workflows/release.yaml`: build → test → pack → NuGet push → GitHub Release.
      `prerelease` is derived from `contains(github.ref_name, '-')`, so it correctly resolves to
      `false` for `v2.0.0`
- [ ] Replace the auto-generated release notes. `generate_release_notes: true` produces a commit list,
      which is thin for a major — lead with the migration guide link and the headline changes
      (`TData : class`, infinite queries, the `DotNetQuery.Mvvm` package, telemetry units and tags)
- [ ] Verify all five packages are live on NuGet.org with both `net9.0` and `net10.0` lib folders:
      `DotNetQuery.Core`, `.Extensions.DependencyInjection`, `.Blazor`, `.Blazor.DevTools`, `.Mvvm`
- [ ] Confirm the docs site picked up the Phase 1 changes at
      https://psachmann.github.io/dotnet-query/

---

## Phase 5 — After the release lands

These all require `2.0.0` to exist on NuGet first.

- [ ] Bump `PackageValidationBaselineVersion` to `2.0.0` in `src/Directory.Build.props:26`
- [ ] Drop the `PackageValidationBaselineVersion` override from
      `src/DotNetQuery.Mvvm/DotNetQuery.Mvvm.csproj:10` — its own comment says to do this once the
      shared baseline reaches `2.0.0`
- [ ] Delete the now-satisfied entries in `src/DotNetQuery.Blazor/CompatibilitySuppressions.xml` and
      `src/DotNetQuery.Core/CompatibilitySuppressions.xml` — they list intentional breaks against
      `1.3.0`, which is no longer the baseline
- [ ] Re-run `dotnet pack -c Release` to confirm the new baseline resolves and validates
- [ ] Consider adding a `dotnet pack` step to `.github/workflows/build.yaml` so the breaking-change
      gate runs on PRs rather than at tag time
