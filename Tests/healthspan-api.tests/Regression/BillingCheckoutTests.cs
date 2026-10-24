using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text;
using System.Text.Json;
using FluentAssertions;
using Xunit;

namespace healthspan_api.tests.Regression;

public class BillingCheckoutTests : ApiTestBase
{
    public BillingCheckoutTests(HealthspanApiFactory factory) : base(factory) { }

    private async Task<(HttpStatusCode StatusCode, JsonElement Body)> PostCheckoutSessionAsync(
        string? plan,
        string? idempotencyKey)
    {
        using var request = new HttpRequestMessage(
            HttpMethod.Post,
            "/api/billing/checkout-session")
        {
            Content = new StringContent(
                JsonSerializer.Serialize(new { plan }),
                Encoding.UTF8,
                "application/json")
        };

        if (idempotencyKey is not null)
        {
            request.Headers.TryAddWithoutValidation("Idempotency-Key", idempotencyKey);
        }

        var response = await Client.SendAsync(request);
        var body = JsonDocument.Parse(await response.Content.ReadAsStringAsync());

        return (response.StatusCode, body.RootElement.Clone());
    }

    [Fact]
    public async Task CheckoutSession_WithKeyAndKnownPlan_ReturnsSession_AndReplayIsIdempotent()
    {
        var (status, body) = await PostCheckoutSessionAsync("premium-monthly", "idem-checkout-a001");

        status.Should().Be(HttpStatusCode.OK);
        body.GetProperty("sessionId").GetString().Should().NotBeNullOrEmpty();
        body.GetProperty("checkoutUrl").GetString().Should().StartWith("https://checkout.stripe.com/");
        body.GetProperty("plan").GetString().Should().Be("premium-monthly");

        var firstSessionId = body.GetProperty("sessionId").GetString();
        var gatewayCalls = Factory.BillingGateway.CheckoutCalls
            .Count(call => call.IdempotencyKey == "idem-checkout-a001");
        gatewayCalls.Should().Be(1);

        // Same key → same session, no second gateway call.
        var (replayStatus, replayBody) = await PostCheckoutSessionAsync("premium-monthly", "idem-checkout-a001");

        replayStatus.Should().Be(HttpStatusCode.OK);
        replayBody.GetProperty("sessionId").GetString().Should().Be(firstSessionId);

        Factory.BillingGateway.CheckoutCalls
            .Count(call => call.IdempotencyKey == "idem-checkout-a001")
            .Should().Be(1);
    }

    [Fact]
    public async Task CheckoutSession_DifferentKeys_CreateSeparateSessions()
    {
        var (firstStatus, firstBody) = await PostCheckoutSessionAsync("premium-annual", "idem-checkout-b001");
        var (secondStatus, secondBody) = await PostCheckoutSessionAsync("premium-annual", "idem-checkout-b002");

        firstStatus.Should().Be(HttpStatusCode.OK);
        secondStatus.Should().Be(HttpStatusCode.OK);
        secondBody.GetProperty("sessionId").GetString()
            .Should().NotBe(firstBody.GetProperty("sessionId").GetString());
    }

    [Fact]
    public async Task CheckoutSession_WithoutIdempotencyKey_Returns400()
    {
        var (status, body) = await PostCheckoutSessionAsync("premium-monthly", idempotencyKey: null);

        status.Should().Be(HttpStatusCode.BadRequest);
        body.GetProperty("code").GetString().Should().Be("idempotency_key_required");
    }

    [Fact]
    public async Task CheckoutSession_UnknownPlan_Returns404()
    {
        var (status, body) = await PostCheckoutSessionAsync("premium-lifetime", "idem-checkout-c001");

        status.Should().Be(HttpStatusCode.NotFound);
        body.GetProperty("code").GetString().Should().Be("plan_not_found");
    }
}

public class BillingSubscriptionWebhookTests : ApiTestBase
{
    public BillingSubscriptionWebhookTests(HealthspanApiFactory factory) : base(factory) { }

    private async Task<(HttpStatusCode StatusCode, JsonElement Body)> PostWebhookAsync(
        string payload,
        string? signature)
    {
        using var request = new HttpRequestMessage(HttpMethod.Post, "/api/billing/webhook/stripe")
        {
            Content = new StringContent(payload, Encoding.UTF8, "application/json")
        };

        if (signature is not null)
        {
            request.Headers.TryAddWithoutValidation("Stripe-Signature", signature);
        }

        var response = await Client.SendAsync(request);
        var body = JsonDocument.Parse(await response.Content.ReadAsStringAsync());

        return (response.StatusCode, body.RootElement.Clone());
    }

