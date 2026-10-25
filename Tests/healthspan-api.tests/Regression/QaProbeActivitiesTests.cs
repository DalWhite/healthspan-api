using System.Net;
using System.Net.Http.Json;
using System.Text;
using System.Text.Json;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Xunit;
using Xunit.Abstractions;

namespace healthspan_api.tests.Regression;

// QA PROBE (kanban t_3c2457de) — P01/P02/P03/P10 assert the aligned FE<->API
// contract decided in t_0e23bbe6; P04-P09 remain observation-only.
public class QaProbeActivitiesTests : ApiTestBase
{
    private readonly ITestOutputHelper out_;

    public QaProbeActivitiesTests(HealthspanApiFactory factory, ITestOutputHelper output)
        : base(factory)
    {
        out_ = output;
    }

    private async Task<string> Observe(HttpResponseMessage r)
    {
        var body = await r.Content.ReadAsStringAsync();
        return $"{(int)r.StatusCode} {r.StatusCode} :: {(body.Length > 600 ? body[..600] + "…" : body)}";
    }

    private static WellnessActivityWriteResource Base(string type = "sauna") => new()
    {
        Type = type,
        Dimension = WellnessDimension.Health,
        OccurredOnUtc = DateTime.UtcNow.Date,
        DurationMinutes = 20
    };

    [Fact]
    public async Task P01_dimension_query_values()
    {
        await Client.PostAsJsonAsync("/api/activities", Base("sauna"));
        foreach (var v in new[] { "salud", "Health", "health", "HEALTH", "movilidad", "bogus" })
        {
            var response = await Client.GetAsync($"/api/activities?dimension={v}");
            out_.WriteLine($"P01 dimension={v} -> {await Observe(response)}");
            if (v.Equals("health", StringComparison.OrdinalIgnoreCase) || v == "Health" || v == "HEALTH")
                response.StatusCode.Should().Be(HttpStatusCode.OK, $"'{v}' must be accepted (case-insensitive)");
            else
                response.StatusCode.Should().Be(HttpStatusCode.BadRequest, $"'{v}' is not a contract value");
        }
    }

    [Fact]
    public async Task P02_get_response_shape_and_pagination()
    {
        for (var i = 0; i < 5; i++)
            await Client.PostAsJsonAsync("/api/activities", Base($"sauna{i}"));

        var plain = await Client.GetAsync("/api/activities");
        plain.StatusCode.Should().Be(HttpStatusCode.OK);
        var plainPage = await plain.Content.ReadFromJsonAsync<PagedActivitiesResource>();
        plainPage!.Should().NotBeNull("GET must return the {items,total,page,pageSize,hasMore} envelope");
        plainPage.Page.Should().Be(1);
        plainPage.PageSize.Should().Be(10);
        out_.WriteLine($"P02 GET /api/activities -> envelope total={plainPage.Total} items={plainPage.Items.Count} hasMore={plainPage.HasMore}");

        var paged = await Client.GetAsync("/api/activities?page=1&pageSize=2");
        paged.StatusCode.Should().Be(HttpStatusCode.OK);
        var pagedPage = await paged.Content.ReadFromJsonAsync<PagedActivitiesResource>();
        pagedPage!.Items.Count.Should().Be(2);
        pagedPage.PageSize.Should().Be(2);
        pagedPage.HasMore.Should().BeTrue();
        out_.WriteLine($"P02 GET ?page=1&pageSize=2 -> items={pagedPage.Items.Count} total={pagedPage.Total} hasMore={pagedPage.HasMore}");
    }

