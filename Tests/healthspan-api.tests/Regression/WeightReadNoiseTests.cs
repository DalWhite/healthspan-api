#nullable enable
using System.Net;
using System.Net.Http.Json;
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
/// t_50052fc9: QA asked whether the Exercises/Workouts weight read paths
/// (ExercisesController.MapExercise, WorkoutsController session/history maps)
/// carry the same decimal noise QA found for activities (t_e395b7be / D1).
///
/// Verified NO — and these tests pin down WHY, so a future schema change that
/// reintroduces the mechanism fails loudly:
///
///  - Activities' bug lives in Pomelo's DECIMAL(65,30) reader: the CLR entity
///    property is decimal, the MySQL read widens a stored double to a
///    double-derived decimal at scale 26-28 (85.5 -> 85.499...990), and the
///    (double?) cast re-emits it. See DecimalReadNoiseTests.
///  - Here the entity properties Exercise.Weight / BodyWeight / AddedWeight
///    are CLR double over MySQL double columns (Models/Exercise.cs). Pomelo
///    never widens them; the read-path cast is double -> decimal, and .NET
///    rounds that conversion to 15 significant digits, so JSON round-trips the
///    written value exactly. A dev-API probe over the QA matrix (85.5, 33.3,
///    80.5, 71.93, 12.34, 0.1, 71.931, 1234.5678) emitted every value verbatim.
///
/// Applying ToCleanDouble (round to 6 dp) to these paths would CORRUPT valid
/// values (1/3, 1234.5678) — the fix from PR #22 is deliberately not copied.
/// Do not change these columns to decimal without re-reading DecimalReadNoiseTests.
/// </summary>
public class WeightReadNoiseTests : ApiTestBase
{
    public WeightReadNoiseTests(HealthspanApiFactory factory) : base(factory)
    {
    }

    // The exact values the activities probe failed on, plus long-decimal values
    // that a 6-dp round would destroy.
    private static readonly string[] Matrix =
        ["85.5", "33.3", "80.5", "71.93", "12.34", "0.1", "6", "71.931", "1234.5678"];

    [Fact]
    public void Weight_columns_are_double_not_decimal_the_activities_bug_cannot_apply()
    {
        using var scope = Factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<MainDbContext>();
        var entity = db.Model.FindEntityType(typeof(Exercise))!;

        foreach (var name in new[] { "Weight", "BodyWeight", "AddedWeight" })
        {
            var property = entity.FindProperty(name)!;
            property.ClrType.Should().Be(
                typeof(double?),
                $"{name} drifted off double: if it became decimal, Pomelo's " +
                "DECIMAL(65,30) reader reintroduces the activities noise and " +
                "MapExercise needs the ToCleanDouble treatment (DecimalReadNoiseTests).");
        }
    }

    [Theory]
    [InlineData("85.5", "85.5")]
    [InlineData("33.3", "33.3")]
    [InlineData("80.5", "80.5")]
    [InlineData("71.93", "71.93")]
    [InlineData("12.34", "12.34")]
    [InlineData("0.1", "0.1")]
    [InlineData("6", "6")]
    [InlineData("71.931", "71.931")]
    [InlineData("1234.5678", "1234.5678")]
    // double -> decimal rounds to <=15 significant digits: a benign bound, not
    // the activities bug (scale-26 garbage). Real weights have <15 sig digits.
    [InlineData("0.3333333333333333", "0.333333333333333")]
    public void Read_path_cast_double_to_decimal_emits_the_exact_value(string written, string expectedJson)
    {
        // Exactly what MapExercise does: stored double -> (decimal?)Value on the resource.
        double stored = double.Parse(written, System.Globalization.CultureInfo.InvariantCulture);
        decimal mapped = (decimal)stored;

        // The decimal-cast read path truncates at 15 significant digits (benign);
        // the raw double path is exact round-trip by definition.
        JsonSerializer.Serialize((decimal?)mapped).Should().Be(expectedJson);
        JsonSerializer.Serialize((double?)stored).Should().Be(written);
    }

    [Theory]
    [InlineData("85.49999999999999999999999999990", "85.5")]
    [InlineData("33.30000000000000000000000000000", "33.3")]
    public void If_the_activities_noise_ever_reached_a_double_it_would_resurface(string readerArtifact, string expectedClean)
    {
        // Contrast fixture: proves the guard above matters. A decimal read artifact
        // (activities-style widened decimal) cast to double re-emits the bug —
        // which is impossible with the current double columns because Pomelo
        // hands EF a plain double.
        decimal artifact = decimal.Parse(readerArtifact, System.Globalization.CultureInfo.InvariantCulture);
        JsonSerializer.Serialize((double?)artifact).Should().NotBe(expectedClean);
    }

    // Product rule (ExercisesRepository.CalculateComputedValues): the
    // bodyWeight/addedWeight breakdown is only persisted for exercise types with
    // WeightType == "BodyWeight"; for other types it is nulled by design. The
    // breakdown round-trip tests below therefore use a BodyWeight-type probe.
    private const int BodyWeightProbeTypeId = 9901;