    private static string CheckoutCompletedPayload(string eventId, string subscriptionId, string customerId)
        => JsonSerializer.Serialize(new
        {
            id = eventId,
            type = "checkout.session.completed",
            data = new
            {
                @object = new
                {
                    id = "cs_test_flow",
                    subscription = subscriptionId,
                    customer = customerId,
                    metadata = new
                    {
                        userId = HealthspanApiFactory.TestUserId,
                        planSlug = "premium-monthly",
                    },
                },
            },
        });

    private static string SubscriptionUpdatedPayload(
        string eventId,
        string subscriptionId,
        string status,
        bool cancelAtPeriodEnd)
        => JsonSerializer.Serialize(new
        {
            id = eventId,
            type = "customer.subscription.updated",
            data = new
            {
                @object = new
                {
                    id = subscriptionId,
                    customer = "cus_flow_1",
                    status,
                    current_period_end = DateTimeOffset.UtcNow.ToUnixTimeSeconds() + 30 * 24 * 3600,
                    cancel_at_period_end = cancelAtPeriodEnd,
                },
            },
        });

    [Fact]
    public async Task Webhook_FullFlow_CheckoutThenActiveThenReplayThenCancel()
    {
        // 1. No subscription yet → 200 status "none" (QA-B6: free is a normal
        // state, not 404 — spec §30 TrialBanner/EmptyState).
        var initialGet = await Client.GetAsync("/api/billing/subscription");
        initialGet.StatusCode.Should().Be(HttpStatusCode.OK);
        var initialBody = JsonDocument.Parse(await initialGet.Content.ReadAsStringAsync()).RootElement;
        initialBody.GetProperty("status").GetString().Should().Be("none");

        // 2. checkout.session.completed creates the local mirror.
        var (checkoutStatus, checkoutBody) = await PostWebhookAsync(
            CheckoutCompletedPayload("evt_flow_checkout_1", "sub_flow_1", "cus_flow_1"),
            FakeBillingGateway.Sign(CheckoutCompletedPayload("evt_flow_checkout_1", "sub_flow_1", "cus_flow_1")));

        checkoutStatus.Should().Be(HttpStatusCode.OK);
        checkoutBody.GetProperty("outcome").GetString().Should().Be("processed");

        // 3. customer.subscription.updated activates it.
        var updatedPayload = SubscriptionUpdatedPayload("evt_flow_sub_1", "sub_flow_1", "active", cancelAtPeriodEnd: false);
        var (updatedStatus, updatedBody) = await PostWebhookAsync(updatedPayload, FakeBillingGateway.Sign(updatedPayload));

        updatedStatus.Should().Be(HttpStatusCode.OK);
        updatedBody.GetProperty("outcome").GetString().Should().Be("processed");

        // 4. GET subscription reflects the state.
        var getResponse = await Client.GetAsync("/api/billing/subscription");
        getResponse.StatusCode.Should().Be(HttpStatusCode.OK);

        var subscription = JsonDocument.Parse(await getResponse.Content.ReadAsStringAsync()).RootElement;
        subscription.GetProperty("status").GetString().Should().Be("active");
        subscription.GetProperty("plan").GetString().Should().Be("premium-monthly");
        subscription.GetProperty("cancelAtPeriodEnd").GetBoolean().Should().BeFalse();
        subscription.GetProperty("currentPeriodEnd").GetDateTime().Should().BeAfter(DateTime.UtcNow);

        // 5. Stripe retry of the same event id is deduped.
        var (replayStatus, replayBody) = await PostWebhookAsync(updatedPayload, FakeBillingGateway.Sign(updatedPayload));

        replayStatus.Should().Be(HttpStatusCode.OK);
        replayBody.GetProperty("outcome").GetString().Should().Be("duplicate");

        // 6. Cancel at period end.
        var cancelResponse = await Client.PostAsync("/api/billing/subscription/cancel", null);
        cancelResponse.StatusCode.Should().Be(HttpStatusCode.OK);

        var canceled = JsonDocument.Parse(await cancelResponse.Content.ReadAsStringAsync()).RootElement;
        canceled.GetProperty("cancelAtPeriodEnd").GetBoolean().Should().BeTrue();
        canceled.GetProperty("status").GetString().Should().Be("active");

        // 7. Receipts come from the gateway for the subscription's customer.
        var receiptsResponse = await Client.GetAsync("/api/billing/receipts");
        receiptsResponse.StatusCode.Should().Be(HttpStatusCode.OK);

        var receipts = JsonDocument.Parse(await receiptsResponse.Content.ReadAsStringAsync()).RootElement;
        receipts.GetArrayLength().Should().Be(2);
        receipts[0].GetProperty("amountCents").GetInt32().Should().Be(999);
        receipts[0].GetProperty("receiptUrl").GetString().Should().StartWith("https://pay.stripe.com/");
    }

