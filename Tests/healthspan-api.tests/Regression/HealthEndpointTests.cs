using System.Net;
using System.Net.Http.Json;
using FluentAssertions;
using Xunit;

namespace healthspan_api.tests.Regression;

public class HealthEndpointTests : ApiTestBase
{
    public HealthEndpointTests(HealthspanApiFactory factory) : base(factory) { }

    [Fact]
    public async Task Health_Check_Returns_Ok()
    {
        var response = await Client.GetAsync("/health");

        response.StatusCode.Should().Be(HttpStatusCode.OK);
    }

    [Fact]
    public async Task Health_Details_Returns_Ok()
    {
        var response = await Client.GetAsync("/health/details");

        response.StatusCode.Should().Be(HttpStatusCode.OK);
    }
}
