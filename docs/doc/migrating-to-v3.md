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
error CS0535: 'LoggingQueryClient' does not implement interface member 'IQueryClient.CreateStreamedQuery<TArgs, TData, TChunk>(StreamedQueryOptions<TArgs, TData, TChunk>)'
```

| Interface | New member |
|---|---|
| `IQueryClient` | `IQuery<TArgs, TData> CreateStreamedQuery<TArgs, TData, TChunk>(StreamedQueryOptions<TArgs, TData, TChunk> options) where TData : class` |

**Why:** the client can now create [streamed queries](guides/streamed-queries.md), whose fetcher yields chunks that are folded into the data as they arrive. They have their own options type, so the client needs a factory method for them.

**How to migrate:** a decorator forwards the new method like the existing ones:

```csharp
public sealed class LoggingQueryClient(IQueryClient inner) : IQueryClient
{
    public IQuery<TArgs, TData> CreateStreamedQuery<TArgs, TData, TChunk>(
        StreamedQueryOptions<TArgs, TData, TChunk> options
    )
        where TData : class => inner.CreateStreamedQuery(options);

    // CreateQuery, CreateInfiniteQuery, CreateMutation, Invalidate, and Dispose are unchanged.
}
```