    [Fact]
    public async Task Webhook_InvalidSignature_Returns400()
    {
        var payload = CheckoutCompletedPayload("evt_flow_bad_sig_1", "sub_bad_1", "cus_bad_1");

        var (status, body) = await PostWebhookAsync(payload, "t=1,v1=deadbeefdeadbeefdeadbeefdeadbeef");

        status.Should().Be(HttpStatusCode.BadRequest);
        body.GetProperty("code").GetString().Should().Be("invalid_webhook_signature");
    }

    [Fact]
    public async Task Webhook_MissingSignature_Returns400()
    {
        var payload = CheckoutCompletedPayload("evt_flow_no_sig_1", "sub_nosig_1", "cus_nosig_1");

        var (status, body) = await PostWebhookAsync(payload, signature: null);

        status.Should().Be(HttpStatusCode.BadRequest);
        body.GetProperty("code").GetString().Should().Be("invalid_webhook_signature");
    }
}

/// <summary>
/// QA-B1 regression (t_c95d1623): a real Stripe webhook sends v1 as lowercase hex.
/// Before the fix the endpoint answered 400 invalid_webhook_signature for every genuine
/// Stripe delivery, so Premium would never activate in production.
/// Own fixture (own database) so the processed event cannot pollute the full-flow fixture.
/// </summary>
public class BillingWebhookSignatureTests : ApiTestBase
{
    public BillingWebhookSignatureTests(HealthspanApiFactory factory) : base(factory) { }

    [Fact]
    public async Task Webhook_StripeStyleLowercaseHexSignature_IsAccepted()
    {
        var payload = JsonSerializer.Serialize(new
        {
            id = "evt_b1_lower_1",
            type = "checkout.session.completed",
            data = new
            {
                @object = new
                {
                    id = "cs_b1_lower_1",
                    subscription = "sub_b1_lower_1",
                    customer = "cus_b1_lower_1",
                    metadata = new
                    {
                        userId = HealthspanApiFactory.TestUserId,
                        planSlug = "premium-monthly",
                    },
                },
            },
        });

        var signature = FakeBillingGateway.Sign(payload);
        signature.Should().MatchRegex(@"^t=\d+,v1=[0-9a-f]{64}$", "the fake gateway signs the way Stripe signs");

        using var request = new HttpRequestMessage(HttpMethod.Post, "/api/billing/webhook/stripe")
        {
            Content = new StringContent(payload, Encoding.UTF8, "application/json"),
        };
        request.Headers.TryAddWithoutValidation("Stripe-Signature", signature);

        var response = await Client.SendAsync(request);
        var body = JsonDocument.Parse(await response.Content.ReadAsStringAsync());

        response.StatusCode.Should().Be(HttpStatusCode.OK,
            $"a real Stripe webhook (lowercase v1 hex) must not be rejected (got: {body})");
        body.RootElement.GetProperty("outcome").GetString().Should().Be("processed");
    }
}

/// <summary>
/// Separate fixture (own database) so "no subscription yet" assertions cannot be
/// polluted by the full-flow test's ordering.
/// </summary>
public class BillingNoSubscriptionTests : ApiTestBase
{
    public BillingNoSubscriptionTests(HealthspanApiFactory factory) : base(factory) { }

    [Fact]
    public async Task GetSubscription_WithoutSubscription_Returns200None()
    {
        // QA-B6 regression: a free account is a normal state — 200 status "none",
        // not 404. The FE TrialBanner/EmptyState build the screen from it.
        var response = await Client.GetAsync("/api/billing/subscription");

        response.StatusCode.Should().Be(HttpStatusCode.OK);

        var body = JsonDocument.Parse(await response.Content.ReadAsStringAsync()).RootElement;
        body.GetProperty("status").GetString().Should().Be("none");
    }

    [Fact]
    public async Task Cancel_WithoutSubscription_Returns404()
    {
        var response = await Client.PostAsync("/api/billing/subscription/cancel", null);

        response.StatusCode.Should().Be(HttpStatusCode.NotFound);

        var body = JsonDocument.Parse(await response.Content.ReadAsStringAsync()).RootElement;
        body.GetProperty("code").GetString().Should().Be("subscription_not_found");
    }

    [Fact]
    public async Task Receipts_WithoutSubscription_ReturnEmptyList()
    {
        var response = await Client.GetAsync("/api/billing/receipts");

        response.StatusCode.Should().Be(HttpStatusCode.OK);

        var body = JsonDocument.Parse(await response.Content.ReadAsStringAsync()).RootElement;
        body.GetArrayLength().Should().Be(0);
    }
}
