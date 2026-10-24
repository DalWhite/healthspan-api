using System.Diagnostics;
using System.Net;
using System.Net.Http.Headers;
using System.Security.Claims;
using System.Text;
using System.Text.Encodings.Web;
using System.Text.Json;
using FluentAssertions;
using healthspan_api.Persistence;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using NSubstitute;
using Xunit;

namespace healthspan_api.tests.Regression;

/// <summary>
/// QA load/edge probes for the bulk CSV import (t_79d39b1e).
/// Same wiring as HealthspanApiFactory, but the test auth handler honours an
/// X-QA-User header so a probe can act as two different users.
/// </summary>
public class QaUserSwitchFactory : WebApplicationFactory<Program>
{
    public const string UserA = "qa-csv-user-a";
    public const string UserB = "qa-csv-user-b";

    private readonly string _databaseName = $"QaCsvProbe_{Guid.NewGuid():N}";

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
                options.UseInMemoryDatabase(_databaseName));

            services.RemoveAll<IOcrExtractionProvider>();
            services.AddScoped<IOcrExtractionProvider>(_ => Substitute.For<IOcrExtractionProvider>());
            services.RemoveAll<IMarkerNormalizationProvider>();
            services.AddScoped<IMarkerNormalizationProvider>(_ => Substitute.For<IMarkerNormalizationProvider>());
            services.RemoveAll<IBloodTestDocumentStorage>();
            services.AddScoped<IBloodTestDocumentStorage>(_ => Substitute.For<IBloodTestDocumentStorage>());

            services.RemoveAll<Microsoft.Extensions.Hosting.IHostedService>();

            services.AddAuthentication(QaUserAuthHandler.Scheme)
                .AddScheme<AuthenticationSchemeOptions, QaUserAuthHandler>(
                    QaUserAuthHandler.Scheme, _ => { });

            services.PostConfigure<AuthenticationOptions>(options =>
            {
                options.DefaultAuthenticateScheme = QaUserAuthHandler.Scheme;
                options.DefaultChallengeScheme = QaUserAuthHandler.Scheme;
                options.DefaultScheme = QaUserAuthHandler.Scheme;
            });
        });
    }

    public HttpClient ClientFor(string userId)
    {
        var client = CreateClient();
        client.DefaultRequestHeaders.Add("X-QA-User", userId);
        return client;
    }
}

public class QaUserAuthHandler : AuthenticationHandler<AuthenticationSchemeOptions>
{
    public const string Scheme = "QaUser";