    [Fact]
    public async Task P03_fe_payload_shape_from_prototype()
    {
        // exact payload the register prototype sends (t_c35cb312): date/temperatureC/extra
        // are NOT contract fields -> must 400 with field errors, never silently drop data.
        var fePayload = """
        {"type":"sauna","dimension":"salud","date":"2026-10-23","durationMinutes":20,
         "frequencyPerWeek":3,"intensity":7,"temperatureC":85,"cycles":3,
         "notes":"Sesión post-entreno","extra":{"humidity_percent":15}}
        """;
        var asIs = await Client.PostAsync(
            "/api/activities", new StringContent(fePayload, Encoding.UTF8, "application/json"));
        out_.WriteLine("P03 FE payload as-is -> " + await Observe(asIs));
        asIs.StatusCode.Should().Be(HttpStatusCode.BadRequest);
        (await asIs.Content.ReadAsStringAsync()).Should().Contain("errors");

        // payload mapped to the contract (dimension label -> canonical name,
        // date -> occurredOnUtc, temperatureC -> temperatureCelsius, extra -> metadata)
        var mappedPayload = """
        {"type":"sauna","dimension":"health","occurredOnUtc":"2026-10-23","durationMinutes":20,
         "frequencyPerWeek":3,"intensity":7,"temperatureCelsius":85,"cycles":3,
         "notes":"Sesión post-entreno","metadata":{"humidity_percent":15}}
        """;
        var mapped = await Client.PostAsync(
            "/api/activities", new StringContent(mappedPayload, Encoding.UTF8, "application/json"));
        out_.WriteLine("P03 FE payload mapped to contract -> " + await Observe(mapped));
        mapped.StatusCode.Should().Be(HttpStatusCode.Created);
        var body = await mapped.Content.ReadAsStringAsync();
        body.Should().Contain("\"dimension\":\"health\"");
        body.Should().Contain("humidity_percent");
    }

    [Fact]
    public async Task P04_cross_user_put_delete()
    {
        var created = await (await Client.PostAsJsonAsync("/api/activities", Base("sauna"))).Content
            .ReadFromJsonAsync<WellnessActivityResource>();
        using (var scope = Factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<MainDbContext>();
            var stored = db.WellnessActivities.IgnoreQueryFilters().First(a => a.Id == created!.Id);
            stored.UserId = TestDataSeeder.TraineeUserId;
            await db.SaveChangesAsync();
        }
        out_.WriteLine("P04 GET other-user -> " + await Observe(await Client.GetAsync($"/api/activities/{created.Id}")));
        out_.WriteLine("P04 PUT other-user -> " + await Observe(await Client.PutAsJsonAsync($"/api/activities/{created.Id}", Base("sauna"))));
        out_.WriteLine("P04 DELETE other-user -> " + await Observe(await Client.DeleteAsync($"/api/activities/{created.Id}")));
        using (var scope = Factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<MainDbContext>();
            out_.WriteLine("P04 still in DB after cross-user PUT/DELETE? " +
                db.WellnessActivities.IgnoreQueryFilters().Any(a => a.Id == created.Id));
        }
    }

    [Fact]
    public async Task P05_validation_edges()
    {
        var longNotes = Base(); longNotes.Notes = new string('n', 2001);
        out_.WriteLine("P05 notes 2001 -> " + await Observe(await Client.PostAsJsonAsync("/api/activities", longNotes)));

        var t65 = Base(new string('a', 65));
        out_.WriteLine("P05 type 65 chars -> " + await Observe(await Client.PostAsJsonAsync("/api/activities", t65)));

        var negDur = Base(); negDur.DurationMinutes = -1;
        out_.WriteLine("P05 duration -1 -> " + await Observe(await Client.PostAsJsonAsync("/api/activities", negDur)));
        var zeroDur = Base(); zeroDur.DurationMinutes = 0;
        out_.WriteLine("P05 duration 0 -> " + await Observe(await Client.PostAsJsonAsync("/api/activities", zeroDur)));
        var zeroFreq = Base(); zeroFreq.FrequencyPerWeek = 0m;
        out_.WriteLine("P05 frequency 0 -> " + await Observe(await Client.PostAsJsonAsync("/api/activities", zeroFreq)));
        var halfFreq = Base(); halfFreq.FrequencyPerWeek = 0.5m;
        out_.WriteLine("P05 frequency 0.5 -> " + await Observe(await Client.PostAsJsonAsync("/api/activities", halfFreq)));
        var freq99 = Base(); freq99.FrequencyPerWeek = 99m;
        out_.WriteLine("P05 frequency 99 (FE max 7) -> " + await Observe(await Client.PostAsJsonAsync("/api/activities", freq99)));
        var temp100 = Base(); temp100.TemperatureCelsius = 100m;
        out_.WriteLine("P05 temp 100 (FE max 60) -> " + await Observe(await Client.PostAsJsonAsync("/api/activities", temp100)));
        var temp201 = Base(); temp201.TemperatureCelsius = 201m;
        out_.WriteLine("P05 temp 201 -> " + await Observe(await Client.PostAsJsonAsync("/api/activities", temp201)));
        var negCycles = Base(); negCycles.Cycles = -3;
        out_.WriteLine("P05 cycles -3 -> " + await Observe(await Client.PostAsJsonAsync("/api/activities", negCycles)));
        out_.WriteLine("P05 empty body {} -> " + await Observe(await Client.PostAsync(
            "/api/activities", new StringContent("{}", Encoding.UTF8, "application/json"))));
        out_.WriteLine("P05 no body -> " + await Observe(await Client.PostAsync(
            "/api/activities", new StringContent("", Encoding.UTF8, "application/json"))));
    }

