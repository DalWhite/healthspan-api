using System.Net;
using FluentAssertions;
using Xunit;

namespace healthspan_api.tests.Regression;

public class CorsTests : ApiTestBase
{
    public CorsTests(HealthspanApiFactory factory) : base(factory) { }

    [Fact]
    public async Task GetExerciseTypes_WithAllowedOrigin_ReturnsCorsHeader()
    {
        var request = new HttpRequestMessage(HttpMethod.Get, "/exercisetypes");
        request.Headers.Add("Origin", "https://localhost:5001");

        var response = await Client.SendAsync(request);

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        response.Headers.TryGetValues("Access-Control-Allow-Origin", out var origins).Should().BeTrue();
        origins.Should().ContainSingle("https://localhost:5001");
    }

    [Fact]
    public async Task Options_ExerciseTypes_WithAllowedOrigin_ReturnsCorsHeaders()
    {
        var request = new HttpRequestMessage(HttpMethod.Options, "/exercisetypes");
        request.Headers.Add("Origin", "https://localhost:5001");
        request.Headers.Add("Access-Control-Request-Method", "GET");
        request.Headers.Add("Access-Control-Request-Headers", "authorization");

        var response = await Client.SendAsync(request);

        response.StatusCode.Should().Be(HttpStatusCode.NoContent);
        response.Headers.TryGetValues("Access-Control-Allow-Origin", out var origins).Should().BeTrue();
        response.Headers.TryGetValues("Access-Control-Allow-Methods", out var methods).Should().BeTrue();
        response.Headers.TryGetValues("Access-Control-Allow-Headers", out var headers).Should().BeTrue();
        origins.Should().ContainSingle("https://localhost:5001");
        methods.Should().Contain("GET");
        headers.Should().Contain("authorization");
    }

    [Fact]
    public async Task GetWorkouts_Exposes_TotalCount_Header_To_The_Frontend()
    {
        var request = new HttpRequestMessage(
            HttpMethod.Get,
            "/workouts?page=1&pageSize=20");
        request.Headers.Add("Origin", "https://localhost:5001");

        var response = await Client.SendAsync(request);

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        response.Headers.TryGetValues(
            "Access-Control-Expose-Headers",
            out var exposedHeaders).Should().BeTrue();
        string.Join(",", exposedHeaders!)
            .Should().ContainEquivalentOf("X-Total-Count");
        response.Headers.TryGetValues(
            "X-Total-Count",
            out var totalCount).Should().BeTrue();
        totalCount.Should().ContainSingle();
    }
}
