namespace DotNetQuery.Core.Internals;

internal sealed class Mutation<TArgs, TData>(
    MutationOptions<TArgs, TData> options,
    QueryClientOptions globalOptions,
    QueryInstrumentation instrumentation
)
    : MutationCore<TArgs, TData, Unit, MutationState<TData>>(MergeOptions(options, globalOptions), instrumentation),
        IMutation<TArgs, TData>
{
    public static EffectiveMutationOptions<TArgs, TData, Unit> MergeOptions(
        MutationOptions<TArgs, TData> options,
        QueryClientOptions globalOptions
    )
    {
        var mutator = options.Mutator;

        return new()
        {
            Mutator = (args, _, ct) => mutator(args, ct),
            Name = options.Name ?? typeof(TArgs).Name,
            RetryHandler = options.RetryHandler ?? globalOptions.RetryHandler,
            IsEnabled = options.IsEnabled,
            InvalidateKeys = options.InvalidateKeys ?? [],
            OnMutate = options.OnMutate ?? delegate { },
            OnSuccess = options.OnSuccess ?? delegate { },
            OnFailure = options.OnFailure ?? delegate { },
            OnSettled = options.OnSettled ?? delegate { },
        };
    }

    protected override MutationState<TData> CreateIdle() => MutationState<TData>.CreateIdle();

    protected override MutationState<TData> CreateRunning(Unit progress) => MutationState<TData>.CreateRunning();

    protected override MutationState<TData> CreateSuccess(TData data, Unit progress) =>
        MutationState<TData>.CreateSuccess(data);

    protected override MutationState<TData> CreateFailure(Exception error, Unit progress) =>
        MutationState<TData>.CreateFailure(error);
}

internal sealed class Mutation<TArgs, TData, TProgress>(
    MutationOptions<TArgs, TData, TProgress> options,
    QueryClientOptions globalOptions,
    QueryInstrumentation instrumentation
)
    : MutationCore<TArgs, TData, TProgress, MutationState<TData, TProgress>>(
        MergeOptions(options, globalOptions),
        instrumentation
    ),
        IMutation<TArgs, TData, TProgress>
{
    public static EffectiveMutationOptions<TArgs, TData, TProgress> MergeOptions(
        MutationOptions<TArgs, TData, TProgress> options,
        QueryClientOptions globalOptions
    )
    {
        return new()
        {
            Mutator = options.Mutator,
            Name = options.Name ?? typeof(TArgs).Name,
            RetryHandler = options.RetryHandler ?? globalOptions.RetryHandler,
            IsEnabled = options.IsEnabled,
            InvalidateKeys = options.InvalidateKeys ?? [],
            OnMutate = options.OnMutate ?? delegate { },
            OnSuccess = options.OnSuccess ?? delegate { },
            OnFailure = options.OnFailure ?? delegate { },
            OnSettled = options.OnSettled ?? delegate { },
        };
    }

    protected override MutationState<TData, TProgress> CreateIdle() => MutationState<TData, TProgress>.CreateIdle();

    protected override MutationState<TData, TProgress> CreateRunning(TProgress? progress) =>
        MutationState<TData, TProgress>.CreateRunning(progress);

    protected override MutationState<TData, TProgress> CreateSuccess(TData data, TProgress? progress) =>
        MutationState<TData, TProgress>.CreateSuccess(data, progress);

    protected override MutationState<TData, TProgress> CreateFailure(Exception error, TProgress? progress) =>
        MutationState<TData, TProgress>.CreateFailure(error, progress);
}
