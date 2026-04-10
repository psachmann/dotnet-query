namespace DotNetQuery.Core;

/// <summary>
/// Represents the lifecycle state of a mutation execution.
/// </summary>
public enum MutationStatus : byte
{
    /// <summary>The mutation has not been executed yet, or has been reset.</summary>
    Idle = 0,

    /// <summary>The mutation is currently executing.</summary>
    Running = 1,

    /// <summary>The last execution completed successfully.</summary>
    Success = 2,

    /// <summary>The last execution failed with an error.</summary>
    Failure = 3,
}
