using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using FluentAssertions;
using Xunit;

namespace healthspan_api.tests.Regression;

public class TrainerPaymentLinkTests : ApiTestBase
{
    public TrainerPaymentLinkTests(HealthspanApiFactory factory) : base(factory) { }

    private async Task<(HttpStatusCode StatusCode, JsonElement Body)> PutPaymentLinkAsync(string url)
    {
        var response = await Client.PutAsJsonAsync("/api/trainers/me/payment-link", new { url });
        var text = await response.Content.ReadAsStringAsync();
        var body = JsonDocument.Parse(string.IsNullOrWhiteSpace(text) ? "{}" : text);

        return (response.StatusCode, body.RootElement.Clone());
    }

    [Fact]
    public async Task PutPaymentLink_LookalikeDomain_Returns400()
    {
        var (status, body) = await PutPaymentLinkAsync("https://checkout.stripe.com.evil.test/pay");

        status.Should().Be(HttpStatusCode.BadRequest);
        body.GetProperty("code").GetString().Should().Be("payment_link_not_allowed");
    }

    [Fact]
    public async Task PutPaymentLink_NonHttps_Returns400()
    {
        var (status, body) = await PutPaymentLinkAsync("http://paypal.me/trainer123");

        status.Should().Be(HttpStatusCode.BadRequest);
        body.GetProperty("code").GetString().Should().Be("payment_link_not_allowed");
    }

    [Fact]
    public async Task PutPaymentLink_CredentialsInUrl_Returns400()
    {
        var (status, body) = await PutPaymentLinkAsync("https://checkout.stripe.com@evil.test/pay");

        status.Should().Be(HttpStatusCode.BadRequest);
        body.GetProperty("code").GetString().Should().Be("payment_link_not_allowed");
    }

    [Fact]
    public async Task PaymentLinkLifecycle_Publish_Read_PublicRead_Delete()
    {
        var (putStatus, putBody) = await PutPaymentLinkAsync("https://checkout.stripe.com/b/pay/pl_test_123");

        putStatus.Should().Be(HttpStatusCode.OK);
        putBody.GetProperty("verifiedDomain").GetString().Should().Be("checkout.stripe.com");

        var getMe = await Client.GetAsync("/api/trainers/me/payment-link");
        getMe.StatusCode.Should().Be(HttpStatusCode.OK);

        var mine = JsonDocument.Parse(await getMe.Content.ReadAsStringAsync()).RootElement;
        mine.GetProperty("url").GetString().Should().Be("https://checkout.stripe.com/b/pay/pl_test_123");

        // Public read: trainees can see the published link, and nothing else.
        var publicGet = await Client.GetAsync($"/api/trainers/{HealthspanApiFactory.TestUserId}/payment-link");
        publicGet.StatusCode.Should().Be(HttpStatusCode.OK);

        var published = JsonDocument.Parse(await publicGet.Content.ReadAsStringAsync()).RootElement;
        published.GetProperty("url").GetString().Should().Be("https://checkout.stripe.com/b/pay/pl_test_123");
        published.TryGetProperty("email", out _).Should().BeFalse();

        var deleteResponse = await Client.DeleteAsync("/api/trainers/me/payment-link");
        deleteResponse.StatusCode.Should().Be(HttpStatusCode.NoContent);

        var afterDelete = await Client.GetAsync("/api/trainers/me/payment-link");
        afterDelete.StatusCode.Should().Be(HttpStatusCode.NotFound);
    }

    [Fact]
    public async Task PutPaymentLink_PaypalMe_IsAllowed()
    {
        var (status, body) = await PutPaymentLinkAsync("https://paypal.me/entrenadorhs");

        status.Should().Be(HttpStatusCode.OK);
        body.GetProperty("verifiedDomain").GetString().Should().Be("paypal.me");
    }

    [Fact]
    public async Task PutPaymentLink_EmptyUrl_RemovesPublishedLink()
    {
        // QA-B7 regression (spec B3): PUT with an empty url removes the link
        // (204), same effect as DELETE — the FE settings form uses this path.
        var (publishStatus, _) = await PutPaymentLinkAsync("https://paypal.me/quitarlink");
        publishStatus.Should().Be(HttpStatusCode.OK);

        var (removeStatus, _) = await PutPaymentLinkAsync("");
        removeStatus.Should().Be(HttpStatusCode.NoContent);

        var afterRemove = await Client.GetAsync("/api/trainers/me/payment-link");
        afterRemove.StatusCode.Should().Be(HttpStatusCode.NotFound);

        // Removing when there is nothing to remove mirrors DELETE semantics.
        var (againStatus, againBody) = await PutPaymentLinkAsync("");
        againStatus.Should().Be(HttpStatusCode.NotFound);
        againBody.GetProperty("code").GetString().Should().Be("payment_link_not_found");
    }
}
