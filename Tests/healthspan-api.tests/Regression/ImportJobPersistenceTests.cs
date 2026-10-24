using System.Net;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using FluentAssertions;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace healthspan_api.tests.Regression;

/// <summary>
/// t_e26d8b7d: GET /data/import/{jobId} must survive an API restart. The
/// "restart" is simulated with two HealthspanApiFactory instances sharing one
/// in-memory database name: disposing the first is the process death, the
/// second is the restarted API with the same durable store.
/// </summary>
public class ImportJobPersistenceTests
{
    private static string NewSharedDbName() =>
        $"HealthspanImportPersist_{Guid.NewGuid():N}";

    private static async Task<(HealthspanApiFactory Factory, HttpClient Client)> StartApiAsync(
        string databaseName)
    {
        var factory = new HealthspanApiFactory { SharedDatabaseName = databaseName };
        using var scope = factory.Services.CreateScope();
        await TestDataSeeder.SeedAsync(scope.ServiceProvider);
        return (factory, factory.CreateClient());
    }

    [Fact]
    public async Task CompletedImportJob_Survives_ApiRestart()
    {
        var dbName = NewSharedDbName();

        var (factory1, client1) = await StartApiAsync(dbName);
        var jobId = await PostImportAsync(client1,
            "Date,ExerciseType,Series,Reps,Weight,AdditionalComments\n" +
            "01/06/48,Bench Press,4,10,80,restart survivor");

        var live = await GetStatusAsync(client1, jobId);
        live.StatusCode.Should().Be(HttpStatusCode.OK);

        await WaitForTerminalStatusAsync(client1, jobId);
        await factory1.DisposeAsync();

        // Restarted API: same durable store, nothing in memory.
        var (factory2, client2) = await StartApiAsync(dbName);
        try
        {
            var afterRestart = await GetStatusAsync(client2, jobId);
            afterRestart.StatusCode.Should().Be(HttpStatusCode.OK);
            afterRestart.Json.GetProperty("jobId").GetString().Should().Be(jobId);
            afterRestart.Json.GetProperty("status").GetString()
                .Should().BeOneOf("completed", "completed_with_errors");
            afterRestart.Json.GetProperty("total").GetInt32().Should().Be(1);
            afterRestart.Json.GetProperty("processed").GetInt32().Should().Be(1);
        }
        finally
        {
            await factory2.DisposeAsync();
        }
    }

    [Fact]
    public async Task OrphanedRunningJob_AfterRestart_Returns_Job_Not404()
    {
        var dbName = NewSharedDbName();
        var (factory, client) = await StartApiAsync(dbName);
        var jobId = Guid.NewGuid().ToString("N");

        // Simulate the durable state left behind by a crash mid-import:
        // status still "running", no live owner in any process.
        using (var scope = factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<MainDbContext>();
            db.ImportJobRecords.Add(new ImportJobRecord
            {
                Id = jobId,
                UserId = HealthspanApiFactory.TestUserId,
                Status = "running",
                Total = 1_000,
                Processed = 500,
                Skipped = 10,
                ErrorCount = 2,
                StartedAt = DateTimeOffset.UtcNow.AddMinutes(-5),
                UpdatedAt = DateTimeOffset.UtcNow.AddSeconds(-30),
                ErrorsJson = "[{\"Row\":7,\"Message\":\"bad row A\"},{\"Row\":9,\"Message\":\"bad row B\"}]",
            });
            await db.SaveChangesAsync();
        }

        var response = await GetStatusAsync(client, jobId);
        response.StatusCode.Should().Be(HttpStatusCode.OK);
        response.Json.GetProperty("status").GetString().Should().Be("failed");
        response.Json.GetProperty("failureReason").GetString()
            .Should().Contain("reinicio");
        response.Json.GetProperty("total").GetInt32().Should().Be(1_000);
        response.Json.GetProperty("processed").GetInt32().Should().Be(500);
        response.Json.GetProperty("skipped").GetInt32().Should().Be(10);
        response.Json.GetProperty("errorCount").GetInt32().Should().Be(2);
        response.Json.GetProperty("errors")[0].GetProperty("row").GetInt32()
            .Should().Be(7);
        response.Json.GetProperty("finishedAt").ValueKind
            .Should().NotBe(JsonValueKind.Null);

        // The interruption is persisted, not just computed per request.
        var again = await GetStatusAsync(client, jobId);
        again.StatusCode.Should().Be(HttpStatusCode.OK);
        again.Json.GetProperty("status").GetString().Should().Be("failed");

        await factory.DisposeAsync();
    }

