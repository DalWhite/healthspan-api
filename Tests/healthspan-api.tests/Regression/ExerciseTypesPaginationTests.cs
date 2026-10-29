using System.Net;
using System.Net.Http.Json;
using Controllers.ApiContracts.ExerciseTypes;
using FluentAssertions;
using Xunit;

namespace healthspan_api.tests.Regression;

/// <summary>
/// Contract of the paged projections the list screens paint with (t_81a400b3):
/// the whole catalog is 1.4MB, so the client asks for one page at a time and a
/// page must carry the total, a description-free shape for painting and the
/// full shape for the background hydration the offline mirror is built from.
/// </summary>
public class ExerciseTypesPaginationTests : ApiTestBase
{
    public ExerciseTypesPaginationTests(HealthspanApiFactory factory) : base(factory) { }

    [Fact]
    public async Task MinimalPage_ReturnsOnlyTheRequestedRows_AndTheTotalHeader()
    {
        var response = await Client.GetAsync("/exercisetypes?fields=minimal&skip=0&take=2");

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        var rows = await response.Content.ReadFromJsonAsync<List<GetExerciseTypeResource>>();
        rows.Should().NotBeNull();
        rows!.Count.Should().Be(2);

        response.Headers.TryGetValues("X-Total-Count", out var totals).Should().BeTrue();
        int.Parse(totals!.Single()).Should().BeGreaterThanOrEqualTo(rows.Count);
    }

    [Fact]
    public async Task MinimalPage_OmitsDescription_FullPage_KeepsIt()
    {
        var slim = await Client.GetFromJsonAsync<List<GetExerciseTypeResource>>(
            "/exercisetypes?fields=minimal&skip=0&take=10");
        var full = await Client.GetFromJsonAsync<List<GetExerciseTypeResource>>(
            "/exercisetypes?fields=full&skip=0&take=10");

        slim.Should().NotBeNull();
        full.Should().NotBeNull();
        slim!.Select(r => r.Id).Should().BeEquivalentTo(full!.Select(r => r.Id),
            "the two projections page over the same rows");

        slim.Should().OnlyContain(r => r.Description == null,
            "the paint page must not carry the description text");
        full.Should().Contain(r => r.Description != null,
            "hydration and the offline mirror read search-by-description from the full projection");
    }

    [Fact]
    public async Task PagedProjections_CoverEveryRowOnce()
    {
        var seen = new List<int>();
        for (var skip = 0; ; skip += 5)
        {
            var page = await Client.GetFromJsonAsync<List<GetExerciseTypeResource>>(
                $"/exercisetypes?fields=full&skip={skip}&take=5");
            page.Should().NotBeNull();
            if (page!.Count == 0)
            {
                break;
            }

            seen.AddRange(page.Select(r => r.Id));
        }

        seen.Should().OnlyHaveUniqueItems("a row must not land in two pages");
        seen.Should().Contain(TestDataSeeder.BenchPressTypeId);

        var all = await Client.GetFromJsonAsync<List<GetExerciseTypeResource>>("/exercisetypes");
        all.Should().NotBeNull();
        // The unpaged endpoint nests variations inside their parent; the flat
        // projection lists each of them as its own row, so it covers strictly
        // more ids and must cover every one the nested shape reports.
        seen.Should().Contain(all!.Select(r => r.Id));
    }
}
