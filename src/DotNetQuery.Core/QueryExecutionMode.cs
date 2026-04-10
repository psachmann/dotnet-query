namespace DotNetQuery.Core;

/// <summary>
/// Determines how the <see cref="IQueryClient"/> is registered in the DI container,
/// matching the Blazor rendering mode in use.
/// </summary>
public enum QueryExecutionMode
{
    /// <summary>Client-Side Rendering (WebAssembly) — registers as Singleton.</summary>
    Csr,

    /// <summary>Server-Side Rendering (Blazor Server) — registers as Scoped.</summary>
    Ssr,
}
