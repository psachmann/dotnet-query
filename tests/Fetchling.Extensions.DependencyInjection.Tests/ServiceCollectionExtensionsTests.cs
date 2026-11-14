namespace Fetchling.Extensions.DependencyInjection.Tests;

public class ServiceCollectionExtensionsTests
{
    [Test]
    public async Task AddFetchling_RegistersIQueryClient()
    {
        var services = new ServiceCollection();

        services.AddFetchling();

        var descriptor = services.FirstOrDefault(d => d.ServiceType == typeof(IQueryClient));
        await Assert.That(descriptor).IsNotNull();
    }

    [Test]
    public async Task AddFetchling_DefaultExecutionMode_IsCsr_RegistersAsSingleton()
    {
        var services = new ServiceCollection();

        services.AddFetchling();

        var descriptor = services.First(d => d.ServiceType == typeof(IQueryClient));
        await Assert.That(descriptor.Lifetime).IsEqualTo(ServiceLifetime.Singleton);
    }

    [Test]
    public async Task AddFetchling_SsrExecutionMode_RegistersAsScoped()
    {
        var services = new ServiceCollection();

        services.AddFetchling(o => o.ExecutionMode = QueryExecutionMode.Ssr);

        var descriptor = services.First(d => d.ServiceType == typeof(IQueryClient));
        await Assert.That(descriptor.Lifetime).IsEqualTo(ServiceLifetime.Scoped);
    }

    [Test]
    public async Task AddFetchling_CanResolveIQueryClient()
    {
        var services = new ServiceCollection();
        services.AddFetchling();

        var provider = services.BuildServiceProvider();
        var client = provider.GetService<IQueryClient>();

        await Assert.That(client).IsNotNull();
    }

    [Test]
    public async Task AddFetchling_Csr_ReturnsSameInstance()
    {
        var services = new ServiceCollection();
        services.AddFetchling();

        var provider = services.BuildServiceProvider();
        var first = provider.GetRequiredService<IQueryClient>();
        var second = provider.GetRequiredService<IQueryClient>();

        await Assert.That(first).IsSameReferenceAs(second);
    }

    [Test]
    public async Task AddFetchling_Ssr_ReturnsDifferentInstancesPerScope()
    {
        var services = new ServiceCollection();
        services.AddFetchling(o => o.ExecutionMode = QueryExecutionMode.Ssr);

        var provider = services.BuildServiceProvider();
        var first = provider.CreateScope().ServiceProvider.GetRequiredService<IQueryClient>();
        var second = provider.CreateScope().ServiceProvider.GetRequiredService<IQueryClient>();

        await Assert.That(first).IsNotSameReferenceAs(second);
    }

    [Test]
    public async Task AddFetchling_InvalidExecutionMode_ThrowsArgumentOutOfRangeException()
    {
        var services = new ServiceCollection();

        await Assert
            .That(() => services.AddFetchling(o => o.ExecutionMode = (QueryExecutionMode)99))
            .Throws<ArgumentOutOfRangeException>();
    }

    [Test]
    public async Task AddFetchling_WithoutConfigure_DoesNotThrow()
    {
        var services = new ServiceCollection();

        await Assert.That(() => services.AddFetchling()).ThrowsNothing();
    }

    [Test]
    public async Task AddFetchling_InvokesConfigureAction()
    {
        var services = new ServiceCollection();
        var invoked = false;

        services.AddFetchling(_ => invoked = true);

        await Assert.That(invoked).IsTrue();
    }

    [Test]
    public async Task AddFetchling_ReturnsOriginalServiceCollection()
    {
        var services = new ServiceCollection();

        var result = services.AddFetchling();

        await Assert.That(result).IsSameReferenceAs(services);
    }

    [Test]
    public async Task AddFetchling_WithoutLoggerFactory_CanResolveIQueryClient()
    {
        var services = new ServiceCollection();
        services.AddFetchling();

        var provider = services.BuildServiceProvider();
        var client = provider.GetService<IQueryClient>();

        await Assert.That(client).IsNotNull();
    }

    [Test]
    public async Task AddFetchling_WithLoggerFactory_CanResolveIQueryClient()
    {
        var services = new ServiceCollection();
        services.AddSingleton<ILoggerFactory>(NullLoggerFactory.Instance);
        services.AddFetchling();

        var provider = services.BuildServiceProvider();
        var client = provider.GetService<IQueryClient>();

        await Assert.That(client).IsNotNull();
    }
}
