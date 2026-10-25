#nullable enable
using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using FluentAssertions;
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
using Xunit;

namespace healthspan_api.tests.Regression;

/// <summary>
/// t_e395b7be (QA finding D1 from t_cc04f947): GET /api/activities emitted
/// 85.49999999999999 / 33.300000000000004 / 80.49999999999999 for values
/// stored exactly as 85.5 / 33.3 / 80.5.
///
/// Isolation probe over the real dev MySQL proved:
///  - MySQL stores the value exactly (CAST AS CHAR => "85.5").
///  - Pomelo's DECIMAL(65,30) reader hands EF a double-derived decimal
///    widened to scale 26-28, e.g. stored 85.5 comes back as
///    decimal bits [637534208,-1127200657,463496428, scale 28]
///    = 85.49999999999999999999999999990.
///  - The direct (double?) cast in MapActivity then re-emits that noise, and
///    pure-CLR decimal->double is clean, so the reader is the culprit layer.
///
/// Fix: ToCleanDouble rounds to the domain's meaningful precision (6 dp)
/// before the cast. Read path only; write path and the double? DTO exposure
/// from PR #22 are untouched.
/// The artifacts below are the EXACT values captured via decimal.GetBits()
/// in the dev probe; a direct cast of each reproduces verbatim the JSON the
/// QA probe reported.
/// </summary>
public class DecimalReadNoiseTests
{
    // decimal literals are not valid InlineData arguments; fixtures arrive as
    // strings and keep their exact scale when parsed invariant-culture.
    private static decimal Parse(string literal) =>
        decimal.Parse(literal, System.Globalization.CultureInfo.InvariantCulture);

    [Theory]
    // The exact QA failure matrix (reader artifacts captured via GetBits in the probe).
    [InlineData("85.49999999999999999999999999990", "85.5")]
    [InlineData("33.30000000000000000000000000000", "33.3")]
    [InlineData("80.49999999999999999999999999990", "80.5")]
    // Values that were already clean stay clean through the helper.
    [InlineData("7.500000000000000000000000000000", "7.5")]
    [InlineData("6.000000000000000000000000000000", "6")]
    [InlineData("12.34", "12.34")]
    [InlineData("99.99", "99.99")]
    [InlineData("0.1", "0.1")]
    public void ToCleanDouble_absorbs_pomelo_reader_noise(string literal, string expectedJson)
    {
        var result = ActivitiesController.ToCleanDouble(Parse(literal));

        result.Should().NotBeNull();
        JsonSerializer.Serialize(result).Should().Be(expectedJson);
    }

    [Theory]
    // Guard the fixtures themselves: a direct (double?) cast — what MapActivity
    // used to do — must re-emit the exact noise QA observed, so the tests above
    // are a real regression of the bug and not of a made-up artifact.
    [InlineData("85.49999999999999999999999999990", "85.49999999999999")]
    [InlineData("33.30000000000000000000000000000", "33.300000000000004")]
    [InlineData("80.49999999999999999999999999990", "80.49999999999999")]
    public void Direct_cast_reproduces_the_observed_bug(string literal, string buggyJson)
    {
        JsonSerializer.Serialize((double?)Parse(literal)).Should().Be(buggyJson);
    }

    [Fact]
    public void ToCleanDouble_null_stays_null()
    {
        ActivitiesController.ToCleanDouble(null).Should().BeNull();
    }

    [Fact]
    public async Task Get_activity_emits_clean_decimals_after_pomelo_style_noise()
    {
        using var factory = new HealthspanApiFactory();
        using var client = factory.CreateClient();

        var response = await client.PostAsJsonAsync("/api/activities", new WellnessActivityWriteResource
        {
            Type = "sauna",
            Dimension = WellnessDimension.Health,
            OccurredOnUtc = new DateTime(2026, 10, 24),
            DurationMinutes = 10,
            TemperatureCelsius = 85.5m,
            Intensity = 7.5m,
        });
        response.StatusCode.Should().Be(HttpStatusCode.Created);
        var created = JsonSerializer.Deserialize<JsonElement>(await response.Content.ReadAsStringAsync());
        var id = created.GetProperty("id").GetString();

        // InMemory stores exact decimals, so inject the exact reader artifacts
        // Pomelo hands EF for these stored values (captured over the dev MySQL).
        using (var scope = factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<MainDbContext>();
            var activity = db.WellnessActivities.IgnoreQueryFilters().Single(a => a.Id == Guid.Parse(id!));
            activity.TemperatureCelsius = Parse("85.49999999999999999999999999990");
            activity.Intensity = Parse("7.500000000000000000000000000000");
            db.SaveChanges();
        }

        var body = await (await client.GetAsync($"/api/activities/{id}")).Content.ReadAsStringAsync();

        body.Should().Contain("\"temperatureCelsius\":85.5", body);
        body.Should().NotContain("85.49999999999999", body);
        body.Should().Contain("\"intensity\":7.5", body);
    }
}

/// <summary>
/// Opt-in E2E against a REAL MySQL (same env var as the import real-DB
/// probes) — the layer where the bug actually lives, because the noise is
/// produced by Pomelo's DECIMAL(65,30) reader:
///   BENCHMARK_DB_CONNECTION_STRING='...' \
///   dotnet test Tests/healthspan-api.tests --filter "FullyQualifiedName~DecimalReadNoiseRealDbTests"
/// Runs on a throwaway healthspan_bench_* schema (dropped on dispose).
/// </summary>
public class DecimalReadNoiseRealDbTests
{
    private static string? ConnectionString =>
        Environment.GetEnvironmentVariable("BENCHMARK_DB_CONNECTION_STRING");

