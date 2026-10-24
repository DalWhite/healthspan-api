using System.Security.Claims;
using System.Text;
using System.Text.Encodings.Web;
using System.Text.Json;
using FluentAssertions;
using healthspan_api.Persistence;
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
/// t_a4651a49 E2E probe on a REAL MySQL schema: rows that fail at the database
/// level must surface the mapped, actionable reason in the job's errors — never
/// the wrapper text "Import batch insert failed" nor the raw MySQL message.
///
/// Opt-in like the import benchmark (never runs in normal CI):
///   BENCHMARK_DB_CONNECTION_STRING='...' \
///   dotnet test Tests/healthspan-api.tests --filter "FullyQualifiedName~CsvImportRealDbErrorTests"
/// Runs on a throwaway healthspan_bench_* schema (same grants as the benchmark).
/// </summary>
public class CsvImportRealDbErrorTests
{
    private static string? ConnectionString =>
        Environment.GetEnvironmentVariable("BENCHMARK_DB_CONNECTION_STRING");

    [Fact]
    public async Task Non_finite_weight_row_surfaces_invalid_number_reason()
    {
        if (string.IsNullOrWhiteSpace(ConnectionString)) return;

        using var factory = new MySqlBenchFactory(ConnectionString!);
        await EnsureTestUserAsync(factory);

        // Row 4: weight 1e999 parses to Infinity in C#; MySqlConnector renders
        // it as the literal Infinity token and MySQL 8.4 rejects the insert
        // (1054 Unknown column 'Infinity') — the real QA t_79d39b1e case,
        // verified against the real server.
        var csv =
            "Date,ExerciseType,Series,Reps,Weight,AdditionalComments\n" +
            "01/05/26,Press militar,4,10,80.5,\n" +
            "02/05/26,Press militar,4,10,81,\n" +
            "03/05/26,Press militar,4,10,82,\n" +
            "04/05/26,Press militar,4,10,1e999,\n";

        var status = await RunImportAsync(factory, csv);

        status.GetProperty("status").GetString().Should().Be("completed_with_errors");
        status.GetProperty("processed").GetInt32().Should().Be(3);
        status.GetProperty("errorCount").GetInt32().Should().Be(1);

        var error = status.GetProperty("errors")[0];
        error.GetProperty("row").GetInt32().Should().Be(5);
        var message = error.GetProperty("message").GetString()!;
        message.Should().Contain("Infinity", "the offending value must be named");
        message.Should().Contain("no es un número válido");
        // The old generic wrapper and the raw MySQL text must not leak.
        message.Should().NotContain("Import batch insert failed");
        message.Should().NotContain("Unknown column");
    }

    [Fact]
    public async Task Same_csv_reimported_is_skipped_not_duplicated()
    {
        if (string.IsNullOrWhiteSpace(ConnectionString)) return;

        using var factory = new MySqlBenchFactory(ConnectionString!);
        await EnsureTestUserAsync(factory);

        var csv =
            "Date,ExerciseType,Series,Reps,Weight,AdditionalComments\n" +
            "01/05/26,Sentadilla,5,8,100,\n" +
            "02/05/26,Sentadilla,5,8,101,\n";

        var first = await RunImportAsync(factory, csv);
        first.GetProperty("status").GetString().Should().Be("completed");
        first.GetProperty("processed").GetInt32().Should().Be(2);

        // Idempotency contract (t_37acc65b §6.3): the deterministic
        // ClientEntityId dedup catches the collision BEFORE the INSERT, so a
        // reimport reports skipped rows, not 1062 errors, and stores no
        // duplicates. (The 1062 mapping itself is covered by
        // CsvImportErrorMapperTests.)
        var second = await RunImportAsync(factory, csv);
        second.GetProperty("status").GetString().Should().Be("completed");
        second.GetProperty("skipped").GetInt32().Should().Be(2);
        second.GetProperty("processed").GetInt32().Should().Be(0);
        second.GetProperty("errorCount").GetInt32().Should().Be(0);
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
                Name = "Error Probe User",
                Age = 30,
                Weight = 80,
                Height = 180,
            },
            "ProbePassword123!");
        created.Succeeded.Should().BeTrue(string.Join("; ", created.Errors));
    }

    private static async Task<JsonElement> RunImportAsync(
        WebApplicationFactory<Program> factory, string csv)
    {
        using var client = factory.CreateClient();
        using var form = new MultipartFormDataContent();
        var payload = new ByteArrayContent(Encoding.UTF8.GetBytes(csv));
        payload.Headers.ContentType =
            new System.Net.Http.Headers.MediaTypeHeaderValue("text/csv");
        form.Add(payload, "file", "errors.csv");

        var response = await client.PostAsync("/data/import", form);
        response.StatusCode.Should().Be(System.Net.HttpStatusCode.Accepted);

        var posted = JsonSerializer.Deserialize<JsonElement>(
            await response.Content.ReadAsStringAsync());
        var jobId = posted.GetProperty("jobId").GetString()!;

        string status;
        JsonElement lastJson = default;
        do
        {
            using var statusResponse = await client.GetAsync($"/data/import/{jobId}");
            statusResponse.EnsureSuccessStatusCode();
            lastJson = JsonSerializer.Deserialize<JsonElement>(
                await statusResponse.Content.ReadAsStringAsync());
            status = lastJson.GetProperty("status").GetString()!;
            if (status is "running" or "queued")
            {
                await Task.Delay(100);
            }
        }
        while (status is "queued" or "running");

        return lastJson;
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
