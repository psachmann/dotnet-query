namespace DotNetQuery.Core;

public enum MutationStatus : byte
{
    Idle = 0,
    Running = 1,
    Success = 2,
    Failure = 3,
}
