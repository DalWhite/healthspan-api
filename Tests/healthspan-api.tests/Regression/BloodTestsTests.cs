using System.Net;
using System.Net.Http.Json;
using FluentAssertions;
using Xunit;

namespace healthspan_api.tests.Regression;

public class BloodTestsTests : ApiTestBase
{
    public BloodTestsTests(HealthspanApiFactory factory) : base(factory) { }

    [Fact]
    public async Task GetBloodTests_Returns_SeededTests()
    {
        var response = await Client.GetAsync("/bloodtests");

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        var bloodTests = await response.Content.ReadFromJsonAsync<List<GetBloodTestResource>>();
        bloodTests.Should().NotBeNull();
        bloodTests!.Count.Should().BeGreaterThanOrEqualTo(1);
    }

    [Fact]
    public async Task GetBloodTest_ById_Returns_WithMarkers()
    {
        // Create a fresh blood test to avoid interference from review tests
        var newTest = new AddBloodTestResource
        {
            TestDate = DateTime.UtcNow.AddDays(-60),
            LabName = "Marker Check Lab",
            Markers = new List<BloodTestMarkerResource>
            {
                new() { Name = "HDL", Value = 55, Unit = "mg/dL", Status = MarkerStatus.InRange },
                new() { Name = "LDL", Value = 130, Unit = "mg/dL", Status = MarkerStatus.High }
            }
        };

        var createResponse = await Client.PostAsJsonAsync("/bloodtests/manual", newTest);
        createResponse.StatusCode.Should().Be(HttpStatusCode.Created);
        var createdJson = await createResponse.Content.ReadAsStringAsync();
        var createdId = System.Text.Json.JsonDocument.Parse(createdJson).RootElement.GetProperty("id").GetGuid();

        var response = await Client.GetAsync($"/bloodtests/{createdId}");
        response.StatusCode.Should().Be(HttpStatusCode.OK);
        var bloodTest = await response.Content.ReadFromJsonAsync<GetBloodTestResource>();
        bloodTest.Should().NotBeNull();
        bloodTest!.Id.Should().Be(createdId);
        bloodTest.Markers.Should().HaveCountGreaterThanOrEqualTo(2);
        bloodTest.Markers.Should().Contain(m => m.Name == "HDL");
        bloodTest.Markers.Should().Contain(m => m.Name == "LDL");
    }

    [Fact]
    public async Task GetLatestMarkers_Returns_MarkerSnapshots()
    {
        var response = await Client.GetAsync("/bloodtests/markers/latest");

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        var markers = await response.Content.ReadFromJsonAsync<List<LatestMarkerSnapshotResource>>();
        markers.Should().NotBeNull();
        markers!.Count.Should().BeGreaterThanOrEqualTo(1);
    }

    [Fact]
    public async Task GetMarkerTrends_Returns_TrendData()
    {
        var response = await Client.GetAsync("/bloodtests/markers/glucose/trends");

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        var trend = await response.Content.ReadFromJsonAsync<GetMarkerTrendResource>();
        trend.Should().NotBeNull();
        trend!.MetricKey.Should().Be("glucose");
    }

    [Fact]
    public async Task AddManualBloodTest_Returns_Created()
    {
        var newTest = new AddBloodTestResource
        {
            TestDate = DateTime.UtcNow.AddDays(-5),
            LabName = "Manual Lab",
            Markers = new List<BloodTestMarkerResource>
            {
                new()
                {
                    Name = "Hemoglobin",
                    Value = 14.5m,
                    Unit = "g/dL",
                    Status = MarkerStatus.InRange,
                    ReferenceMinValue = 12,
                    ReferenceMaxValue = 17
                }
            }
        };

        var response = await Client.PostAsJsonAsync("/bloodtests/manual", newTest);

        response.StatusCode.Should().Be(HttpStatusCode.Created);
    }

