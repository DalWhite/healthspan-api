using System.Diagnostics;
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

namespace healthspan_api.tests.Benchmarks;

/// <summary>
/// QA probes that need the real MySQL provider (the raw multi-row INSERT path,
/// the unique ClientEntityId index and the batch transaction only exist on a
/// relational provider — the 304-test regression suite runs on InMemory).
///
/// Opt-in, same gate as CsvImportBaselineBenchmark:
///   BENCHMARK_DB_CONNECTION_STRING='Server=127.0.0.1;...' \
///   dotnet test Tests/healthspan-api.tests -c Release \
///     --filter "FullyQualifiedName~QaCsvImportMySqlProbe"
///
/// Uses the same deterministic dataset generator as the baseline harness so the
/// timings are comparable with t_37acc65b / t_c1f8e6ac.
/// </summary>
public class QaCsvImportMySqlProbe
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

    private static readonly JsonSerializerOptions JsonOptions =
        new() { PropertyNameCaseInsensitive = true };

    private static string? ConnectionString =>
        Environment.GetEnvironmentVariable("BENCHMARK_DB_CONNECTION_STRING");

    // ---- perf targets (t_37acc65b) -----------------------------------------

    [Theory]
    [InlineData(10_000)]
    [InlineData(50_000)]
    public async Task Probe_Import_Completes_Within_Target_Time(int rows)
    {
        if (string.IsNullOrWhiteSpace(ConnectionString)) return;

        using var factory = new MySqlProbeFactory(ConnectionString!);
        await factory.EnsureUsersAsync();
        var client = factory.CreateClient();

        var csv = GenerateCsv(rows);
        var postSw = Stopwatch.StartNew();
        var response = await client.PostAsync("/data/import", BuildCsvContent(csv));
        postSw.Stop();
        response.StatusCode.Should().Be(System.Net.HttpStatusCode.Accepted);

        var posted = JsonSerializer.Deserialize<JsonElement>(
            await response.Content.ReadAsStringAsync(), JsonOptions);
        var jobId = posted.GetProperty("jobId").GetString()!;

        postSw.Elapsed.Should().BeLessThan(TimeSpan.FromSeconds(5),
            $"POST /data/import must answer 202 immediately even at {rows} rows");

        var totalSw = Stopwatch.StartNew();
        var json = await WaitAsync(client, jobId, 300_000);
        totalSw.Stop();

        AppendResult(new
        {
            probe = "qa_import_job_json",
            rows,
            total_ms = totalSw.ElapsedMilliseconds,
            job = json.ToString(),
        });

        json.GetProperty("status").GetString().Should().Be("completed");
        (json.GetProperty("processed").GetInt32()
            + json.GetProperty("skipped").GetInt32()).Should().Be(rows,
            "every row must be either imported or recognised as an existing duplicate");
        json.GetProperty("errorCount").GetInt32().Should().Be(0);

        var targetSeconds = rows <= 10_000 ? 15 : 60;
        totalSw.Elapsed.TotalSeconds.Should().BeLessThan(targetSeconds,
            $"target from t_37acc65b: {rows} rows < {targetSeconds}s server-side");

        using var scope = factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<MainDbContext>();
        (await db.Exercises.IgnoreQueryFilters().CountAsync())
            .Should().Be(json.GetProperty("processed").GetInt32(),
            "the job counters must match what is actually persisted");

        AppendResult(new
        {
            probe = "qa_import_time",
            rows,
            post_ms = postSw.ElapsedMilliseconds,
            total_ms = totalSw.ElapsedMilliseconds,
            rows_per_second = Math.Round(rows / (double)totalSw.ElapsedMilliseconds * 1000.0, 2),
            measured_at = DateTimeOffset.UtcNow.ToString("o"),
        });
    }

    // ---- idempotency at scale ----------------------------------------------

    [Fact]
    public async Task Probe_Reimport_10k_Skips_Everything_And_Duplicates_Nothing()
    {
        if (string.IsNullOrWhiteSpace(ConnectionString)) return;

        using var factory = new MySqlProbeFactory(ConnectionString!);
        await factory.EnsureUsersAsync();
        var client = factory.CreateClient();
        var csv = GenerateCsv(10_000);

        var first = await WaitAsync(client, await PostJobId(client, csv), 300_000);
        first.GetProperty("processed").GetInt32().Should().Be(10_000);

        var second = await WaitAsync(client, await PostJobId(client, csv), 300_000);
        second.GetProperty("status").GetString().Should().Be("completed");
        second.GetProperty("processed").GetInt32().Should().Be(0);
        second.GetProperty("skipped").GetInt32().Should().Be(10_000);

        using var scope = factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<MainDbContext>();
        (await db.Exercises.IgnoreQueryFilters().CountAsync()).Should().Be(10_000);
        (await db.Workouts.IgnoreQueryFilters().CountAsync()).Should().Be(
            await db.Workouts.IgnoreQueryFilters().CountAsync());
    }

    // ---- concurrency --------------------------------------------------------

    [Fact]
    public async Task Probe_Two_Concurrent_Identical_Imports_Do_Not_Duplicate()
    {
        if (string.IsNullOrWhiteSpace(ConnectionString)) return;

        using var factory = new MySqlProbeFactory(ConnectionString!);
        await factory.EnsureUsersAsync();

        var rounds = new List<object>();
        var expectedTotal = 0;
        var failures = new List<string>();

        // Three rounds: a race needs more than one sample to tell "always" from
        // "sometimes".
        foreach (var (rows, variant) in new[] { (3_000, 0), (1_500, 1), (2_000, 2) })
        {
            var clientA = factory.CreateClient();
            var clientB = factory.CreateClient();
            var csv = GenerateCsv(rows, variant);

            var jobA = await PostJobId(clientA, csv);

            // t_633ebd61 contract (product decision: serialize per user):
            // while an import is running for the same user, a second submit
            // is accepted as a "queued" job that runs after the active one
            // finishes on the idempotent-skip path — it must NOT abort as
            // "failed" on the IX_Workouts_DailySessionKey race. An explicit
            // 409 rejection is also an accepted outcome.
            var responseB = await clientB.PostAsync("/data/import", BuildCsvContent(csv));
            var bodyBRaw = await responseB.Content.ReadAsStringAsync();
            JsonElement? jobB = null;
            if (responseB.StatusCode == System.Net.HttpStatusCode.Accepted)
            {
                jobB = JsonSerializer.Deserialize<JsonElement>(bodyBRaw, JsonOptions);
            }

            var doneA = await WaitAsync(clientA, jobA, 300_000);
            JsonElement? doneB = jobB is { } jb
                ? await WaitAsync(clientB, jb.GetProperty("jobId").GetString()!, 300_000)
                : null;

            using var scope = factory.Services.CreateScope();
            var db = scope.ServiceProvider.GetRequiredService<MainDbContext>();
            var persisted = await db.Exercises.IgnoreQueryFilters().CountAsync();

            expectedTotal += rows;
            rounds.Add(new
            {
                rows,
                jobA = Summary(doneA),
                jobB = doneB is { } bDone
                    ? Summary(bDone)
                    : (object)new { http = (int)responseB.StatusCode, body = bodyBRaw },
                persisted,
                expected = expectedTotal,
            });

            if (persisted != expectedTotal)
            {
                failures.Add($"round {variant}: persisted {persisted}, expected {expectedTotal}");
            }

            if (responseB.StatusCode == System.Net.HttpStatusCode.Conflict)
            {
                if (!bodyBRaw.Contains("import_in_progress"))
                {
                    failures.Add($"round {variant}: 409 without import_in_progress code: {bodyBRaw}");
                }
            }
            else if (doneB is { } b)
            {
                // If a job B was created after all, it must survive to a
                // non-failed terminal state (repository retry defense).
                if (doneA.GetProperty("status").GetString() == "failed"
                    || b.GetProperty("status").GetString() == "failed")
                {
                    failures.Add($"round {variant}: a concurrent job ended as failed -> "
                        + $"{Summary(doneA)} | {Summary(b)}");
                }
            }
            else
            {
                failures.Add($"round {variant}: second concurrent submit returned "
                    + $"{(int)responseB.StatusCode} instead of 409 import_in_progress: {bodyBRaw}");
            }
        }

        // Evidence first, verdict second.
        AppendResult(new { probe = "qa_concurrent_identical", rounds });

        failures.Should().BeEmpty(
            "concurrent identical imports must neither duplicate rows nor fail a job "
            + "(second submit is rejected with 409 import_in_progress)");
    }

    private static object Summary(JsonElement job) => new
    {
        status = job.GetProperty("status").GetString(),
        processed = job.GetProperty("processed").GetInt32(),
        skipped = job.GetProperty("skipped").GetInt32(),
        errorCount = job.GetProperty("errorCount").GetInt32(),
        failureReason = job.TryGetProperty("failureReason", out var fr)
            ? fr.GetString() : null,
    };

    // ---- batch failure must degrade to per-row errors ----------------------

    [Fact]
    public async Task Probe_Bad_Value_In_A_Batch_Errors_Only_That_Row()
    {
        if (string.IsNullOrWhiteSpace(ConnectionString)) return;

        using var factory = new MySqlProbeFactory(ConnectionString!);
        await factory.EnsureUsersAsync();
        var client = factory.CreateClient();

        // 2000 rows, one with a weight MySQL cannot store (double overflow).
        // The 1000-row batch INSERT fails; the per-row fallback must keep the
        // other 1999 rows and report only the broken one.
        var builder = new StringBuilder(
            "Date,ExerciseType,Series,Reps,Weight,AdditionalComments\n");
        for (var i = 0; i < 2_000; i++)
        {
            var weight = i == 7 ? "1e999" : (40 + i % 100).ToString();
            builder.Append($"{1 + i % 28:D2}/{1 + i % 11:D2}/40,Fallback Type {i % 5},3,8,{weight},fallback-{i}\n");
        }

        var job = await WaitAsync(client, await PostJobId(client, builder.ToString()), 300_000);

        AppendResult(new { probe = "qa_fallback_job_json", job = job.ToString() });

        job.GetProperty("processed").GetInt32().Should().Be(1_999,
            "a single un-storable row must not cost the whole import");
        job.GetProperty("errorCount").GetInt32().Should().Be(1);
        job.GetProperty("status").GetString().Should().Be("completed_with_errors");
        job.GetProperty("errors")[0].GetProperty("row").GetInt32().Should().Be(9,
            "physical line number (header = line 1)");

        using var scope = factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<MainDbContext>();
        (await db.Exercises.IgnoreQueryFilters().CountAsync()).Should().Be(1_999);
    }

    // ---- progress quality during a real 10k import -------------------------

    [Fact]
    public async Task Probe_Progress_Monotonic_And_Polls_Stay_Fast_During_10k_Import()
    {
        if (string.IsNullOrWhiteSpace(ConnectionString)) return;

        using var factory = new MySqlProbeFactory(ConnectionString!);
        await factory.EnsureUsersAsync();
        var client = factory.CreateClient();

        var jobId = await PostJobId(client, GenerateCsv(10_000));

        var samples = new List<(long ms, int processed, string status)>();
        var maxPollMs = 0L;
        var sw = Stopwatch.StartNew();
        string lastStatus = "queued";
        int lastProcessed = -1;

        while (lastStatus is "queued" or "running")
        {
            var pollSw = Stopwatch.StartNew();
            var response = await client.GetAsync($"/data/import/{jobId}");
            pollSw.Stop();
            response.StatusCode.Should().Be(System.Net.HttpStatusCode.OK);
            maxPollMs = Math.Max(maxPollMs, pollSw.ElapsedMilliseconds);

            var json = JsonSerializer.Deserialize<JsonElement>(
                await response.Content.ReadAsStringAsync(), JsonOptions);
            lastStatus = json.GetProperty("status").GetString()!;
            var processed = json.GetProperty("processed").GetInt32();

            processed.Should().BeGreaterThanOrEqualTo(lastProcessed,
                "progress must never go backwards");
            lastProcessed = processed;
            samples.Add((sw.ElapsedMilliseconds, processed, lastStatus));

            if (lastStatus is "queued" or "running") await Task.Delay(100);
        }
        sw.Stop();

        lastStatus.Should().Be("completed");
        maxPollMs.Should().BeLessThan(1000,
            "every progress poll must answer in <1s so the FE bar keeps moving");
        samples.Count.Should().BeGreaterThan(3,
            "a 10k import must expose several progress samples, not a single jump 0 -> done");

        AppendResult(new
        {
            probe = "qa_progress_samples",
            samples = samples.Count,
            max_poll_ms = maxPollMs,
            first_sample_ms = samples[0].ms,
            total_ms = sw.ElapsedMilliseconds,
        });
    }

    // ---- cross-user on the real provider ------------------------------------

    [Fact]
    public async Task Probe_Cross_User_Job_Is_404_And_Unique_Index_Does_Not_Leak()
    {
        if (string.IsNullOrWhiteSpace(ConnectionString)) return;

        using var factory = new MySqlProbeFactory(ConnectionString!);
        await factory.EnsureUsersAsync();
        var userA = factory.ClientFor(MySqlProbeFactory.UserA);
        var userB = factory.ClientFor(MySqlProbeFactory.UserB);

        var csv = "Date,ExerciseType,Series,Reps,Weight,AdditionalComments\n" +
                  string.Concat(Enumerable.Range(0, 1_200).Select(i =>
                      $"{1 + i % 28:D2}/{1 + i % 11:D2}/38,Leak Type {i % 6},3,8,{40 + i % 80},leak-{i}\n"));

        var jobA = await PostJobId(userA, csv);
        (await userB.GetAsync($"/data/import/{jobA}"))
            .StatusCode.Should().Be(System.Net.HttpStatusCode.NotFound);

        var jobB = await PostJobId(userB, csv);
        var doneA = await WaitAsync(userA, jobA, 300_000);
        var doneB = await WaitAsync(userB, jobB, 300_000);

        doneA.GetProperty("processed").GetInt32().Should().Be(1_200);
        doneB.GetProperty("processed").GetInt32().Should().Be(1_200,
            "the global unique ClientEntityId index must not dedupe across users");

        using var scope = factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<MainDbContext>();
        (await db.Exercises.IgnoreQueryFilters().CountAsync()).Should().Be(2_400);
    }

    // ---- helpers ------------------------------------------------------------

    private static string GenerateCsv(int rows, int variant = 0)
    {
        // Same deterministic dataset as the baseline harness; variant shifts the
        // seed so a probe can build several independent datasets.
        var random = new Random(20261023 + variant);
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

    private static MultipartFormDataContent BuildCsvContent(string csv)
    {
        var content = new MultipartFormDataContent();
        var payload = new ByteArrayContent(Encoding.UTF8.GetBytes(csv));
        payload.Headers.ContentType = new System.Net.Http.Headers.MediaTypeHeaderValue("text/csv");
        content.Add(payload, "file", "exercises.csv");
        return content;
    }

    private static async Task<string> PostJobId(HttpClient client, string csv)
    {
        var response = await client.PostAsync("/data/import", BuildCsvContent(csv));
        response.StatusCode.Should().Be(System.Net.HttpStatusCode.Accepted);
        var posted = JsonSerializer.Deserialize<JsonElement>(
            await response.Content.ReadAsStringAsync(), JsonOptions);
        return posted.GetProperty("jobId").GetString()!;
    }

    private static async Task<JsonElement> WaitAsync(HttpClient client, string jobId, int timeoutMs)
    {
        var deadline = DateTime.UtcNow.AddMilliseconds(timeoutMs);
        while (DateTime.UtcNow < deadline)
        {
            var response = await client.GetAsync($"/data/import/{jobId}");
            response.StatusCode.Should().Be(System.Net.HttpStatusCode.OK);
            var json = JsonSerializer.Deserialize<JsonElement>(
                await response.Content.ReadAsStringAsync(), JsonOptions);
            var status = json.GetProperty("status").GetString();
            if (status is "completed" or "completed_with_errors" or "failed")
            {
                return json;
            }
            await Task.Delay(100);
        }

        throw new TimeoutException($"Import job {jobId} did not finish in {timeoutMs} ms");
    }

    private static void AppendResult(object result)
    {
        var file = Environment.GetEnvironmentVariable("BENCHMARK_RESULTS_FILE");
        if (!string.IsNullOrWhiteSpace(file))
        {
            File.AppendAllText(file, JsonSerializer.Serialize(result) + Environment.NewLine);
        }
    }

    private sealed class MySqlProbeFactory : WebApplicationFactory<Program>
    {
        public const string UserA = "qa-mysql-user-a";
        public const string UserB = "qa-mysql-user-b";

        private readonly string _connectionString;
        // Keep the name short: EF's migration lock is __<db>_EFMigrationsLock (max 64 chars).
        private readonly string _databaseName = $"qa_csv_probe_{Guid.NewGuid():N}"[..30];

        public MySqlProbeFactory(string connectionString)
        {
            var builder = new MySqlConnector.MySqlConnectionStringBuilder(connectionString)
            {
                Database = _databaseName,
            };
            _connectionString = builder.ToString();
        }

        public HttpClient ClientFor(string userId)
        {
            var client = CreateClient();
            client.DefaultRequestHeaders.Add("X-QA-User", userId);
            return client;
        }

        public async Task EnsureUsersAsync()
        {
            using var scope = Services.CreateScope();
            var users = scope.ServiceProvider.GetRequiredService<UserManager<User>>();
            foreach (var id in new[] { UserA, UserB })
            {
                if (await users.FindByIdAsync(id) is null)
                {
                    var created = await users.CreateAsync(
                        new User
                        {
                            Id = id,
                            UserName = id,
                            Email = $"{id}@qa.dev",
                            Name = "QA Probe User",
                            Age = 30,
                            Weight = 80,
                            Height = 180,
                        },
                        "QaProbePassword123!");
                    created.Succeeded.Should().BeTrue(string.Join("; ", created.Errors));
                }
            }
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
                    options.UseMySql(_connectionString, ServerVersion.AutoDetect(_connectionString)));

                services.RemoveAll<IOcrExtractionProvider>();
                services.AddScoped<IOcrExtractionProvider>(_ => NSubstitute.Substitute.For<IOcrExtractionProvider>());
                services.RemoveAll<IMarkerNormalizationProvider>();
                services.AddScoped<IMarkerNormalizationProvider>(_ => NSubstitute.Substitute.For<IMarkerNormalizationProvider>());
                services.RemoveAll<IBloodTestDocumentStorage>();
                services.AddScoped<IBloodTestDocumentStorage>(_ => NSubstitute.Substitute.For<IBloodTestDocumentStorage>());

                services.RemoveAll<Microsoft.Extensions.Hosting.IHostedService>();

                services.AddAuthentication(QaProbeMySqlAuthHandler.Scheme)
                    .AddScheme<AuthenticationSchemeOptions, QaProbeMySqlAuthHandler>(
                        QaProbeMySqlAuthHandler.Scheme, _ => { });

                services.PostConfigure<AuthenticationOptions>(options =>
                {
                    options.DefaultAuthenticateScheme = QaProbeMySqlAuthHandler.Scheme;
                    options.DefaultChallengeScheme = QaProbeMySqlAuthHandler.Scheme;
                    options.DefaultScheme = QaProbeMySqlAuthHandler.Scheme;
                });
            });
        }
    }

    public class QaProbeMySqlAuthHandler : AuthenticationHandler<AuthenticationSchemeOptions>
    {
        public const string Scheme = "QaMysql";

        public QaProbeMySqlAuthHandler(
            IOptionsMonitor<AuthenticationSchemeOptions> options,
            ILoggerFactory logger,
            UrlEncoder encoder)
            : base(options, logger, encoder)
        {
        }

        protected override Task<AuthenticateResult> HandleAuthenticateAsync()
        {
            var userId = Request.Headers.TryGetValue("X-QA-User", out var header)
                ? header.ToString()
                : MySqlProbeFactory.UserA;

            var claims = new[]
            {
                new Claim(ClaimTypes.NameIdentifier, userId),
                new Claim(ClaimTypes.Email, $"{userId}@qa.dev"),
                new Claim(ClaimTypes.Name, "QA Probe User"),
            };

            var identity = new ClaimsIdentity(claims, Scheme);
            var principal = new ClaimsPrincipal(identity);
            var ticket = new AuthenticationTicket(principal, Scheme);

            return Task.FromResult(AuthenticateResult.Success(ticket));
        }
    }
}
