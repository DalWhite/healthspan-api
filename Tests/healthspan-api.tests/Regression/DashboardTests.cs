using System.Net;
using System.Net.Http.Json;
using FluentAssertions;
using Xunit;

namespace healthspan_api.tests.Regression;

public class DashboardTests : ApiTestBase
{
    public DashboardTests(HealthspanApiFactory factory) : base(factory) { }

    [Fact]
    public async Task GetDashboardSummary_Returns_AggregatedData()
    {
        var response = await Client.GetAsync("/dashboard/summary");

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        var summary = await response.Content.ReadFromJsonAsync<GetHealthDashboardSummaryResource>();
        summary.Should().NotBeNull();
        summary!.BloodTestCount.Should().BeGreaterThanOrEqualTo(1);
    }
}
