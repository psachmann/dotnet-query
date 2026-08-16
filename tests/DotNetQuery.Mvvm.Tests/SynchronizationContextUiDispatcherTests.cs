namespace DotNetQuery.Mvvm.Tests;

public class SynchronizationContextUiDispatcherTests
{
    [Test]
    public async Task Post_WithNullContext_InvokesInline()
    {
        var sut = new SynchronizationContextUiDispatcher(context: null);
        var invoked = false;

        sut.Post(() => invoked = true);

        await Assert.That(invoked).IsTrue();
    }

    [Test]
    public async Task Post_WithContext_PostsToThatContext()
    {
        var context = new RecordingSynchronizationContext();
        var sut = new SynchronizationContextUiDispatcher(context);
        var invoked = false;

        sut.Post(() => invoked = true);

        await Assert.That(invoked).IsFalse();

        context.DrainAll();

        await Assert.That(invoked).IsTrue();
    }

    [Test]
    public async Task CaptureCurrent_CapturesAmbientContext()
    {
        var context = new RecordingSynchronizationContext();
        var previous = SynchronizationContext.Current;
        SynchronizationContext.SetSynchronizationContext(context);

        try
        {
            var sut = SynchronizationContextUiDispatcher.CaptureCurrent();
            var invoked = false;

            sut.Post(() => invoked = true);

            await Assert.That(invoked).IsFalse();

            context.DrainAll();

            await Assert.That(invoked).IsTrue();
        }
        finally
        {
            SynchronizationContext.SetSynchronizationContext(previous);
        }
    }
}
