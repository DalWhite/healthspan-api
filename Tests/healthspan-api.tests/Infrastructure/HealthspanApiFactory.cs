using System.Security.Claims;
using System.Text.Encodings.Web;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using NSubstitute;
using Services;

namespace healthspan_api.tests;

public class HealthspanApiFactory : WebApplicationFactory<Program>
{
    public const string TestUserId = "test-user-001";
    public const string TestUserEmail = "test@healthspan.dev";

    private readonly string _databaseName = $"HealthspanTestDb_{Guid.NewGuid():N}";

    /// <summary>Set to share one in-memory store across factory instances
    /// (simulates an API restart, t_e26d8b7d): identical EF options map to the
    /// same internal service provider and database root. Must be set via
    /// object initializer before the host is created; xunit fixtures may only
    /// have one public constructor.</summary>
    public string? SharedDatabaseName { get; init; }

    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        // A fixed environment makes local and CI behavior identical. Keeping error logging
        // enabled also preserves the server exception in CI if a request fails.
        builder.UseEnvironment("Testing");
        builder.ConfigureLogging(logging => logging.SetMinimumLevel(LogLevel.Warning));

        builder.ConfigureTestServices(services =>
        {
            // Replace only MainDbContext's MySQL options. Removing every EF service
            // is brittle because framework patch versions can register them differently.
            services.RemoveAll<IDbContextOptionsConfiguration<MainDbContext>>();
            services.RemoveAll<DbContextOptions<MainDbContext>>();
            services.RemoveAll<DbContextOptions>();
            services.RemoveAll<MainDbContext>();
            services.AddDbContext<MainDbContext>(options =>
                options.UseInMemoryDatabase(SharedDatabaseName ?? _databaseName));

            // Replace external service dependencies with no-ops/fakes.
            services.RemoveAll<IOcrExtractionProvider>();
            services.AddScoped<IOcrExtractionProvider>(_ => Substitute.For<IOcrExtractionProvider>());

            services.RemoveAll<IMarkerNormalizationProvider>();
            services.AddScoped<IMarkerNormalizationProvider>(_ => Substitute.For<IMarkerNormalizationProvider>());

            services.RemoveAll<IBloodTestDocumentStorage>();
            services.AddScoped<IBloodTestDocumentStorage>(_ => Substitute.For<IBloodTestDocumentStorage>());

            // Background workers must not outlive or mutate an integration test database.
            services.RemoveAll<Microsoft.Extensions.Hosting.IHostedService>();

            services.AddAuthentication(TestAuthHandler.AuthenticationSchemeName)
                .AddScheme<AuthenticationSchemeOptions, TestAuthHandler>(
                    TestAuthHandler.AuthenticationSchemeName,
                    _ => { });

            services.PostConfigure<AuthenticationOptions>(options =>
            {
                options.DefaultAuthenticateScheme = TestAuthHandler.AuthenticationSchemeName;
                options.DefaultChallengeScheme = TestAuthHandler.AuthenticationSchemeName;
                options.DefaultScheme = TestAuthHandler.AuthenticationSchemeName;
            });
        });
    }
}

public class TestAuthHandler : AuthenticationHandler<AuthenticationSchemeOptions>
{
    public const string AuthenticationSchemeName = "Test";

    public TestAuthHandler(
        IOptionsMonitor<AuthenticationSchemeOptions> options,
        ILoggerFactory logger,
        UrlEncoder encoder)
        : base(options, logger, encoder)
    {
    }

    protected override Task<AuthenticateResult> HandleAuthenticateAsync()
    {
        var claims = new[]
        {
            new Claim(ClaimTypes.NameIdentifier, HealthspanApiFactory.TestUserId),
            new Claim(ClaimTypes.Email, HealthspanApiFactory.TestUserEmail),
            new Claim(ClaimTypes.Name, "Test User"),
        };

        var identity = new ClaimsIdentity(claims, AuthenticationSchemeName);
        var principal = new ClaimsPrincipal(identity);
        var ticket = new AuthenticationTicket(principal, AuthenticationSchemeName);

        return Task.FromResult(AuthenticateResult.Success(ticket));
    }
}