    public QaUserAuthHandler(
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
            : QaUserSwitchFactory.UserA;

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

public class QaCsvImportProbeTests : IClassFixture<QaUserSwitchFactory>
{
    private const string Header =
        "Date,ExerciseType,Series,Reps,Weight,AdditionalComments\n";

    private static readonly JsonSerializerOptions JsonOptions =
        new() { PropertyNameCaseInsensitive = true };

    private readonly QaUserSwitchFactory _factory;

    public QaCsvImportProbeTests(QaUserSwitchFactory factory) => _factory = factory;

    // ---- limits -------------------------------------------------------------

    [Fact]
    public async Task Probe_50001_rows_is_rejected_upfront_with_structural_error()
    {
        var client = _factory.ClientFor(QaUserSwitchFactory.UserA);
        var builder = new StringBuilder(Header);
        for (var i = 0; i < 50_001; i++)
        {
            builder.Append($"01/0{1 + i % 9}/39,Cap Type {i % 3},3,8,60,cap-{i}\n");
        }

        var sw = Stopwatch.StartNew();
        var response = await client.PostAsync("/data/import", BuildCsvContent(builder.ToString()));
        sw.Stop();

        response.StatusCode.Should().Be(HttpStatusCode.BadRequest,
            "row cap must be answered upfront, not as a background job");
        sw.Elapsed.Should().BeLessThan(TimeSpan.FromSeconds(10),
            "preflight must not depend on import duration");

        var body = await response.Content.ReadAsStringAsync();
        body.Should().Contain("máximo 50000");
    }

    [Fact]
    public async Task Probe_50000_invalid_rows_is_also_rejected_by_the_row_cap()
    {
        var client = _factory.ClientFor(QaUserSwitchFactory.UserA);
        var builder = new StringBuilder(Header);
        for (var i = 0; i < 50_001; i++)
        {
            builder.Append("not-a-date,Bad Row,3,8,60,invalid\n");
        }

        var response = await client.PostAsync("/data/import", BuildCsvContent(builder.ToString()));

        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
        (await response.Content.ReadAsStringAsync()).Should().Contain("máximo 50000");
    }

    [Fact]
    public async Task Probe_oversized_file_is_rejected_before_any_parsing()
    {
        var client = _factory.ClientFor(QaUserSwitchFactory.UserA);
        var content = new MultipartFormDataContent();
        var payload = new ByteArrayContent(new byte[CsvService.MaxImportBytes + 1]);
        payload.Headers.ContentType = new MediaTypeHeaderValue("text/csv");
        content.Add(payload, "file", "big.csv");

        var response = await client.PostAsync("/data/import", content);

        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
        (await response.Content.ReadAsStringAsync()).Should().Contain("5 MB");
    }

    // ---- progress contract --------------------------------------------------

    [Fact]
    public async Task Probe_progress_is_visible_within_1s_of_the_post()
    {
        var client = _factory.ClientFor(QaUserSwitchFactory.UserA);
        var csv = BuildCsv(3_000, "Progress Type");

        var postSw = Stopwatch.StartNew();
        var response = await client.PostAsync("/data/import", BuildCsvContent(csv));
        postSw.Stop();
        response.StatusCode.Should().Be(HttpStatusCode.Accepted);

        var posted = JsonSerializer.Deserialize<JsonElement>(
            await response.Content.ReadAsStringAsync(), JsonOptions);
        var jobId = posted.GetProperty("jobId").GetString()!;
        posted.GetProperty("total").GetInt32().Should().Be(3_000);

        // Target from t_37acc65b: progress visible < 1 s after the POST returns.
        var getSw = Stopwatch.StartNew();
        var statusResponse = await client.GetAsync($"/data/import/{jobId}");
        getSw.Stop();

        statusResponse.StatusCode.Should().Be(HttpStatusCode.OK);
        var json = JsonSerializer.Deserialize<JsonElement>(
            await statusResponse.Content.ReadAsStringAsync(), JsonOptions);
        json.GetProperty("status").GetString().Should().BeOneOf("queued", "running", "completed");
        json.GetProperty("total").GetInt32().Should().Be(3_000);
        getSw.Elapsed.Should().BeLessThan(TimeSpan.FromSeconds(1),
            "GET /data/import/{jobId} must answer in well under a second");
        postSw.Elapsed.Should().BeLessThan(TimeSpan.FromSeconds(10),
            "POST must return 202 without waiting for the import");

        // The job must not be aborted by the client going away: keep polling.
        var final = await WaitAsync(client, jobId, 120_000);
        final.GetProperty("status").GetString().Should().Be("completed");
        final.GetProperty("processed").GetInt32().Should().Be(3_000);
    }

    [Fact]
    public async Task Probe_header_only_csv_returns_202_with_total_zero()
    {
        var client = _factory.ClientFor(QaUserSwitchFactory.UserA);

        var response = await client.PostAsync("/data/import", BuildCsvContent(Header));
        response.StatusCode.Should().Be(HttpStatusCode.Accepted);

        var posted = JsonSerializer.Deserialize<JsonElement>(
            await response.Content.ReadAsStringAsync(), JsonOptions);
        var job = await WaitAsync(client, posted.GetProperty("jobId").GetString()!, 30_000);

        job.GetProperty("total").GetInt32().Should().Be(0);
        job.GetProperty("processed").GetInt32().Should().Be(0);
        job.GetProperty("status").GetString().Should().Be("completed");
    }

    [Fact]
    public async Task Probe_all_rows_invalid_reports_errors_and_never_aborts()
    {
        var client = _factory.ClientFor(QaUserSwitchFactory.UserA);
        var builder = new StringBuilder(Header);
        for (var i = 0; i < 200; i++)
        {
            builder.Append("31/99/25,Bad Type,3,abc,60,bad\n");
        }

        var posted = JsonSerializer.Deserialize<JsonElement>(
            await response_of(client, builder.ToString()), JsonOptions);
        var job = await WaitAsync(client, posted.GetProperty("jobId").GetString()!, 60_000);

        job.GetProperty("status").GetString().Should().Be("completed_with_errors");
        job.GetProperty("processed").GetInt32().Should().Be(0);
        job.GetProperty("errorCount").GetInt32().Should().Be(200);
    }

    [Fact]
    public async Task Probe_exposed_errors_are_capped_at_100_but_errorCount_is_exact()
    {
        var client = _factory.ClientFor(QaUserSwitchFactory.UserA);
        var builder = new StringBuilder(Header);
        for (var i = 0; i < 300; i++)
        {
            builder.Append("31/99/25,Bad Type,3,abc,60,bad\n");
        }
        builder.Append("05/05/39,Good Type,3,8,60,good row\n");

        var posted = JsonSerializer.Deserialize<JsonElement>(
            await response_of(client, builder.ToString()), JsonOptions);
        var job = await WaitAsync(client, posted.GetProperty("jobId").GetString()!, 60_000);

        job.GetProperty("errorCount").GetInt32().Should().Be(300);
        job.GetProperty("errors").GetArrayLength().Should().Be(100,
            "MaxErrorsExposed caps the payload; errorCount keeps the real total");
        job.GetProperty("processed").GetInt32().Should().Be(1);
    }

    // ---- idempotency --------------------------------------------------------

    [Fact]
    public async Task Probe_identical_rows_across_batches_are_skipped()
    {
        var client = _factory.ClientFor(QaUserSwitchFactory.UserA);
        var builder = new StringBuilder(Header);
        for (var i = 0; i < 2_000; i++)
        {
            builder.Append("17/02/41,Same Type,4,10,80,exact duplicate\n");
        }

        var posted = JsonSerializer.Deserialize<JsonElement>(
            await response_of(client, builder.ToString()), JsonOptions);
        var job = await WaitAsync(client, posted.GetProperty("jobId").GetString()!, 120_000);

        job.GetProperty("total").GetInt32().Should().Be(2_000);
        job.GetProperty("processed").GetInt32().Should().Be(1,
            "2000 byte-identical rows must collapse to one exercise");
        job.GetProperty("skipped").GetInt32().Should().Be(1_999);
    }

    [Fact]
    public async Task Probe_reimport_of_2000_rows_skips_everything_and_duplicates_nothing()
    {
        var client = _factory.ClientFor(QaUserSwitchFactory.UserA);
        var csv = BuildCsv(2_000, "Reimport Type");

        var first = await WaitAsync(client, await PostJobId(client, csv), 120_000);
        first.GetProperty("processed").GetInt32().Should().Be(2_000);

        var second = await WaitAsync(client, await PostJobId(client, csv), 120_000);
        second.GetProperty("processed").GetInt32().Should().Be(0);
        second.GetProperty("skipped").GetInt32().Should().Be(2_000);
        second.GetProperty("status").GetString().Should().Be("completed");

        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<MainDbContext>();
        var count = await db.Exercises
            .IgnoreQueryFilters()
            .CountAsync(e => e.Workout.UserId == QaUserSwitchFactory.UserA
                && e.Comments!.StartsWith("reimport-"));
        count.Should().Be(2_000, "reimport must not duplicate a single row");
    }

    // ---- cross-user isolation ----------------------------------------------

    [Fact]
    public async Task Probe_other_users_job_returns_404()
    {
        var userA = _factory.ClientFor(QaUserSwitchFactory.UserA);
        var userB = _factory.ClientFor(QaUserSwitchFactory.UserB);

        var jobId = await PostJobId(userA, Header + "09/09/42,Isolation Type,3,8,60,isolated row\n");

        (await userB.GetAsync($"/data/import/{jobId}"))
            .StatusCode.Should().Be(HttpStatusCode.NotFound);
        (await userA.GetAsync($"/data/import/{jobId}"))
            .StatusCode.Should().Be(HttpStatusCode.OK);
    }

    [Fact]
    public async Task Probe_same_csv_from_two_users_imports_twice_without_leaking()
    {
        var userA = _factory.ClientFor(QaUserSwitchFactory.UserA);
        var userB = _factory.ClientFor(QaUserSwitchFactory.UserB);
        var csv = BuildCsv(1_200, "Shared Content Type");

        var jobA = await WaitAsync(userA, await PostJobId(userA, csv), 120_000);
        var jobB = await WaitAsync(userB, await PostJobId(userB, csv), 120_000);

        // The ClientEntityId unique index is global; the deterministic id must
        // be salted per user or user B would silently skip user A's rows.
        jobA.GetProperty("processed").GetInt32().Should().Be(1_200);
        jobB.GetProperty("processed").GetInt32().Should().Be(1_200,
            "user B must not be deduped against user A's rows");

        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<MainDbContext>();
        var rowsA = await db.Exercises.IgnoreQueryFilters()
            .CountAsync(e => e.Workout.UserId == QaUserSwitchFactory.UserA
                && e.Comments!.StartsWith("shared-content-"));
        var rowsB = await db.Exercises.IgnoreQueryFilters()
            .CountAsync(e => e.Workout.UserId == QaUserSwitchFactory.UserB
                && e.Comments!.StartsWith("shared-content-"));
        rowsA.Should().Be(1_200);
        rowsB.Should().Be(1_200);
    }

    // ---- real-world CSV shapes ---------------------------------------------

    [Fact]
    public async Task Probe_utf8_bom_header_is_rejected_with_a_clear_error()
    {
        var client = _factory.ClientFor(QaUserSwitchFactory.UserA);
        var csv = "\uFEFF" + Header + "01/06/45,Bom Type,3,8,60,bom row\n";

        var response = await client.PostAsync("/data/import", BuildCsvContent(csv));

        // Verified behaviour: the BOM is stripped by the StreamReader, so an
        // Excel "CSV UTF-8" export is accepted and the row is imported.
        response.StatusCode.Should().Be(HttpStatusCode.Accepted);
        var job = await WaitAsync(client, JsonSerializer
            .Deserialize<JsonElement>(await response.Content.ReadAsStringAsync(), JsonOptions)
            .GetProperty("jobId").GetString()!, 30_000);
        job.GetProperty("processed").GetInt32().Should().Be(1);
    }

    [Fact]
    public async Task Probe_semicolon_delimited_csv_is_rejected()
    {
        var client = _factory.ClientFor(QaUserSwitchFactory.UserA);
        var csv = "Date;ExerciseType;Series;Reps;Weight;AdditionalComments\n" +
                  "01/06/45;Semi Type;3;8;60;semi row\n";

        var response = await client.PostAsync("/data/import", BuildCsvContent(csv));

        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
    }

    [Fact]
    public async Task Probe_octet_stream_content_type_with_csv_extension_is_accepted()
    {
        var client = _factory.ClientFor(QaUserSwitchFactory.UserA);
        var content = new MultipartFormDataContent();
        var payload = new ByteArrayContent(Encoding.UTF8.GetBytes(
            Header + "01/06/46,Octet Type,3,8,60,octet row\n"));
        payload.Headers.ContentType = new MediaTypeHeaderValue("application/octet-stream");
        content.Add(payload, "file", "IMPORT.CSV");

        var response = await client.PostAsync("/data/import", content);

        response.StatusCode.Should().Be(HttpStatusCode.Accepted,
            "browsers often send octet-stream for .csv uploads");
    }

    [Fact]
    public async Task Probe_missing_optional_columns_import_with_null_metrics()
    {
        var client = _factory.ClientFor(QaUserSwitchFactory.UserA);
        var csv = "Date,ExerciseType\n01/06/47,Sparse Type\n";

        var jobId = await PostJobId(client, csv);
        var job = await WaitAsync(client, jobId, 30_000);
        job.GetProperty("status").GetString().Should().Be("completed");
        job.GetProperty("processed").GetInt32().Should().Be(1);

        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<MainDbContext>();
        var exercise = await db.Exercises
            .IgnoreQueryFilters()
            .SingleAsync(e => e.Workout.UserId == QaUserSwitchFactory.UserA
                && e.Type!.Name == "Sparse Type");
        exercise.MaxRepetition.Should().BeNull();
        exercise.Volume.Should().BeNull();
        exercise.Intensity.Should().BeNull();
    }

    [Fact]
    public async Task Probe_weight_with_comma_decimal_is_reported_as_a_row_error()
    {
        var client = _factory.ClientFor(QaUserSwitchFactory.UserA);
        var csv = Header + "01/06/48,Comma Type,3,8,\"80,5\",comma decimal\n";

        var job = await WaitAsync(client, await PostJobId(client, csv), 30_000);

        job.GetProperty("status").GetString().Should().Be("completed_with_errors");
        job.GetProperty("processed").GetInt32().Should().Be(0);
        job.GetProperty("errorCount").GetInt32().Should().Be(1);
    }

    // ---- job lifetime -------------------------------------------------------

    [Fact]
    public async Task Probe_job_state_lives_only_in_memory_and_is_lost_on_restart()
    {
        var client = _factory.ClientFor(QaUserSwitchFactory.UserA);
        var jobId = await PostJobId(client, Header + "01/06/49,Restart Type,3,8,60,before restart\n");
        (await WaitAsync(client, jobId, 30_000))
            .GetProperty("status").GetString().Should().Be("completed");

        // A second factory == a fresh process: same jobId, new job registry.
        using var restarted = new QaUserSwitchFactory();
        var restartedClient = restarted.ClientFor(QaUserSwitchFactory.UserA);

        (await restartedClient.GetAsync($"/data/import/{jobId}"))
            .StatusCode.Should().Be(HttpStatusCode.NotFound,
                "job registry is in-memory: an API restart or a second instance " +
                "loses the job and the FE can no longer read its final state");
    }

    // ---- helpers ------------------------------------------------------------

    private static string BuildCsv(int rows, string typePrefix)
    {
        var builder = new StringBuilder();
        builder.Append(Header);
        for (var i = 0; i < rows; i++)
        {
            var day = 1 + i % 28;
            var month = 1 + (i / 28) % 11;
            builder.Append($"{day}/{month:D2}/43,{typePrefix} {i % 7},{1 + i % 5},{5 + i % 10},{40 + i % 100},{typePrefix.ToLowerInvariant().Replace(' ', '-')}-{i}\n");
        }
        return builder.ToString();
    }

    private static MultipartFormDataContent BuildCsvContent(string csv)
    {
        var content = new MultipartFormDataContent();
        var payload = new ByteArrayContent(Encoding.UTF8.GetBytes(csv));
        payload.Headers.ContentType = new MediaTypeHeaderValue("text/csv");
        content.Add(payload, "file", "import.csv");
        return content;
    }

    private static async Task<string> PostJobId(HttpClient client, string csv)
    {
        var response = await client.PostAsync("/data/import", BuildCsvContent(csv));
        response.StatusCode.Should().Be(HttpStatusCode.Accepted);
        var posted = JsonSerializer.Deserialize<JsonElement>(
            await response.Content.ReadAsStringAsync(), JsonOptions);
        return posted.GetProperty("jobId").GetString()!;
    }

    private static async Task<JsonElement> response_of(HttpClient client, string csv)
    {
        var response = await client.PostAsync("/data/import", BuildCsvContent(csv));
        response.StatusCode.Should().Be(HttpStatusCode.Accepted);
        var body = await response.Content.ReadAsStringAsync();
        return JsonSerializer.Deserialize<JsonElement>(body, JsonOptions);
    }

    private static async Task<JsonElement> WaitAsync(HttpClient client, string jobId, int timeoutMs)
    {
        var deadline = DateTime.UtcNow.AddMilliseconds(timeoutMs);
        while (DateTime.UtcNow < deadline)
        {
            var response = await client.GetAsync($"/data/import/{jobId}");
            response.StatusCode.Should().Be(HttpStatusCode.OK);
            var json = JsonSerializer.Deserialize<JsonElement>(
                await response.Content.ReadAsStringAsync(), JsonOptions);
            var status = json.GetProperty("status").GetString();
            if (status is "completed" or "completed_with_errors" or "failed")
            {
                return json;
            }
            await Task.Delay(50);
        }

        throw new TimeoutException($"Import job {jobId} did not finish in {timeoutMs} ms");
    }
}