    private async Task SeedBodyWeightProbeTypeAsync()
    {
        using var scope = Factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<MainDbContext>();
        if (!await db.ExerciseTypes.AnyAsync(t => t.Id == BodyWeightProbeTypeId))
        {
            db.ExerciseTypes.Add(new ExerciseType
            {
                Id = BodyWeightProbeTypeId,
                Name = "Dip (probe)",
                Description = "probe",
                ImageRoute = "",
                DifficultyLevel = 1,
                WeightType = "BodyWeight",
            });
            await db.SaveChangesAsync();
        }
    }

    [Fact]
    public async Task Session_exercise_round_trip_emits_weights_verbatim()
    {
        await SeedBodyWeightProbeTypeAsync();
        var created = await Client.PostAsJsonAsync("/Workouts", new { date = "2026-10-25T00:00:00Z" });
        created.StatusCode.Should().Be(HttpStatusCode.Created);
        var workoutId = JsonSerializer.Deserialize<JsonElement>(
            await created.Content.ReadAsStringAsync()).GetProperty("id").GetString();

        foreach (var v in Matrix)
        {
            double w = double.Parse(v, System.Globalization.CultureInfo.InvariantCulture);
            var added = await Client.PostAsJsonAsync($"/Workouts/{workoutId}/exercises", new
            {
                typeId = BodyWeightProbeTypeId,
                series = 3,
                repetitions = 10,
                bodyWeight = w,
                addedWeight = w,
            });
            added.StatusCode.Should().BeOneOf(HttpStatusCode.OK, HttpStatusCode.Created);
        }

        var body = await (await Client.GetAsync($"/Workouts/{workoutId}")).Content.ReadAsStringAsync();
        foreach (var v in Matrix)
        {
            body.Should().Contain($"\"bodyWeight\":{v}", $"bodyWeight {v} not emitted verbatim: {Trim(body)}");
            body.Should().Contain($"\"addedWeight\":{v}", $"addedWeight {v} not emitted verbatim");
            // weight is recomputed as bodyWeight + addedWeight for this load type.
            double sum = double.Parse(v, System.Globalization.CultureInfo.InvariantCulture) * 2;
            body.Should().Contain($"\"weight\":{sum.ToString(System.Globalization.CultureInfo.InvariantCulture)}",
                $"summed weight for {v} not emitted verbatim");
        }
        body.Should().NotMatchRegex(@"\d+\.\d{15,}", "long-decimal noise in workout read path");
    }

    [Fact]
    public async Task Standard_type_nulls_weight_breakdown_by_design_not_by_noise()
    {
        // QA seen bodyWeight:null reads for Bench Press (WeightType=Standard).
        // That is the documented product rule, not the activities D1 bug.
        var created = await Client.PostAsJsonAsync("/Workouts", new { date = "2026-10-25T00:00:00Z" });
        var workoutId = JsonSerializer.Deserialize<JsonElement>(
            await created.Content.ReadAsStringAsync()).GetProperty("id").GetString();

        await Client.PostAsJsonAsync($"/Workouts/{workoutId}/exercises", new
        {
            typeId = TestDataSeeder.BenchPressTypeId,
            series = 3,
            repetitions = 10,
            weight = 85.5,
            bodyWeight = 71.93,
            addedWeight = 12.34,
        });

        var body = await (await Client.GetAsync($"/Workouts/{workoutId}")).Content.ReadAsStringAsync();
        body.Should().Contain("\"weight\":85.5");
        body.Should().Contain("\"bodyWeight\":null",
            "Standard types collapse the breakdown by design (CalculateComputedValues)");
        body.Should().Contain("\"addedWeight\":null");
    }

    [Fact]
    public async Task Exercise_post_get_put_round_trip_emits_weights_verbatim()
    {
        await SeedBodyWeightProbeTypeAsync();
        var created = await Client.PostAsJsonAsync("/Workouts", new { date = "2026-10-25T00:00:00Z" });
        var workoutId = JsonSerializer.Deserialize<JsonElement>(
            await created.Content.ReadAsStringAsync()).GetProperty("id").GetString();

        foreach (var v in Matrix)
        {
            double w = double.Parse(v, System.Globalization.CultureInfo.InvariantCulture);
            double sum = w * 2; // recomputed weight = bodyWeight + addedWeight

            var posted = await Client.PostAsJsonAsync("/Exercises", new
            {
                typeId = BodyWeightProbeTypeId,
                workoutId,
                series = 3,
                repetitions = 10,
                bodyWeight = w,
                addedWeight = w,
            });
            posted.StatusCode.Should().Be(HttpStatusCode.Created);
            var id = JsonSerializer.Deserialize<JsonElement>(
                await posted.Content.ReadAsStringAsync()).GetProperty("id").GetString();

            var got = await Client.GetAsync($"/Exercises/{id}");
            got.StatusCode.Should().Be(HttpStatusCode.OK);
            var gotBody = await got.Content.ReadAsStringAsync();
            var sumJson = sum.ToString(System.Globalization.CultureInfo.InvariantCulture);
            gotBody.Should().Contain($"\"weight\":{sumJson}", $"GET weight {sumJson}: {Trim(gotBody)}");
            gotBody.Should().Contain($"\"bodyWeight\":{v}", $"GET bodyWeight {v}: {Trim(gotBody)}");
            gotBody.Should().Contain($"\"addedWeight\":{v}", $"GET addedWeight {v}");

            var updated = await Client.PutAsJsonAsync("/Exercises", new
            {
                id,
                series = 4,
                repetitions = 8,
                bodyWeight = w,
                addedWeight = w,
                tags = Array.Empty<string>(),
            });
            updated.StatusCode.Should().Be(HttpStatusCode.OK);
            var putBody = await updated.Content.ReadAsStringAsync();
            putBody.Should().Contain($"\"weight\":{sumJson}", $"PUT weight {sumJson}: {Trim(putBody)}");
            putBody.Should().Contain($"\"bodyWeight\":{v}", $"PUT bodyWeight {v}: {Trim(putBody)}");
            putBody.Should().NotMatchRegex(@"\d+\.\d{15,}", "long-decimal noise in exercise read path");
        }
    }

