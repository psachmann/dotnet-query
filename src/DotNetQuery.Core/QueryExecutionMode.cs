namespace DotNetQuery.Core;

public enum QueryExecutionMode
{
    /// <summary>Client-Side Rendering (WebAssembly) — registers as Singleton.</summary>
    Csr,

    /// <summary>Server-Side Rendering (Blazor Server) — registers as Scoped.</summary>
    Ssr,
}