    [Theory]
    [InlineData("85.5", "85.5")]
    [InlineData("33.3", "33.3")]
    [InlineData("80.5", "80.5")]
    [InlineData("12.34", "12.34")]
    [InlineData("7.5", "7.5")]
    [InlineData("6", "6")]
    public async Task Get_activities_emits_clean_decimals_from_real_mysql(string written, string expectedJson)
    {
        if (string.IsNullOrWhiteSpace(ConnectionString)) return;

        using var factory = new MySqlBenchFactory(ConnectionString!);
        await EnsureTestUserAsync(factory);

        using (var client = factory.CreateClient())
        {
            var response = await client.PostAsJsonAsync("/api/activities", new WellnessActivityWriteResource
            {
                Type = "probe",
                Dimension = WellnessDimension.Health,
                OccurredOnUtc = new DateTime(2026, 10, 24),
                DurationMinutes = 10,
                TemperatureCelsius = decimal.Parse(written, System.Globalization.CultureInfo.InvariantCulture),
            });
            response.StatusCode.Should().Be(HttpStatusCode.Created);
            var created = JsonSerializer.Deserialize<JsonElement>(await response.Content.ReadAsStringAsync());
            var id = created.GetProperty("id").GetString();

            var body = await (await client.GetAsync($"/api/activities/{id}")).Content.ReadAsStringAsync();

            body.Should().Contain($"\"temperatureCelsius\":{expectedJson}", body);
        }
    }

    private static async Task EnsureTestUserAsync(WebApplicationFactory<Program> factory)
    {
        using var scope = factory.Services.CreateScope();
        var userManager = scope.ServiceProvider.GetRequiredService<UserManager<User>>();
        if (await userManager.FindByIdAsync(HealthspanApiFactory.TestUserId) is not null)
        {
            return;
        }
        var created = await userManager.CreateAsync(
            new User
            {
                Id = HealthspanApiFactory.TestUserId,
                UserName = HealthspanApiFactory.TestUserEmail,
                Email = HealthspanApiFactory.TestUserEmail,
                Name = "Decimal Probe User",
            },
            "ProbePassword123!");
        created.Succeeded.Should().BeTrue(string.Join("; ", created.Errors));
    }

    private sealed class MySqlBenchFactory : WebApplicationFactory<Program>
    {
        private readonly string _connectionString;
        // Keep the name short: EF's migration lock is __<db>_EFMigrationsLock (max 64 chars).
        private readonly string _databaseName = $"healthspan_bench_{Guid.NewGuid():N}"[..30];

        public MySqlBenchFactory(string connectionString)
        {
            var builder = new MySqlConnector.MySqlConnectionStringBuilder(connectionString)
            {
                Database = _databaseName,
            };
            _connectionString = builder.ToString();
        }

        protected override void Dispose(bool disposing)
        {
            if (disposing)
            {
                try
                {
                    var builder = new MySqlConnector.MySqlConnectionStringBuilder(_connectionString)
                    {
                        Database = null,
                    };
                    using var connection = new MySqlConnector.MySqlConnection(builder.ToString());
                    connection.Open();
                    using var command = connection.CreateCommand();
                    command.CommandText = $"DROP DATABASE IF EXISTS `{_databaseName}`";
                    command.ExecuteNonQuery();
                }
                catch
                {
                    // Best-effort cleanup.
                }
            }
            base.Dispose(disposing);
        }

        protected override void ConfigureWebHost(IWebHostBuilder builder)
        {
            builder.UseEnvironment("Testing");
            builder.ConfigureLogging(logging => logging.SetMinimumLevel(LogLevel.Warning));

            builder.ConfigureTestServices(services =>
            {
                services.RemoveAll<IDbContextOptionsConfiguration<MainDbContext>>();
                services.RemoveAll<DbContextOptions<MainDbContext>>();
                services.RemoveAll<DbContextOptions>();
                services.RemoveAll<MainDbContext>();
                services.AddDbContext<MainDbContext>(options =>
                    options.UseMySql(
                        _connectionString,
                        ServerVersion.AutoDetect(_connectionString)));

                services.RemoveAll<IOcrExtractionProvider>();
                services.AddScoped<IOcrExtractionProvider>(_ => NSubstitute.Substitute.For<IOcrExtractionProvider>());
                services.RemoveAll<IMarkerNormalizationProvider>();
                services.AddScoped<IMarkerNormalizationProvider>(_ => NSubstitute.Substitute.For<IMarkerNormalizationProvider>());
                services.RemoveAll<IBloodTestDocumentStorage>();
                services.AddScoped<IBloodTestDocumentStorage>(_ => NSubstitute.Substitute.For<IBloodTestDocumentStorage>());

                services.RemoveAll<Microsoft.Extensions.Hosting.IHostedService>();

                services.AddAuthentication(TestAuthHandler.AuthenticationSchemeName)
                    .AddScheme<AuthenticationSchemeOptions, TestAuthHandler>(
                        TestAuthHandler.AuthenticationSchemeName, _ => { });

                services.PostConfigure<AuthenticationOptions>(options =>
                {
                    options.DefaultAuthenticateScheme = TestAuthHandler.AuthenticationSchemeName;
                    options.DefaultChallengeScheme = TestAuthHandler.AuthenticationSchemeName;
                    options.DefaultScheme = TestAuthHandler.AuthenticationSchemeName;
                });
            });
        }
    }
}
