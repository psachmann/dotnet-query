namespace DotNetQuery.Core.Internals;

/// <summary>
/// The members <see cref="MutationCore{TArgs,TData,TProgress,TState}"/> reads from a state snapshot to
/// derive <c>Success</c>, <c>Failure</c> and <c>Settled</c>. Implemented implicitly by both public
/// state records, so the engine works with either shape without projecting one into the other.
/// </summary>
internal interface IMutationState<TData>
{
    public MutationStatus Status { get; }

    public TData? CurrentData { get; }

    public Exception? Error { get; }
}
