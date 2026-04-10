namespace DotNetQuery.Core;

/// <summary>
/// Represents the lifecycle state of a query fetch operation.
/// </summary>
public enum QueryStatus : byte
{
    /// <summary>The query has not been fetched yet, or has been reset.</summary>
    Idle = 0,

    /// <summary>The query is currently fetching data.</summary>
    Fetching = 1,

    /// <summary>The last fetch completed successfully.</summary>
    Success = 2,

    /// <summary>The last fetch failed with an error.</summary>
    Failure = 3,
}