    [Fact]
    public async Task P06_type_normalization_and_query()
    {
        var created = await (await Client.PostAsJsonAsync("/api/activities", Base("  Wim Hof  ")))
            .Content.ReadFromJsonAsync<WellnessActivityResource>();
        out_.WriteLine($"P06 stored type from '  Wim Hof  ' -> '{created!.Type}'");
        out_.WriteLine("P06 GET ?type=Wim%20Hof -> " + await Observe(await Client.GetAsync("/api/activities?type=Wim%20Hof")));
        out_.WriteLine("P06 GET ?type=WIM%20HOF -> " + await Observe(await Client.GetAsync("/api/activities?type=WIM%20HOF")));
        out_.WriteLine("P06 GET ?type=sauna -> " + await Observe(await Client.GetAsync("/api/activities?type=sauna")));
        var weird = Base("crossfit_wod#2");
        out_.WriteLine("P06 type 'crossfit_wod#2' -> " + await Observe(await Client.PostAsJsonAsync("/api/activities", weird)));
        var dots = Base("breathing.v2");
        out_.WriteLine("P06 type 'breathing.v2' -> " + await Observe(await Client.PostAsJsonAsync("/api/activities", dots)));
    }

    [Fact]
    public async Task P07_metadata_roundtrip_nested()
    {
        var r = Base("wim_hof");
        r.Metadata = new Dictionary<string, object?>
        {
            ["breathsPerRound"] = 35,
            ["rounds"] = new List<object?> { 1, 2, 3 },
            ["coach"] = new Dictionary<string, object?> { ["name"] = "Ana", ["cert"] = null },
            ["emptyObj"] = new Dictionary<string, object?>(),
            ["nullValue"] = null
        };
        var created = await (await Client.PostAsJsonAsync("/api/activities", r))
            .Content.ReadFromJsonAsync<WellnessActivityResource>();
        out_.WriteLine("P07 metadata out -> " + System.Text.Json.JsonSerializer.Serialize(created!.Metadata));
        var got = await (await Client.GetAsync($"/api/activities/{created.Id}")).Content
            .ReadFromJsonAsync<WellnessActivityResource>();
        out_.WriteLine("P07 metadata back -> " + System.Text.Json.JsonSerializer.Serialize(got!.Metadata));
    }

