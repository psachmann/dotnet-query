# Introduction

Welcome to **DotNet Query** — a library that takes the hard parts out of async data management in .NET and Blazor.

## The Problem

Every app that talks to an API ends up with the same boilerplate:

```csharp
private bool _isLoading;
private UserDto? _user;
private Exception? _error;

public async Task LoadUserAsync(int id)
{
    _isLoading = true;
    try
    {
        _user = await _userService.GetByIdAsync(id);
    }
    catch (Exception ex)
    {
        _error = ex;
    }
    finally
    {
        _isLoading = false;
        StateHasChanged();
    }
}
```

And that is just for one endpoint. When you add caching, background refresh, retry logic, cache invalidation after form submissions, and sharing cached data between components — the complexity explodes fast.

DotNet Query handles all of that for you so you can focus on what your app actually does.

## Inspiration

DotNet Query is directly inspired by [TanStack Query](https://tanstack.com/query) (formerly React Query) — one of the most popular data-fetching libraries in the JavaScript ecosystem. The same mental model translates naturally to .NET, powered by [Rx.NET](https://github.com/dotnet/reactive) observables instead of React's render cycle.

If you have used TanStack Query before, many concepts will feel familiar. If you have not, no worries — this documentation covers everything from scratch.

## Core Concepts

There are a handful of concepts that underpin everything in DotNet Query. Once you understand these, the rest falls into place quickly.

### Queries

A **query** is a declarative description of an async read operation. You define:
- a **key** that uniquely identifies the cached result,
- a **fetcher** that knows how to actually fetch the data.

DotNet Query takes care of everything else: when to fetch, when to cache, when to refetch in the background, and what state to expose while all of that is happening.

Queries expose their current state as an `IObservable<QueryState<TData>>` stream. The state is always one of four values:

| Status | Meaning |
|--------|---------|
| `Idle` | No args have been pushed yet, or the query has been reset. |
| `Fetching` | A fetch is currently in progress. |
| `Success` | The last fetch completed successfully. |
| `Failure` | The last fetch failed. |

### Mutations

A **mutation** is a declarative description of an async write operation — a form submission, a delete, an update. Mutations:
- have their own state machine (`Idle → Running → Success/Failure`),
- can automatically invalidate related query cache entries on success,
- support lifecycle callbacks (`OnSuccess`, `OnFailure`, `OnSettled`).

### Query Keys

A **query key** (`QueryKey`) is an immutable, equality-comparable value that identifies a cache entry. It is composed of one or more parts:

```csharp
QueryKey.From("users")              // all users
QueryKey.From("users", 42)          // user with id 42
QueryKey.From("users", 42, "posts") // posts for user 42
```

Keys drive caching and invalidation. Two queries with the same key share a single fetch and cache entry. Calling `Invalidate(QueryKey.From("users"))` marks every matching entry as stale.

### The Cache

The **cache** lives inside `IQueryClient`. When you create a query and push args to it, the client:
1. Derives the key from the args using your `KeyFactory`.
2. Looks up (or creates) the cache entry for that key.
3. Decides whether a fetch is needed, based on **stale time**.

Data stays in the cache even after all subscribers unsubscribe, for a configurable **cache time** (default: 5 minutes). This means re-subscribing later is instant — the data is already there.

### Stale Time vs Cache Time

These two settings are easy to confuse, but they control different things:

- **Stale time** — how long fetched data is considered "fresh". Within this window, re-subscribing or calling `Invalidate()` does nothing. Defaults to `TimeSpan.Zero` (immediately stale).
- **Cache time** — how long data stays in memory *after the last subscriber unsubscribes*. After this window, the entry is evicted. Defaults to 5 minutes.

### Reactive State

DotNet Query is built on Rx.NET. Every query and mutation exposes `IObservable` streams:

```csharp
query.State.Subscribe(state => { /* every transition */ });
query.Success.Subscribe(data => { /* unwrapped data on success */ });
query.Failure.Subscribe(error => { /* exception on failure */ });
query.Settled.Subscribe(state => { /* after any fetch completes */ });
```

This means you can compose, filter, debounce, and combine query streams just like any other observable — no special APIs required.

## When to Use DotNet Query

DotNet Query is a great fit when your app:
- fetches data from APIs or other async sources,
- needs to share and cache data across multiple components or services,
- benefits from background refresh or automatic retry,
- uses Blazor and wants declarative loading/error states.

It is less useful for purely synchronous data or simple in-process state that never touches a network or database.

## What is Not Included

DotNet Query does not prescribe how you structure your services, how you authenticate, or how you serialize data. It slots in alongside your existing `HttpClient`, `IRepository`, or whatever data access pattern you prefer.
