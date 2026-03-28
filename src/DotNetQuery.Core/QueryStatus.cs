namespace DotNetQuery.Core;

public enum QueryStatus : byte
{
    Idle = 0,
    Fetching = 1,
    Success = 2,
    Failure = 3,
}
