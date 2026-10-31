# Migrating from v2 to v3

> **3.0 is not released yet.** This guide tracks the breaking changes merged so far and grows until the release. The current stable version is 2.0.0.

This guide covers what changes when you upgrade from DotNet Query 2.x (last stable release: 2.0.0) to 3.0.

## At a Glance

| Change | Affects you if… | What to do |
|---|---|---|
| [.NET 9 is no longer supported](#net-9-is-no-longer-supported) | your application targets `net9.0` | Move to .NET 10, or stay on 2.0.0 |
| [New members on `IQueryClient`](#new-members-on-iqueryclient) | you implement `IQueryClient` yourself | Implement the new members |

## Breaking Changes

### .NET 9 is no longer supported

All packages now target `net10.0` only. 2.x shipped `net9.0` and `net10.0` assemblies side by side; 3.0 drops the `net9.0` one, so a project targeting `net9.0` fails at restore:

```
error NU1202: Package DotNetQuery.Core 3.0.0 is not compatible with net9.0 (.NETCoreApp,Version=v9.0).
```

**Why:** .NET 9 reaches end of support on 10 November 2026, and targeting .NET 10 alone lets the library use .NET 10 APIs without fallbacks.

**How to migrate:** retarget your application to `net10.0`. There is no API change tied to this — code that built against the `net10.0` assemblies of 2.0.0 is unaffected.

If you have to stay on .NET 9, stay on 2.0.0. It is the last release that supports .NET 9, and there will be no further 2.x releases.

### New members on `IQueryClient`

`IQueryClient` gained members. This only affects code that implements it itself — decorators, wrappers, or hand-written test fakes. Mocking libraries pick the new members up automatically. An implementation written against 2.0.0 no longer builds:

```
error CS0535: 'LoggingQueryClient' does not implement interface member 'IQueryClient.CreateMutation<TArgs, TData, TProgress>(MutationOptions<TArgs, TData, TProgress>)'
```

| Interface | New member |
|---|---|
| `IQueryClient` | `IMutation<TArgs, TData, TProgress> CreateMutation<TArgs, TData, TProgress>(MutationOptions<TArgs, TData, TProgress> options)` |

**Why:** mutations can now report progress (see [Reporting Progress](guides/mutations.md#reporting-progress)). A progress-reporting mutation has its own options, state, and interface types, so the client needs a factory method for it, beside the existing one.

**How to migrate:** a decorator forwards the new method like the existing ones:

```csharp
public sealed class LoggingQueryClient(IQueryClient inner) : IQueryClient
{
    public IMutation<TArgs, TData, TProgress> CreateMutation<TArgs, TData, TProgress>(
        MutationOptions<TArgs, TData, TProgress> options
    ) => inner.CreateMutation(options);

    // CreateQuery, CreateInfiniteQuery, the existing CreateMutation, Invalidate, and Dispose are unchanged.
}
```

## Behaviour Changes

These compile without errors but can change what your application does at runtime.

### Fixes you may notice

- **Executing a mutation again while it runs no longer flickers its state.** `Execute` while a run is in flight cancels that run in favour of the new one. In 2.x the cancelled run still published `Idle` (or its result, if the mutator ignored the token) once it wound down, so the mutation briefly looked idle — or finished — while the new run was still going, and a command gated on `IsRunning` could re-enable early. A superseded run now leaves `State` alone — and therefore also `Success`, `Failure`, `Settled`, and `InvalidateKeys`, which are driven by it. Its callbacks still fire as before: `OnSettled` always, and `OnSuccess` / `OnFailure` if the mutator ignored the token and completed anyway. If you relied on that stray emission — for example, waiting for `Idle` after the second `Execute` — wait for the second run's `Settled` instead.
