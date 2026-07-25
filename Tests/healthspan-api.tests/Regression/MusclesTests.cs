using System.Net;
using System.Net.Http.Json;
using FluentAssertions;
using Xunit;

namespace healthspan_api.tests.Regression;

public class MusclesTests : ApiTestBase
{
    public MusclesTests(HealthspanApiFactory factory) : base(factory) { }

    [Fact]
    public async Task GetAll_Returns_SeededMuscles()
    {
        var response = await Client.GetAsync("/api/muscles");

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        var json = await response.Content.ReadAsStringAsync();
        json.Should().Contain("Chest");
        json.Should().Contain("Quads");
    }

    [Fact]
    public async Task GetById_Returns_CorrectMuscle()
    {
        var response = await Client.GetAsync($"/api/muscles/{TestDataSeeder.ChestMuscleId}");

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        var json = await response.Content.ReadAsStringAsync();
        json.Should().Contain("Chest");
    }
}
