# Fetchling rename — follow-ups

## Waiting on others

- [ ] **NuGet prefix reservation:** wait for the reply from account@nuget.org (`Fetchling` + `Fetchling.*`).
  - [ ] Once granted, check that all six packages show the reserved-prefix checkmark on nuget.org.
  - [ ] Confirm via `curl -s "https://azuresearch-usnc.nuget.org/query?q=packageid:Fetchling.Core&prerelease=true" | jq '.data[].verified'` → `true`.
- [x] **NuGet search indexing:** check that the `Fetchling*` packages appear in nuget.org search (expected within a few hours of the first publish; if still missing after a day, look into it).

## Check soon

- [x] **Codecov:** the `main` build after #135 uploaded coverage for the renamed repo, and the README badge shows a value.
- [x] **Old Context7 project** `/psachmann/dotnet-query`: create a redirect to `/psachmann/fetchling`.
- [ ] **DevTools screenshots** (`docs/images/fetchling-blazor-devtools-*.png`): retake them if they still show the old name.

## At 3.0.0 stable

- [ ] **Deprecate** `DotNetQuery.Core`, `.Extensions.DependencyInjection`, `.Blazor`, `.Blazor.DevTools`, `.Mvvm` on nuget.org.
  - Reason: *Legacy*; alternative: the matching `Fetchling.*` package. Apply to all versions.
- [ ] **Package validation:** set `PackageValidationBaselineVersion` to `3.0.0` in `src/Directory.Build.props`.
  - Remove the "no baseline yet" notes there and in `CLAUDE.md`.
- [ ] **Public API files:** move `PublicAPI.Unshipped.txt` into `PublicAPI.Shipped.txt` (delete `*REMOVED*` lines together with their `Shipped` counterparts).
  - Remove the "2.0.0 surface written under the Fetchling names" note in `CLAUDE.md`.
- [ ] **Migration guide:**
  - Remove the "not released yet" note from `docs/doc/migrating-to-v3.md`.
  - Link it from `docs/index.md`, `docs/llms.txt`, and the "Formerly DotNetQuery" note in `README.md`. Use the full URL `https://psachmann.github.io/fetchling/doc/migrating-to-v3.html`, since the README is shown on nuget.org.
  - Reword "the upcoming 3.0" mentions in the docs (`getting-started.md`, `llms.txt`).
- [ ] **Context7:**
  - Add `v3.0.0` to `previousVersions` in `context7.json`.
  - Update the versioned ID in `README.md` and `docs/llms.txt`.

## Do not

- **Don't create a new repo named `dotnet-query`.** It would break GitHub's redirect from the old repo URL, which clones, the 2.0.0 package metadata, and old links still rely on.

## Optional

- [x] Add `fetchling`, `tanstack-query`, `reactive` to the GitHub repo topics.
- [x] Rename the local folder `~/Projects/dotnet-query` → `~/Projects/fetchling`.
