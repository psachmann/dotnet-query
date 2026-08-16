namespace DotNetQuery.Mvvm.Tests;

/// <summary>
/// A <see cref="SynchronizationContext"/> that queues posted callbacks and executes them
/// only when <see cref="DrainAll"/> is called, making UI-thread marshaling deterministic in tests.
/// </summary>
internal sealed class RecordingSynchronizationContext : SynchronizationContext
{
    private readonly Lock _lock = new();
    private readonly Queue<(SendOrPostCallback Callback, object? State)> _queue = new();

    public int PostCount { get; private set; }

    public override void Post(SendOrPostCallback d, object? state)
    {
        lock (_lock)
        {
            PostCount++;
            _queue.Enqueue((d, state));
        }
    }

    public void DrainAll()
    {
        while (TryDequeue(out var entry))
        {
            entry.Callback(entry.State);
        }
    }

    private bool TryDequeue(out (SendOrPostCallback Callback, object? State) entry)
    {
        lock (_lock)
        {
            return _queue.TryDequeue(out entry);
        }
    }
}
