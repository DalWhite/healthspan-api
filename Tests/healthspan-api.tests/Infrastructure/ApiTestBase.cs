using System.Net.Http.Json;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace healthspan_api.tests;

/// <summary>
/// Base class for API regression tests.
/// Creates a shared factory + seeded DB per test class via IAsyncLifetime.
/// </summary>
public abstract class ApiTestBase : IClassFixture<HealthspanApiFactory>, IAsyncLifetime
{
    protected readonly HealthspanApiFactory Factory;
    protected HttpClient Client = null!;

    protected ApiTestBase(HealthspanApiFactory factory)
    {
        Factory = factory;
    }

    public async Task InitializeAsync()
    {
        Client = Factory.CreateClient();

        using var scope = Factory.Services.CreateScope();
        await TestDataSeeder.SeedAsync(scope.ServiceProvider);
    }

    public Task DisposeAsync() => Task.CompletedTask;
}
