namespace DotNetQuery.Core.Tests;

public class QueryClientOptionsTests
{
    [Test]
    public void Validate_DefaultOptions_DoesNotThrow()
    {
        // If this throws, the test fails — no assertion needed for "does not throw" in TUnit.
        new QueryClientOptions().Validate();
    }

    [Test]
    public async Task Validate_NegativeStaleTime_ThrowsArgumentOutOfRangeException()
    {
        var options = new QueryClientOptions { StaleTime = TimeSpan.FromSeconds(-1) };

        var act = () => options.Validate();

        var ex = await Assert.That(act).ThrowsException().And.IsTypeOf<ArgumentOutOfRangeException>();
        await Assert.That(ex?.ParamName!).IsEqualTo(nameof(QueryClientOptions.StaleTime));
    }

    [Test]
    public async Task Validate_NegativeCacheTime_ThrowsArgumentOutOfRangeException()
    {
        var options = new QueryClientOptions { CacheTime = TimeSpan.FromSeconds(-1) };

        var act = () => options.Validate();

        var ex = await Assert.That(act).ThrowsException().And.IsTypeOf<ArgumentOutOfRangeException>();
        await Assert.That(ex?.ParamName!).IsEqualTo(nameof(QueryClientOptions.CacheTime));
    }

    [Test]
    public async Task Validate_ZeroRefetchInterval_ThrowsArgumentOutOfRangeException()
    {
        var options = new QueryClientOptions { RefetchInterval = TimeSpan.Zero };

        var act = () => options.Validate();

        var ex = await Assert.That(act).ThrowsException().And.IsTypeOf<ArgumentOutOfRangeException>();
        await Assert.That(ex?.ParamName!).IsEqualTo(nameof(QueryClientOptions.RefetchInterval));
    }

    [Test]
    public async Task Validate_NegativeRefetchInterval_ThrowsArgumentOutOfRangeException()
    {
        var options = new QueryClientOptions { RefetchInterval = TimeSpan.FromSeconds(-1) };

        var act = () => options.Validate();

        var ex = await Assert.That(act).ThrowsException().And.IsTypeOf<ArgumentOutOfRangeException>();
        await Assert.That(ex?.ParamName!).IsEqualTo(nameof(QueryClientOptions.RefetchInterval));
    }

    [Test]
    public void Validate_NullRefetchInterval_DoesNotThrow()
    {
        new QueryClientOptions { RefetchInterval = null }.Validate();
    }

    [Test]
    public async Task Validate_NullRetryHandler_ThrowsArgumentNullException()
    {
        var options = new QueryClientOptions { RetryHandler = null! };

        var act = () => options.Validate();

        var ex = await Assert.That(act).ThrowsException().And.IsTypeOf<ArgumentNullException>();
        await Assert.That(ex?.ParamName).IsEqualTo(nameof(QueryClientOptions.RetryHandler));
    }

    [Test]
    public void Validate_ZeroStaleTime_DoesNotThrow()
    {
        new QueryClientOptions { StaleTime = TimeSpan.Zero }.Validate();
    }

    [Test]
    public void Validate_ZeroCacheTime_DoesNotThrow()
    {
        // Zero cache time is valid: evicts the entry immediately when all subscribers leave.
        new QueryClientOptions { CacheTime = TimeSpan.Zero }.Validate();
    }

    [Test]
    public async Task QueryClientFactory_InvalidOptions_ThrowsBeforeCreatingClient()
    {
        var options = new QueryClientOptions { StaleTime = TimeSpan.FromSeconds(-1) };

        var act = () => QueryClientFactory.Create(options);

        await Assert.That(act).ThrowsException().And.IsTypeOf<ArgumentOutOfRangeException>();
    }
}
