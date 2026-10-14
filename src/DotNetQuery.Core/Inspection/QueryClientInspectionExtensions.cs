using System.Diagnostics.CodeAnalysis;
using DotNetQuery.Core.Inspection;

namespace DotNetQuery.Core;

/// <summary>Extension methods for inspecting the cache of an <see cref="IQueryClient"/>.</summary>
public static class QueryClientInspectionExtensions
{
    /// <summary>
    /// Gets the cache inspector of <paramref name="client"/>. Clients created by
    /// <see cref="QueryClientFactory"/> or <c>AddDotNetQuery()</c> always support inspection; a custom
    /// <see cref="IQueryClient"/> implementation (or a mock) supports it only if it also implements
    /// <see cref="IQueryCacheInspector"/>.
    /// </summary>
    /// <param name="client">The client to inspect.</param>
    /// <param name="inspector">When this method returns <c>true</c>, the client's cache inspector.</param>
    /// <returns><c>true</c> when <paramref name="client"/> supports inspection; otherwise <c>false</c>.</returns>
    public static bool TryGetInspector(
        this IQueryClient client,
        [NotNullWhen(true)] out IQueryCacheInspector? inspector
    )
    {
        ArgumentNullException.ThrowIfNull(client);

        inspector = client as IQueryCacheInspector;
        return inspector is not null;
    }
}
