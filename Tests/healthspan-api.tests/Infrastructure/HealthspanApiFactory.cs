using System.Security.Claims;
using System.Text.Encodings.Web;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.EntityFrameworkCore;
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

    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        builder.ConfigureLogging(logging => logging.ClearProviders());

        builder.ConfigureTestServices(services =>
        {
            services.AddLogging(logging => logging.ClearProviders());

            // Replace MySQL with InMemory — must remove ALL EF descriptors to avoid dual-provider conflict
            var descriptorsToRemove = services
                .Where(d => d.ServiceType.FullName?.Contains("EntityFrameworkCore") == true
                         || d.ServiceType == typeof(DbContextOptions<MainDbContext>)
                         || d.ServiceType == typeof(DbContextOptions)
                         || d.ServiceType == typeof(MainDbContext))
                .ToList();
            foreach (var d in descriptorsToRemove)
                services.Remove(d);

            var testDbName = $"HealthspanTestDb_{Guid.NewGuid():N}";
            services.AddDbContext<MainDbContext>((sp, options) =>
            {
                options.UseInMemoryDatabase(testDbName);
            });

            // CurrentUserProvider reads claims from HttpContext. During HTTP requests, TestAuthHandler
            // provides the claims. During seeding (no request), we need a fallback HttpContext.
            services.RemoveAll<IHttpContextAccessor>();
            services.AddSingleton<IHttpContextAccessor>(_ =>
            {
                var claims = new[]
                {
                    new Claim(ClaimTypes.NameIdentifier, TestUserId),
                    new Claim(ClaimTypes.Email, TestUserEmail),
                    new Claim(ClaimTypes.Name, "Test User"),
                };
                var identity = new ClaimsIdentity(claims, "Test");
                var principal = new ClaimsPrincipal(identity);
                var context = new DefaultHttpContext { User = principal };
                return new TestHttpContextAccessor(context);
            });

            // Replace external service dependencies with no-ops/fakes
            services.RemoveAll<IOcrExtractionProvider>();
            services.AddScoped<IOcrExtractionProvider>(_ => Substitute.For<IOcrExtractionProvider>());

            services.RemoveAll<IMarkerNormalizationProvider>();
            services.AddScoped<IMarkerNormalizationProvider>(_ => Substitute.For<IMarkerNormalizationProvider>());

            services.RemoveAll<IBloodTestDocumentStorage>();
            services.AddScoped<IBloodTestDocumentStorage>(_ => Substitute.For<IBloodTestDocumentStorage>());

            // Remove hosted services (background workers)
            services.RemoveAll<Microsoft.Extensions.Hosting.IHostedService>();

            // Add test authentication scheme
            services.AddAuthentication("Test")
                .AddScheme<AuthenticationSchemeOptions, TestAuthHandler>("Test", _ => { });

            // Override default authentication scheme
            services.PostConfigure<AuthenticationOptions>(options =>
            {
                options.DefaultAuthenticateScheme = "Test";
                options.DefaultChallengeScheme = "Test";
                options.DefaultScheme = "Test";
            });
        });
    }
}

public class TestAuthHandler : AuthenticationHandler<AuthenticationSchemeOptions>
{
    public TestAuthHandler(
        IOptionsMonitor<AuthenticationSchemeOptions> options,
        ILoggerFactory logger,
        UrlEncoder encoder)
        : base(options, logger, encoder) { }

    protected override Task<AuthenticateResult> HandleAuthenticateAsync()
    {
        var claims = new[]
        {
            new Claim(ClaimTypes.NameIdentifier, HealthspanApiFactory.TestUserId),
            new Claim(ClaimTypes.Email, HealthspanApiFactory.TestUserEmail),
            new Claim(ClaimTypes.Name, "Test User"),
        };

        var identity = new ClaimsIdentity(claims, "Test");
        var principal = new ClaimsPrincipal(identity);
        var ticket = new AuthenticationTicket(principal, "Test");

        return Task.FromResult(AuthenticateResult.Success(ticket));
    }
}

/// <summary>
/// An IHttpContextAccessor that returns the real request HttpContext when available,
/// falling back to a default context with test claims (for seeding outside HTTP requests).
/// </summary>
public class TestHttpContextAccessor : IHttpContextAccessor
{
    private readonly HttpContext _fallback;
    private readonly HttpContextAccessor _inner = new();

    public TestHttpContextAccessor(HttpContext fallback) => _fallback = fallback;

    public HttpContext? HttpContext
    {
        get => _inner.HttpContext ?? _fallback;
        set => _inner.HttpContext = value;
    }
}