    [Fact]
    public async Task Restart_During_Active_Import_Keeps_Job_Findable()
    {
        var dbName = NewSharedDbName();

        var (factory1, client1) = await StartApiAsync(dbName);
        var jobId = await PostImportAsync(client1, BuildLargeCsv(20_000));

        // The job is mid-flight when the API dies.
        var midImport = await GetStatusAsync(client1, jobId);
        midImport.StatusCode.Should().Be(HttpStatusCode.OK);
        midImport.Json.GetProperty("status").GetString()
            .Should().BeOneOf("queued", "running");

        await factory1.DisposeAsync();

        var (factory2, client2) = await StartApiAsync(dbName);
        try
        {
            // Core acceptance: 200 with the current job state, not 404.
            var afterRestart = await GetStatusAsync(client2, jobId);
            afterRestart.StatusCode.Should().Be(HttpStatusCode.OK);
            afterRestart.Json.GetProperty("jobId").GetString().Should().Be(jobId);
            afterRestart.Json.GetProperty("status").GetString()
                .Should().BeOneOf("queued", "running", "failed", "completed", "completed_with_errors");
            afterRestart.Json.GetProperty("total").GetInt32().Should().Be(20_000);
        }
        finally
        {
            await factory2.DisposeAsync();
        }
    }

    [Fact]
    public async Task ImportJobRecord_Of_Another_User_Returns404()
    {
        var dbName = NewSharedDbName();
        var (factory, client) = await StartApiAsync(dbName);

        using (var scope = factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<MainDbContext>();
            db.ImportJobRecords.Add(new ImportJobRecord
            {
                Id = Guid.NewGuid().ToString("N"),
                UserId = "some-other-user",
                Status = "completed",
                Total = 1,
                Processed = 1,
                UpdatedAt = DateTimeOffset.UtcNow,
            });
            await db.SaveChangesAsync();
        }

        var foreign = await client.GetAsync(
            $"/data/import/{FindForeignJobId(factory)}");
        foreign.StatusCode.Should().Be(HttpStatusCode.NotFound);

        await factory.DisposeAsync();
    }

    private static string FindForeignJobId(HealthspanApiFactory factory)
    {
        using var scope = factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<MainDbContext>();
        var record = db.ImportJobRecords
            .First(entry => entry.UserId == "some-other-user");
        return record.Id;
    }

    private static string BuildLargeCsv(int rows)
    {
        var sb = new StringBuilder(
            "Date,ExerciseType,Series,Reps,Weight,AdditionalComments\n");
        for (var i = 0; i < rows; i++)
        {
            // 400 distinct dates x distinct weights per date: every row is
            // unique, so nothing is deduped before the API dies.
            var date = new DateOnly(2024, 1, 1).AddDays(i % 400);
            var weight = 60 + i / 400;
            sb.Append(date.ToString("dd/MM/yy"))
              .Append(",Bench Press,4,10,")
              .Append(weight.ToString(System.Globalization.CultureInfo.InvariantCulture))
              .Append(",restart row ").Append(i).Append('\n');
        }
        return sb.ToString();
    }

    private static async Task<string> PostImportAsync(HttpClient client, string csv)
    {
        using var content = new MultipartFormDataContent();
        var fileContent = new ByteArrayContent(Encoding.UTF8.GetBytes(csv));
        fileContent.Headers.ContentType = new MediaTypeHeaderValue("text/csv");
        content.Add(fileContent, "file", "import.csv");

        var response = await client.PostAsync("/data/import", content);
        response.StatusCode.Should().Be(HttpStatusCode.Accepted);

        var posted = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        return posted.RootElement.GetProperty("jobId").GetString()!;
    }

    private static async Task<(HttpStatusCode StatusCode, JsonElement Json)> GetStatusAsync(
        HttpClient client,
        string jobId)
    {
        var response = await client.GetAsync($"/data/import/{jobId}");
        var json = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        return (response.StatusCode, json.RootElement.Clone());
    }

    private static async Task WaitForTerminalStatusAsync(HttpClient client, string jobId)
    {
        var deadline = DateTime.UtcNow.AddSeconds(60);
        while (DateTime.UtcNow < deadline)
        {
            var status = await GetStatusAsync(client, jobId);
            if (status.Json.GetProperty("status").GetString()
                    is "completed" or "completed_with_errors" or "failed")
            {
                return;
            }
            await Task.Delay(50);
        }
        throw new TimeoutException($"Import job {jobId} did not finish in 60 s");
    }
}