    [Fact]
    public async Task ReviewBloodTest_Returns_Ok()
    {
        var review = new ReviewBloodTestResource
        {
            TestDate = DateTime.UtcNow.AddDays(-30),
            LabName = "Test Lab",
            Markers = new List<BloodTestMarkerResource>
            {
                new()
                {
                    Name = "Glucose",
                    Value = 92,
                    Unit = "mg/dL",
                    Status = MarkerStatus.InRange,
                    ReferenceMinValue = 70,
                    ReferenceMaxValue = 110
                }
            }
        };

        var response = await Client.PostAsJsonAsync($"/bloodtests/{TestDataSeeder.BloodTestId1}/review", review);

        response.StatusCode.Should().BeOneOf(HttpStatusCode.OK, HttpStatusCode.NoContent);
    }

    [Fact]
    public async Task GetBloodTest_NonExistent_Returns_NotFound()
    {
        var response = await Client.GetAsync($"/bloodtests/{Guid.NewGuid()}");

        response.StatusCode.Should().Be(HttpStatusCode.NotFound);
    }

    [Fact]
    public async Task GetPendingReviewBloodTests_Returns_Ok()
    {
        var response = await Client.GetAsync("/bloodtests/pending-review");

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        var bloodTests = await response.Content.ReadFromJsonAsync<List<GetBloodTestResource>>();
        bloodTests.Should().NotBeNull();
    }

    [Fact]
    public async Task UploadBloodTest_WithoutFile_Returns_BadRequest()
    {
        var content = new MultipartFormDataContent();
        content.Add(new StringContent("2025-06-01"), "testDate");

        var response = await Client.PostAsync("/bloodtests/upload", content);

        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
    }

    [Fact]
    public async Task UploadBloodTest_OversizedFile_Returns_BadRequest()
    {
        var content = new MultipartFormDataContent();
        // Create content exceeding 20MB limit
        var oversized = new byte[21 * 1024 * 1024];
        var fileContent = new ByteArrayContent(oversized);
        fileContent.Headers.ContentType = new System.Net.Http.Headers.MediaTypeHeaderValue("application/pdf");
        content.Add(fileContent, "file", "huge.pdf");
        content.Add(new StringContent("2025-06-01"), "testDate");

        var response = await Client.PostAsync("/bloodtests/upload", content);

        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
    }

    [Fact]
    public async Task UploadBloodTest_InvalidFormat_Returns_BadRequest()
    {
        var content = new MultipartFormDataContent();
        var fileContent = new ByteArrayContent(new byte[] { 1, 2, 3 });
        fileContent.Headers.ContentType = new System.Net.Http.Headers.MediaTypeHeaderValue("text/plain");
        content.Add(fileContent, "file", "data.txt");
        content.Add(new StringContent("2025-06-01"), "testDate");

        var response = await Client.PostAsync("/bloodtests/upload", content);

        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
    }

    [Fact]
    public async Task AddManualBloodTest_WithMultipleMarkers_Returns_Created()
    {
        var newTest = new AddBloodTestResource
        {
            TestDate = DateTime.UtcNow.AddDays(-10),
            LabName = "Multi Marker Lab",
            Markers = new List<BloodTestMarkerResource>
            {
                new() { Name = "Iron", Value = 80, Unit = "µg/dL", Status = MarkerStatus.InRange, ReferenceMinValue = 60, ReferenceMaxValue = 170 },
                new() { Name = "Ferritin", Value = 45, Unit = "ng/mL", Status = MarkerStatus.InRange, ReferenceMinValue = 20, ReferenceMaxValue = 300 },
                new() { Name = "Vitamin D", Value = 18, Unit = "ng/mL", Status = MarkerStatus.Low, ReferenceMinValue = 30, ReferenceMaxValue = 100 }
            }
        };

        var response = await Client.PostAsJsonAsync("/bloodtests/manual", newTest);

        response.StatusCode.Should().Be(HttpStatusCode.Created);
    }

    [Fact]
    public async Task AddManualBloodTest_WithEmptyMarkers_Returns_Created()
    {
        var newTest = new AddBloodTestResource
        {
            TestDate = DateTime.UtcNow.AddDays(-2),
            LabName = "Empty Markers Lab",
            Markers = new List<BloodTestMarkerResource>()
        };

        var response = await Client.PostAsJsonAsync("/bloodtests/manual", newTest);

        response.StatusCode.Should().Be(HttpStatusCode.Created);
    }
}