    [Fact]
    public async Task P08_date_filters_and_update_semantics()
    {
        await Client.PostAsJsonAsync("/api/activities", Base("hoy"));
        var old = Base("antigua"); old.OccurredOnUtc = DateTime.UtcNow.Date.AddDays(-30);
        await Client.PostAsJsonAsync("/api/activities", old);

        out_.WriteLine("P08 from>to -> " + await Observe(await Client.GetAsync(
            $"/api/activities?from={DateTime.UtcNow.Date:yyyy-MM-dd}&to={DateTime.UtcNow.Date.AddDays(-10):yyyy-MM-dd}")));
        out_.WriteLine("P08 from only -> " + await Observe(await Client.GetAsync(
            $"/api/activities?from={DateTime.UtcNow.Date.AddDays(-5):yyyy-MM-dd}")));
        out_.WriteLine("P08 bad date 'notadate' -> " + await Observe(await Client.GetAsync("/api/activities?from=notadate")));
        out_.WriteLine("P08 bad id guid -> " + await Observe(await Client.GetAsync("/api/activities/no-es-guid")));

        var created = await (await Client.PostAsJsonAsync("/api/activities", Base("meditation")))
            .Content.ReadFromJsonAsync<WellnessActivityResource>();
        var put = Base("meditation_edit");
        var putResp = await Client.PutAsJsonAsync($"/api/activities/{created.Id}", put);
        out_.WriteLine("P08 PUT -> " + await Observe(putResp));
        out_.WriteLine("P08 PUT unknown id -> " + await Observe(await Client.PutAsJsonAsync($"/api/activities/{Guid.NewGuid()}", put)));
        out_.WriteLine("P08 PUT invalid body -> " + await Observe(await Client.PutAsJsonAsync($"/api/activities/{created.Id}", Base("bad type !!"))));
        out_.WriteLine("P08 DELETE -> " + await Observe(await Client.DeleteAsync($"/api/activities/{created.Id}")));
        out_.WriteLine("P08 DELETE again -> " + await Observe(await Client.DeleteAsync($"/api/activities/{created.Id}")));
    }

    [Fact]
    public async Task P09_date_future_and_timezone()
    {
        var todayLate = Base(); todayLate.OccurredOnUtc = DateTime.UtcNow.Date.AddDays(1).AddSeconds(-1);
        out_.WriteLine("P09 today 23:59:59 -> " + await Observe(await Client.PostAsJsonAsync("/api/activities", todayLate)));
        var tomorrow = Base(); tomorrow.OccurredOnUtc = DateTime.UtcNow.Date.AddDays(1);
        out_.WriteLine("P09 tomorrow 00:00 -> " + await Observe(await Client.PostAsJsonAsync("/api/activities", tomorrow)));
        // FE sends a local date string "2026-10-23" (no time)
        out_.WriteLine("P09 date string '2026-10-23' -> " + await Observe(await Client.PostAsync(
            "/api/activities", new StringContent(
                "{\"type\":\"sauna\",\"dimension\":\"health\",\"occurredOnUtc\":\"2026-10-23\"}",
                Encoding.UTF8, "application/json"))));
        // FE sends local CEST datetime with offset
        out_.WriteLine("P09 date '2026-10-23T23:30:00+02:00' -> " + await Observe(await Client.PostAsync(
            "/api/activities", new StringContent(
                "{\"type\":\"sauna\",\"dimension\":\"health\",\"occurredOnUtc\":\"2026-10-23T23:30:00+02:00\"}",
                Encoding.UTF8, "application/json"))));
    }

