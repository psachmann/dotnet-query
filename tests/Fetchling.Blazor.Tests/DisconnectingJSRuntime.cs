namespace Fetchling.Blazor.Tests;

using Microsoft.JSInterop;

// BL0016 asks component code to guard JS interop with try/catch. This test double *is* the interop layer
// and throws JSDisconnectedException on purpose, so there is nothing to guard here.
#pragma warning disable BL0016

/// <summary>
/// Minimal <see cref="IJSRuntime"/> whose calls succeed with defaults, except for the identifiers in
/// <c>disconnectOn</c>, which throw <see cref="JSDisconnectedException"/> like a torn-down Blazor Server circuit.
/// Needed because bUnit cannot make <c>import</c> or an <see cref="IJSObjectReference"/>-returning call throw.
/// </summary>
internal sealed class DisconnectingJSRuntime(params string[] disconnectOn) : IJSRuntime
{
    private readonly HashSet<string> _disconnectOn = [.. disconnectOn];

    public List<string> Invocations { get; } = [];

    public ValueTask<TValue> InvokeAsync<TValue>(string identifier, object?[]? args) =>
        ValueTask.FromResult(Complete<TValue>(identifier));

    public ValueTask<TValue> InvokeAsync<TValue>(
        string identifier,
        CancellationToken cancellationToken,
        object?[]? args
    ) => ValueTask.FromResult(Complete<TValue>(identifier));

    // Throws synchronously: callers await the returned task inside their try block, so it surfaces the same way.
    private TValue Complete<TValue>(string identifier)
    {
        Invocations.Add(identifier);

        if (_disconnectOn.Contains(identifier))
        {
            throw new JSDisconnectedException("circuit disconnected");
        }

        object? result = default(TValue);
        if (typeof(TValue) == typeof(IJSObjectReference))
        {
            result = new Module(this);
        }
        else if (typeof(TValue) == typeof(string))
        {
            result = "dark";
        }

        return (TValue)result!;
    }

    private sealed class Module(DisconnectingJSRuntime runtime) : IJSObjectReference
    {
        public ValueTask<TValue> InvokeAsync<TValue>(string identifier, object?[]? args) =>
            ValueTask.FromResult(runtime.Complete<TValue>(identifier));

        public ValueTask<TValue> InvokeAsync<TValue>(
            string identifier,
            CancellationToken cancellationToken,
            object?[]? args
        ) => ValueTask.FromResult(runtime.Complete<TValue>(identifier));

        public ValueTask DisposeAsync()
        {
            runtime.Complete<object?>("dispose");
            return ValueTask.CompletedTask;
        }
    }
}
