# Migrating from v2 to v3

> **3.0 is not released yet.** This guide tracks the breaking changes merged so far and grows until the release. The current stable version is 2.0.0.

This guide covers what changes when you upgrade from DotNet Query 2.x (last stable release: 2.0.0) to 3.0.

## At a Glance

| Change | Affects you if… | What to do |
|---|---|---|
| [.NET 9 is no longer supported](#net-9-is-no-longer-supported) | your application targets `net9.0` | Move to .NET 10, or stay on 2.0.0 |

## Breaking Changes

### .NET 9 is no longer supported

All packages now target `net10.0` only. 2.x shipped `net9.0` and `net10.0` assemblies side by side; 3.0 drops the `net9.0` one, so a project targeting `net9.0` fails at restore:

```
error NU1202: Package DotNetQuery.Core 3.0.0 is not compatible with net9.0 (.NETCoreApp,Version=v9.0).
```

**Why:** .NET 9 reaches end of support on 10 November 2026, and targeting .NET 10 alone lets the library use .NET 10 APIs without fallbacks.

**How to migrate:** retarget your application to `net10.0`. There is no API change tied to this — code that built against the `net10.0` assemblies of 2.0.0 is unaffected.

If you have to stay on .NET 9, stay on 2.0.0. It is the last release that supports .NET 9, and there will be no further 2.x releases.