    private static string Trim(string body) =>
        body.Length <= 240 ? body : body[..240];
}

/// <summary>
/// Opt-in E2E against a REAL MySQL (Pomelo is the layer where the activities bug
/// lived, so this is where cleanliness must be proven):
///   BENCHMARK_DB_CONNECTION_STRING='...' \
///   dotnet test Tests/healthspan-api.tests --filter "FullyQualifiedName~WeightReadNoiseRealDbTests"
/// Runs on a throwaway healthspan_bench_* schema (dropped on dispose).
/// </summary>
public class WeightReadNoiseRealDbTests
{
    private static string? ConnectionString =>
        Environment.GetEnvironmentVariable("BENCHMARK_DB_CONNECTION_STRING");

    [Fact]
    public async Task Real_mysql_weight_round_trip_emits_values_verbatim()
    {
        if (string.IsNullOrWhiteSpace(ConnectionString)) return;

        using var factory = new MySqlBenchFactory(ConnectionString!);
        using var client = factory.CreateClient();

        using (var scope = factory.Services.CreateScope())
        {
            var userManager = scope.ServiceProvider.GetRequiredService<UserManager<User>>();
            if (await userManager.FindByIdAsync(HealthspanApiFactory.TestUserId) is null)
            {
                var created = await userManager.CreateAsync(
                    new User
                    {
                        Id = HealthspanApiFactory.TestUserId,
                        UserName = HealthspanApiFactory.TestUserEmail,
                        Email = HealthspanApiFactory.TestUserEmail,
                        Name = "Weight Probe User",
                    },
                    "ProbePassword123!");
                created.Succeeded.Should().BeTrue(string.Join("; ", created.Errors));
            }

            var db = scope.ServiceProvider.GetRequiredService<MainDbContext>();
            if (!await db.ExerciseTypes.AnyAsync(t => t.Id == 9902))
            {
                db.ExerciseTypes.Add(new ExerciseType
                {
                    Id = 9902,
                    Name = "Dip (probe)",
                    Description = "probe",
                    ImageRoute = "",
                    DifficultyLevel = 1,
                    WeightType = "BodyWeight",
                });
                await db.SaveChangesAsync();
            }
        }

        var createdWorkout = await client.PostAsJsonAsync("/Workouts", new { date = "2026-10-25T00:00:00Z" });
        createdWorkout.StatusCode.Should().Be(HttpStatusCode.Created);
        var workoutId = JsonSerializer.Deserialize<JsonElement>(
            await createdWorkout.Content.ReadAsStringAsync()).GetProperty("id").GetString();

        var matrix = new[] { "85.5", "33.3", "80.5", "71.93", "12.34", "0.1", "6", "1234.5678" };
        foreach (var v in matrix)
        {
            double w = double.Parse(v, System.Globalization.CultureInfo.InvariantCulture);
            var added = await client.PostAsJsonAsync($"/Workouts/{workoutId}/exercises", new
            {
                typeId = 9902, // WeightType=BodyWeight: breakdown persists by design
                series = 3,
                repetitions = 10,
                bodyWeight = w,
                addedWeight = w,
            });
            added.StatusCode.Should().BeOneOf(HttpStatusCode.OK, HttpStatusCode.Created);
        }

        var body = await (await client.GetAsync($"/Workouts/{workoutId}")).Content.ReadAsStringAsync();
        foreach (var v in matrix)
        {
            body.Should().Contain($"\"bodyWeight\":{v}", $"Pomelo read path altered {v}: {body[..Math.Min(240, body.Length)]}");
            body.Should().Contain($"\"addedWeight\":{v}", $"addedWeight {v}");
        }
        body.Should().NotMatchRegex(@"\d+\.\d{15,}", "long-decimal noise from the Pomelo reader");
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