    [Fact]
    public async Task P10_date_only_and_intensity_bounds()
    {
        // date-only string with a numeric dimension (legacy shape) -> still accepted,
        // stored/returned as explicit UTC with Z.
        var dateOnly = await Client.PostAsync(
            "/api/activities", new StringContent(
                "{\"type\":\"sauna\",\"dimension\":1,\"occurredOnUtc\":\"2026-10-20\"}",
                Encoding.UTF8, "application/json"));
        out_.WriteLine("P10 date-only '2026-10-20' + dimension:1 -> " + await Observe(dateOnly));
        dateOnly.StatusCode.Should().Be(HttpStatusCode.Created);
        (await dateOnly.Content.ReadAsStringAsync()).Should().Contain("\"occurredOnUtc\":\"2026-10-20T00:00:00Z\"");

        // dimension omitted entirely -> 400, never a silent Strength default
        var omitted = await Client.PostAsync(
            "/api/activities", new StringContent(
                "{\"type\":\"sauna\",\"occurredOnUtc\":\"2026-10-20T10:00:00Z\"}",
                Encoding.UTF8, "application/json"));
        out_.WriteLine("P10 dimension omitted -> " + await Observe(omitted));
        omitted.StatusCode.Should().Be(HttpStatusCode.BadRequest);
        (await omitted.Content.ReadAsStringAsync()).Should().Contain("activity_dimension_required");

        // intensity outside the documented 0-10 range
        var hi = Base("intensity_hi"); hi.Intensity = 11m;
        var hiResp = await Client.PostAsJsonAsync("/api/activities", hi);
        out_.WriteLine("P10 intensity 11 -> " + await Observe(hiResp));
        hiResp.StatusCode.Should().Be(HttpStatusCode.BadRequest);

        var lo = Base("intensity_lo"); lo.Intensity = -1m;
        var loResp = await Client.PostAsJsonAsync("/api/activities", lo);
        out_.WriteLine("P10 intensity -1 -> " + await Observe(loResp));
        loResp.StatusCode.Should().Be(HttpStatusCode.BadRequest);

        // FE-style unknown top-level fields (name/style/breathsPerRound/extra) -> 400,
        // never a 201 with metadata:null that silently drops the user's data
        var unknown = await Client.PostAsync(
            "/api/activities", new StringContent(
                "{\"type\":\"other\",\"dimension\":\"health\",\"occurredOnUtc\":\"2026-10-20T10:00:00Z\",\"name\":\"Padel\",\"style\":\"Guiada\",\"breathsPerRound\":30,\"extra\":{\"pistas\":2},\"durationMinutes\":60}",
                Encoding.UTF8, "application/json"));
        out_.WriteLine("P10 FE unknown fields -> " + await Observe(unknown));
        unknown.StatusCode.Should().Be(HttpStatusCode.BadRequest);
    }

    [Fact]
    public async Task P11_read_serialization_utc_Z_and_clean_decimals()
    {
        // QA-C F1 (t_dbb640bd): GET responses must carry the 'Z' UTC designator
        // (MySQL/EF return Kind=Unspecified without a converter) and must not
        // leak MySQL decimal scale (intensity 7 -> "7", not 7.000000000000000000…).
        var created = await Client.PostAsJsonAsync("/api/activities", new
        {
            type = "other",
            dimension = "health",
            occurredOnUtc = "2026-10-24T18:30:00Z",
            durationMinutes = 20,
            frequencyPerWeek = 3,
            intensity = 7,
            temperatureCelsius = 85,
            cycles = 3,
            metadata = new { probe = "read-serialize" }
        });
        created.StatusCode.Should().Be(HttpStatusCode.Created);
        var createdBody = await created.Content.ReadAsStringAsync();
        var id = JsonDocument.Parse(createdBody).RootElement.GetProperty("id").GetGuid();

        var byId = await Client.GetAsync($"/api/activities/{id}");
        byId.StatusCode.Should().Be(HttpStatusCode.OK);
        var byIdBody = await byId.Content.ReadAsStringAsync();
        out_.WriteLine($"P11 GET /api/activities/{id} -> {byIdBody}");
        byIdBody.Should().Contain("\"occurredOnUtc\":\"2026-10-24T18:30:00Z\"");
        byIdBody.Should().MatchRegex("\"createdAtUtc\":\"[0-9T:.-]+Z\"");
        byIdBody.Should().Contain("\"intensity\":7").And.NotContain("7.0");
        byIdBody.Should().Contain("\"temperatureCelsius\":85").And.NotContain("85.0");
        byIdBody.Should().Contain("\"frequencyPerWeek\":3").And.NotContain("3.0");

        var list = await Client.GetAsync("/api/activities?page=1&pageSize=5");
        list.StatusCode.Should().Be(HttpStatusCode.OK);
        var listBody = await list.Content.ReadAsStringAsync();
        out_.WriteLine($"P11 GET list -> {(listBody.Length > 600 ? listBody[..600] + "…" : listBody)}");
        listBody.Should().MatchRegex("\"occurredOnUtc\":\"[0-9T:.-]+Z\"");
    }
}
