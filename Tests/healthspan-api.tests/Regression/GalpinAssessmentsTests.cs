using System.Net;
using System.Net.Http.Json;
using FluentAssertions;
using Xunit;

namespace healthspan_api.tests.Regression;

public class GalpinAssessmentsTests : ApiTestBase
{
    public GalpinAssessmentsTests(HealthspanApiFactory factory) : base(factory) { }

    [Fact]
    public async Task GetCatalog_Returns_AllAdaptations()
    {
        var response = await Client.GetAsync("/galpinassessments/catalog");

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        var catalog = await response.Content.ReadFromJsonAsync<List<GalpinAdaptationCatalogItemResource>>();
        catalog.Should().NotBeNull();
        catalog!.Count.Should().BeGreaterThanOrEqualTo(9); // All 9 Galpin adaptation types
    }

    [Fact]
    public async Task AddAssessment_Then_GetAssessments_Returns_It()
    {
        var newAssessment = new AddGalpinAssessmentResource
        {
            AssessedOnUtc = DateTime.UtcNow,
            Notes = "Test assessment",
            Scores = new List<GalpinAssessmentScoreResource>
            {
                new() { Adaptation = GalpinAdaptationType.Strength, Score = 7.5m, EvidenceNotes = "Good strength" },
                new() { Adaptation = GalpinAdaptationType.Hypertrophy, Score = 6.0m, EvidenceNotes = "Moderate" },
                new() { Adaptation = GalpinAdaptationType.Speed, Score = 5.0m }
            }
        };

        var createResponse = await Client.PostAsJsonAsync("/galpinassessments", newAssessment);
        createResponse.StatusCode.Should().Be(HttpStatusCode.Created);

        var listResponse = await Client.GetAsync("/galpinassessments");
        listResponse.StatusCode.Should().Be(HttpStatusCode.OK);
        var assessments = await listResponse.Content.ReadFromJsonAsync<List<GetGalpinAssessmentResource>>();
        assessments.Should().NotBeNull();
        assessments!.Count.Should().BeGreaterThanOrEqualTo(1);
    }

    [Fact]
    public async Task GetLatestAssessment_After_Create_Returns_Latest()
    {
        var newAssessment = new AddGalpinAssessmentResource
        {
            AssessedOnUtc = DateTime.UtcNow,
            Notes = "Latest assessment",
            Scores = new List<GalpinAssessmentScoreResource>
            {
                new() { Adaptation = GalpinAdaptationType.Power, Score = 8.0m }
            }
        };

        var createResponse = await Client.PostAsJsonAsync("/galpinassessments", newAssessment);
        createResponse.EnsureSuccessStatusCode();

        var response = await Client.GetAsync("/galpinassessments/latest");
        response.StatusCode.Should().Be(HttpStatusCode.OK);
        var latest = await response.Content.ReadFromJsonAsync<GetGalpinAssessmentResource>();
        latest.Should().NotBeNull();
        latest!.Notes.Should().Be("Latest assessment");
    }

    [Fact]
    public async Task GetAdaptationTrend_Returns_TrendPoints()
    {
        // Seed an assessment first
        var assessment = new AddGalpinAssessmentResource
        {
            AssessedOnUtc = DateTime.UtcNow.AddDays(-10),
            Scores = new List<GalpinAssessmentScoreResource>
            {
                new() { Adaptation = GalpinAdaptationType.MuscularEndurance, Score = 6.5m }
            }
        };
        var createResponse = await Client.PostAsJsonAsync("/galpinassessments", assessment);
        createResponse.EnsureSuccessStatusCode();

        var response = await Client.GetAsync("/galpinassessments/trends/MuscularEndurance");

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        var trend = await response.Content.ReadFromJsonAsync<GetGalpinAdaptationTrendResource>();
        trend.Should().NotBeNull();
        trend!.Points.Should().NotBeEmpty();
    }
}
