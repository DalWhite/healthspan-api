using System.Diagnostics;
using FluentAssertions;
using Xunit;
using System.Security.Claims;
using System.Text;
using System.Text.Encodings.Web;
using System.Text.Json;
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

namespace healthspan_api.tests;

/// <summary>
/// Baseline benchmark for POST /data/import (CSV bulk import), measured against a
/// real MySQL database. Runs only when BENCHMARK_DB_CONNECTION_STRING is set;
/// otherwise every case exits early so it never runs in normal CI.
///
/// Results are appended as JSON lines to BENCHMARK_RESULTS_FILE (if set).
///
/// Run:
///   BENCHMARK_DB_CONNECTION_STRING='...' BENCHMARK_RESULTS_FILE=/tmp/bench.jsonl \
///   dotnet test Tests/healthspan-api.tests -c Release \
///     --filter "FullyQualifiedName~CsvImportBaselineBenchmark"
/// </summary>
public class CsvImportBaselineBenchmark
{
    private static readonly string[] ExerciseTypes =
    [
        "Press militar", "dominadas", "sentadilla", "peso muerto", "hip thrust",
        "remo con barra", "jalón al pecho", "press banca", "curl bíceps", "fondos",
        "zancadas", "press inclinado", "elevaciones laterales", "face pull",
        "patada de glúteo", "extensiones cuádriceps", "curl femoral", "gemelos",
        "press francés", "aperturas", "pullover", "sentadilla búlgara",
        "peso muerto rumano", "hip abduction", "crunch", "plancha",
        "elevación de pelvis", "jerk", "clean", "snatch", "kettlebell swing",
        "remos manubrio", "pullover manubrio", "press Arnold", "pájaros",
        "curl martillo", "dominadas agarre neutro"
    ];

    [Theory]
    [InlineData(1000)]
    [InlineData(5000)]
    [InlineData(10000)]
    public async Task Import_Csv_Rows_Baseline(int rows)
    {
        var connectionString = Environment.GetEnvironmentVariable("BENCHMARK_DB_CONNECTION_STRING");
        if (string.IsNullOrWhiteSpace(connectionString))
        {
            return; // benchmark opt-in only; normal CI skips
        }

        using var factory = new MySqlBenchmarkFactory(connectionString);
        using var scope = factory.Services.CreateScope();
        // Schema comes from the app's own startup MigrateAsync (identical to dev).
        var db = scope.ServiceProvider.GetRequiredService<MainDbContext>();

        var userManager = scope.ServiceProvider.GetRequiredService<UserManager<User>>();
        if (await userManager.FindByIdAsync(HealthspanApiFactory.TestUserId) is null)
        {
            var created = await userManager.CreateAsync(
                new User
                {
                    Id = HealthspanApiFactory.TestUserId,
                    UserName = HealthspanApiFactory.TestUserEmail,
                    Email = HealthspanApiFactory.TestUserEmail,
                    Name = "Benchmark User",
                    Age = 30,
                    Weight = 80,
                    Height = 180,
                },
                "BenchmarkPassword123!");
            created.Succeeded.Should().BeTrue(string.Join("; ", created.Errors));
        }

        var csv = GenerateCsv(rows);
        var payload = new ByteArrayContent(Encoding.UTF8.GetBytes(csv));
        payload.Headers.ContentType = new System.Net.Http.Headers.MediaTypeHeaderValue("text/csv");

        using var client = factory.CreateClient();
        client.Timeout = TimeSpan.FromMinutes(120);

        using var form = new MultipartFormDataContent();
        form.Add(payload, "file", "exercises.csv");

        var stopwatch = Stopwatch.StartNew();
        var response = await client.PostAsync("/data/import", form);
        response.StatusCode.Should().Be(System.Net.HttpStatusCode.Accepted);

        // Bulk import is async (t_c1f8e6ac): measure POST → job completion.
        var posted = JsonSerializer.Deserialize<JsonElement>(
            await response.Content.ReadAsStringAsync());
        var jobId = posted.GetProperty("jobId").GetString()!;
        string status;
        JsonElement lastJson = default;
        do
        {
            using var statusResponse = await client.GetAsync($"/data/import/{jobId}");
            statusResponse.EnsureSuccessStatusCode();
            var json = JsonSerializer.Deserialize<JsonElement>(
                await statusResponse.Content.ReadAsStringAsync());
            lastJson = json;
            status = json.GetProperty("status").GetString()!;
            if (status is "running" or "queued")
            {
                await Task.Delay(100);
            }
        }
        while (status is "queued" or "running");
        stopwatch.Stop();
        if (status != "completed")
        {
            var reason = lastJson.TryGetProperty("failureReason", out var fr) ? fr.GetString() : "(none)";
            var errs = lastJson.TryGetProperty("errors", out var er) ? er.GetRawText() : "[]";
            status.Should().Be("completed", $"benchmark CSV has no bad rows; job failed: {reason} | errors: {errs}");
        }

        var importedCount = await db.Exercises
            .IgnoreQueryFilters()
            .CountAsync();

        var result = new
        {
            rows,
            elapsed_ms = stopwatch.ElapsedMilliseconds,
            rows_per_second = Math.Round(rows / (double)stopwatch.ElapsedMilliseconds * 1000.0, 2),
            persisted_exercises = importedCount,
            db = "mysql",
            measured_at = DateTimeOffset.UtcNow.ToString("o"),
        };

        var resultsFile = Environment.GetEnvironmentVariable("BENCHMARK_RESULTS_FILE");
        if (!string.IsNullOrWhiteSpace(resultsFile))
        {
            File.AppendAllText(resultsFile, JsonSerializer.Serialize(result) + Environment.NewLine);
        }
    }

    private static string GenerateCsv(int rows)
    {
        var random = new Random(20261023); // deterministic dataset
        var start = new DateTime(2018, 1, 1);
        var days = (new DateTime(2026, 10, 1) - start).Days;

        var builder = new StringBuilder();
        builder.AppendLine("Date,ExerciseType,Series,Reps,Weight,AdditionalComments");
        for (var i = 0; i < rows; i++)
        {
            var date = start.AddDays(random.Next(days));
            var type = ExerciseTypes[random.Next(ExerciseTypes.Length)];
            var series = random.Next(3, 6);
            var reps = random.Next(5, 13);
            var weight = Math.Round(20 + random.NextDouble() * 120, 1);
            builder.Append(date.ToString("dd/MM/yy")).Append(',')
                .Append(type).Append(',')
                .Append(series).Append(',')
                .Append(reps).Append(',')
                .Append(weight.ToString(System.Globalization.CultureInfo.InvariantCulture))
                .AppendLine(",");
        }
        return builder.ToString();
    }

    private sealed class MySqlBenchmarkFactory : WebApplicationFactory<Program>
    {
        private readonly string _connectionString;
        // Keep the name short: EF's migration lock is __<db>_EFMigrationsLock (max 64 chars).
        private readonly string _databaseName = $"healthspan_bench_{Guid.NewGuid():N}"[..30];

        public MySqlBenchmarkFactory(string connectionString)
        {
            // Point at the benchmark schema on the same MySQL server.
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
                    // Benchmark schemas are throwaway; drop them so repeated
                    // runs don't litter the MySQL server.
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
                    // Best-effort cleanup; never fail a benchmark over it.
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
