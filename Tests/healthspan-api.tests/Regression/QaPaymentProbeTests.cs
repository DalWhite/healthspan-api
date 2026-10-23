#nullable enable

// QA probe suite — kanban t_183251da (QA de pago: billing backend + payment link + UX spec t_5b46e650).
//
// These tests encode the EXPECTED behaviour (Stripe protocol + the product decisions in
// pago-ux-spec-t_5b46e650.md). A failing probe is the repro of a defect; flip the assertion
// once the fix lands. This file lives only on branch qa/t_183251da — it is not meant to be
// merged to main until the defects it documents are fixed.

using System.Net;
using System.Net.Http.Json;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using FluentAssertions;
using healthspan_api.Options;
using healthspan_api.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using Services;
using Xunit;

namespace healthspan_api.tests;

/// <summary>
/// Pure protocol/unit probes: no HTTP pipeline involved.
/// </summary>
public class QaStripeProtocolProbeTests
{
    private const string Secret = FakeBillingGateway.WebhookSecret;

    private static string SignLowercase(string payload, long? timestamp = null)
    {
        var ts = timestamp ?? DateTimeOffset.UtcNow.ToUnixTimeSeconds();
        using var hmac = new HMACSHA256(Encoding.UTF8.GetBytes(Secret));
        var hex = Convert.ToHexString(hmac.ComputeHash(Encoding.UTF8.GetBytes($"{ts}.{payload}"))).ToLowerInvariant();
        return $"t={ts},v1={hex}";
    }

    // QA-B1 (critical). Stripe signs the v1 signature with LOWERCASE hex. The verifier compares
    // its uppercase Convert.ToHexString() against the candidate byte-by-byte, so every real
    // Stripe webhook is rejected with 400 while the test helper (uppercase) passes.
    [Fact]
    public void QaB1_StripeWebhookVerifier_AcceptsStripeStyleLowercaseHexSignature()
    {
        const string payload = "{\"id\":\"evt_lower\",\"type\":\"checkout.session.completed\",\"data\":{\"object\":{}}}";
        var header = SignLowercase(payload);

        var act = () => StripeWebhookVerifier.Verify(payload, header, Secret);

        act.Should().NotThrow(
            "Stripe sends v1 signatures as lowercase hex; a genuine Stripe webhook must verify");
    }

    // QA-B9 (contract). Product decision D2 in the UX spec: "Trial de 7 días sin tarjeta".
    // The gateway hardcodes 14 days and never sends payment_method_collection=if_required,
    // so Checkout collects a card upfront — the opposite of the agreed trial.
    [Fact]
    public async Task QaB9_CheckoutSessionRequest_UsesSevenDayTrialWithoutCardPerProductDecision()
    {
        var handler = new CapturingHandler();
        using var http = new HttpClient(handler);
        var gateway = new StripeBillingGateway(http, Microsoft.Extensions.Options.Options.Create(new StripeSettings
        {
            SecretKey = "sk_test_probe",
            WebhookSigningSecret = "whsec_probe",
            PriceIds = { ["premium-monthly"] = "price_probe" },
        }));

        await gateway.CreateCheckoutSessionAsync("user-1", "premium-monthly", "price_probe", "idem-1");

        var form = handler.Body!;
        form.Should().Contain("trial_period_days")
            .And.Contain("trial_period_days]=7", "UX decision D2 is a 7-day trial, not 14");
        form.Should().Contain("payment_method_collection=if_required",
            "UX decision D2 is a trial WITHOUT card; Stripe requires this flag to skip collecting one");
    }

    // Informational (not a defect): the allowlist accepts any subdomain of an allowed host.
    // Characterisation test — documents the current behaviour for the record.
    [Fact]
    public void QaB8_PaymentLinkValidator_CurrentlyAcceptsSubdomainsOfAllowedHosts()
    {
        TrainerPaymentLinkValidator.Validate("https://evil.paypal.com/checkout")
            .Should().Be("paypal.com", "documented current behaviour: host.EndsWith(\".\" + allowed)");
        TrainerPaymentLinkValidator.Validate("https://paypal.com.evil.tld/").Should().BeNull();
        TrainerPaymentLinkValidator.Validate("http://paypal.me/x").Should().BeNull();
    }

    private sealed class CapturingHandler : HttpMessageHandler
    {
        public string? Body { get; private set; }

        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            Body = request.Content is null ? null : await request.Content.ReadAsStringAsync(cancellationToken);
            return new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent("{\"id\":\"cs_probe\",\"url\":\"https://checkout.stripe.com/c/pay/cs_probe\"}"),
            };
        }
    }
}

/// <summary>
/// Webhook-path probes (real verifier + fake gateway through the full API pipeline).
/// </summary>
public class QaBillingWebhookProbeTests : ApiTestBase
{
    public QaBillingWebhookProbeTests(HealthspanApiFactory factory) : base(factory) { }

    private static (string Payload, string Signature) StripeEvent(string eventId, string type, object dataObject, bool lowercase, long? timestamp = null)
    {
        var payload = JsonSerializer.Serialize(new { id = eventId, type, data = new { @object = dataObject } });
        var ts = timestamp ?? DateTimeOffset.UtcNow.ToUnixTimeSeconds();
        using var hmac = new HMACSHA256(Encoding.UTF8.GetBytes(FakeBillingGateway.WebhookSecret));
        var hex = Convert.ToHexString(hmac.ComputeHash(Encoding.UTF8.GetBytes($"{ts}.{payload}")));
        if (lowercase)
        {
            hex = hex.ToLowerInvariant();
        }
        return (payload, $"t={ts},v1={hex}");
    }

    private async Task<string> PostWebhookAsync(string eventId, string type, object dataObject, bool lowercase = false)
    {
        var (payload, signature) = StripeEvent(eventId, type, dataObject, lowercase);
        using var request = new HttpRequestMessage(HttpMethod.Post, "/api/billing/webhook/stripe")
        {
            Content = new StringContent(payload, Encoding.UTF8, "application/json"),
        };
        request.Headers.TryAddWithoutValidation("Stripe-Signature", signature);

        var response = await Client.SendAsync(request);
        return await response.Content.ReadAsStringAsync();
    }

    private async Task PostCheckoutAsync(string plan, string idempotencyKey)
    {
        using var request = new HttpRequestMessage(HttpMethod.Post, "/api/billing/checkout-session")
        {
            Content = new StringContent(JsonSerializer.Serialize(new { plan }), Encoding.UTF8, "application/json"),
        };
        request.Headers.TryAddWithoutValidation("Idempotency-Key", idempotencyKey);
        var response = await Client.SendAsync(request);
        response.EnsureSuccessStatusCode();
    }

    private async Task ActivateSubscriptionAsync(string eventIdPrefix, string subscriptionId, string planSlug)
    {
        await PostWebhookAsync($"{eventIdPrefix}_completed", "checkout.session.completed", new
        {
            id = $"cs_{subscriptionId}",
            subscription = subscriptionId,
            customer = $"cus_{subscriptionId}",
            metadata = new { userId = TestDataSeeder.TestUserId, planSlug },
        });

        await PostWebhookAsync($"{eventIdPrefix}_updated", "customer.subscription.updated", new
        {
            id = subscriptionId,
            customer = $"cus_{subscriptionId}",
            status = "active",
            current_period_end = DateTimeOffset.UtcNow.AddDays(30).ToUnixTimeSeconds(),
            cancel_at_period_end = false,
        });
    }

    // QA-B1 (critical) — same bug as the unit probe, seen through the HTTP surface.
    [Fact]
    public async Task QaB1_StripeWebhook_StripeStyleLowercaseSignature_IsAccepted()
    {
        var body = await PostWebhookAsync("evt_qa_b1", "checkout.session.completed", new
        {
            id = "cs_qa_b1",
            subscription = "sub_qa_b1",
            customer = "cus_qa_b1",
            metadata = new { userId = TestDataSeeder.TestUserId, planSlug = "premium-monthly" },
        }, lowercase: true);

        body.Should().NotContain("invalid_webhook_signature",
            $"a real Stripe webhook (lowercase v1 hex) must not be rejected (got: {body})");
    }

    // QA-B4. Stripe sends charge.refunded; the handler ignores it, so a refunded user keeps
    // Premium access and the local mirror still says "active".
    [Fact]
    public async Task QaB4_ChargeRefunded_DoesNotRevokeOrFlagTheMirroredSubscription()
    {
        await PostCheckoutAsync("premium-monthly", "qa-b4-key");
        await ActivateSubscriptionAsync("qa_b4", "sub_qa_b4", "premium-monthly");

        var outcome = await PostWebhookAsync("qa_b4_refund", "charge.refunded", new
        {
            id = "ch_qa_b4",
            customer = "cus_qa_b4",
            amount = 999,
            amount_refunded = 999,
        });

        outcome.Should().Contain("\"outcome\":\"processed\"",
            "a refund must change the mirrored subscription (cancel or flag it), not be ignored");

        using var scope = Factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<MainDbContext>();
        var subscription = await db.Subscriptions.IgnoreQueryFilters()
            .FirstOrDefaultAsync(s => s.UserId == TestDataSeeder.TestUserId);

        subscription!.Status.Should().NotBe(SubscriptionStatus.Active,
            "the charge was refunded in full; access must not stay active");
    }

    // QA-B5. invoice.payment_failed is ignored too: a failed renewal never reaches the app,
    // so the user keeps Premium and never sees the "pago fallado" state the UX spec defines.
    [Fact]
    public async Task QaB5_InvoicePaymentFailed_LeavesSubscriptionActive()
    {
        await PostCheckoutAsync("premium-monthly", "qa-b5-key");
        await ActivateSubscriptionAsync("qa_b5", "sub_qa_b5", "premium-monthly");

        var outcome = await PostWebhookAsync("qa_b5_failed", "invoice.payment_failed", new
        {
            id = "in_qa_b5",
            customer = "cus_qa_b5",
            subscription = "sub_qa_b5",
            attempt_count = 2,
        });

        outcome.Should().Contain("\"outcome\":\"processed\"",
            "a failed renewal must move the subscription to past_due (UX state 'failed')");

        using var scope = Factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<MainDbContext>();
        var subscription = await db.Subscriptions.IgnoreQueryFilters()
            .FirstOrDefaultAsync(s => s.UserId == TestDataSeeder.TestUserId);

        subscription!.Status.Should().Be(SubscriptionStatus.PastDue);
    }
}

/// <summary>
/// Checkout + subscription contract probes.
/// </summary>
public class QaBillingCheckoutProbeTests : ApiTestBase
{
    public QaBillingCheckoutProbeTests(HealthspanApiFactory factory) : base(factory) { }

    private async Task<HttpResponseMessage> PostCheckoutAsync(string plan, string idempotencyKey)
    {
        using var request = new HttpRequestMessage(HttpMethod.Post, "/api/billing/checkout-session")
        {
            Content = new StringContent(JsonSerializer.Serialize(new { plan }), Encoding.UTF8, "application/json"),
        };
        request.Headers.TryAddWithoutValidation("Idempotency-Key", idempotencyKey);
        return await Client.SendAsync(request);
    }

    private static (string Payload, string Signature) StripeEvent(string eventId, string type, object dataObject)
    {
        var payload = JsonSerializer.Serialize(new { id = eventId, type, data = new { @object = dataObject } });
        var ts = DateTimeOffset.UtcNow.ToUnixTimeSeconds();
        using var hmac = new HMACSHA256(Encoding.UTF8.GetBytes(FakeBillingGateway.WebhookSecret));
        var hex = Convert.ToHexString(hmac.ComputeHash(Encoding.UTF8.GetBytes($"{ts}.{payload}")));
        return (payload, $"t={ts},v1={hex}");
    }

    private async Task PostWebhookAsync(string eventId, string type, object dataObject)
    {
        var (payload, signature) = StripeEvent(eventId, type, dataObject);
        using var request = new HttpRequestMessage(HttpMethod.Post, "/api/billing/webhook/stripe")
        {
            Content = new StringContent(payload, Encoding.UTF8, "application/json"),
        };
        request.Headers.TryAddWithoutValidation("Stripe-Signature", signature);
        var response = await Client.SendAsync(request);
        response.EnsureSuccessStatusCode();
    }

    // QA-B2. Replaying an Idempotency-Key with a DIFFERENT plan silently returns the first
    // plan's session: the user asks for annual, gets charged monthly, and the API says 200.
    [Fact]
    public async Task QaB2_SameIdempotencyKeyWithDifferentPlan_IsRejected()
    {
        var first = await PostCheckoutAsync("premium-monthly", "qa-b2-key");
        first.EnsureSuccessStatusCode();

        var replay = await PostCheckoutAsync("premium-annual", "qa-b2-key");
        var body = await replay.Content.ReadAsStringAsync();

        replay.StatusCode.Should().Be(HttpStatusCode.UnprocessableEntity,
            $"replaying an idempotency key with a different plan must not return the original plan (got {body})");
    }

    // QA-B3. A second checkout while a subscription is active repoints the single local row at
    // the new Stripe subscription and never cancels the old one → two active Stripe
    // subscriptions, only one visible to the app (double billing, uncancellable).
    [Fact]
    public async Task QaB3_SecondCheckoutWhileActive_DoesNotOrphanThePreviousStripeSubscription()
    {
        await PostCheckoutAsync("premium-monthly", "qa-b3-a");
        await PostWebhookAsync("qa_b3_a_completed", "checkout.session.completed", new
        {
            id = "cs_sub_qa_b3_a",
            subscription = "sub_qa_b3_a",
            customer = "cus_qa_b3_a",
            metadata = new { userId = TestDataSeeder.TestUserId, planSlug = "premium-monthly" },
        });
        await PostWebhookAsync("qa_b3_a_updated", "customer.subscription.updated", new
        {
            id = "sub_qa_b3_a",
            status = "active",
            current_period_end = DateTimeOffset.UtcNow.AddDays(30).ToUnixTimeSeconds(),
        });

        await PostCheckoutAsync("premium-annual", "qa-b3-b");
        await PostWebhookAsync("qa_b3_b_completed", "checkout.session.completed", new
        {
            id = "cs_sub_qa_b3_b",
            subscription = "sub_qa_b3_b",
            customer = "cus_qa_b3_b",
            metadata = new { userId = TestDataSeeder.TestUserId, planSlug = "premium-annual" },
        });

        var orphan = await Factory.BillingGateway.GetSubscriptionAsync("sub_qa_b3_a");
        orphan.Should().NotBeNull();
        orphan!.CancelAtPeriodEnd.Should().BeTrue(
            "the previous Stripe subscription must be cancelled (or reused) when the app starts a new one");
    }
}

/// <summary>
/// API-contract probes against pago-ux-spec-t_5b46e650.md.
/// </summary>
public class QaBillingContractProbeTests : ApiTestBase
{
    public QaBillingContractProbeTests(HealthspanApiFactory factory) : base(factory) { }

    // QA-B7. Spec §118: "quitar = PUT url vacía". The API rejects an empty url with 400
    // payment_link_not_allowed, so the FE flow documented in the spec cannot remove a link.
    [Fact]
    public async Task QaB7_PaymentLink_EmptyUrlRemovesTheLinkPerSpec()
    {
        var publish = await Client.PutAsJsonAsync("/api/trainers/me/payment-link", new { url = "https://buy.stripe.com/qa_probe_link" });
        publish.EnsureSuccessStatusCode();

        var remove = await Client.PutAsJsonAsync("/api/trainers/me/payment-link", new { url = "" });
        var body = await remove.Content.ReadAsStringAsync();

        remove.StatusCode.Should().Be(HttpStatusCode.NoContent,
            $"spec B3 says removal is PUT with an empty url (got {(int)remove.StatusCode}: {body})");

        var read = await Client.GetAsync("/api/trainers/me/payment-link");
        read.StatusCode.Should().Be(HttpStatusCode.NotFound);
    }

    // QA-B13. Spec A3 / TrialBanner: the FE must show "Prueba Premium · termina el {fecha}" and
    // "Día X de 7". The subscription resource exposes no trial end date at all.
    [Fact]
    public async Task QaB13_SubscriptionEndpoint_ExposesTrialEndForTheTrialBanner()
    {
        using var scope = Factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<MainDbContext>();
        db.Subscriptions.Add(new Subscription
        {
            UserId = TestDataSeeder.TestUserId,
            GatewaySubscriptionId = "sub_qa_b13",
            PlanSlug = "premium-monthly",
            Status = SubscriptionStatus.Trialing,
            CurrentPeriodEnd = DateTime.UtcNow.AddDays(7),
        });
        await db.SaveChangesAsync();

        var response = await Client.GetAsync("/api/billing/subscription");
        response.EnsureSuccessStatusCode();
        var body = await response.Content.ReadAsStringAsync();

        using var json = JsonDocument.Parse(body);
        var hasTrialEnd = json.RootElement.TryGetProperty("trialEndsAt", out _)
                          || json.RootElement.TryGetProperty("trialEnd", out _)
                          || json.RootElement.TryGetProperty("trial_ends_at", out _);

        hasTrialEnd.Should().BeTrue(
            $"the trial banner needs the trial end date and the API does not return one (body: {body})");
    }

    // QA-B14 lives in its own fixture (QaBillingGatewayRefreshProbeTests) so the fixture's
    // single-user subscription row is not polluted by the other probes in this class.
}

/// <summary>
/// Gateway-refresh probe — isolated fixture with exactly one subscription row.
/// </summary>
public class QaBillingGatewayRefreshProbeTests : ApiTestBase
{
    public QaBillingGatewayRefreshProbeTests(HealthspanApiFactory factory) : base(factory) { }

    // QA-B14 (characterisation, documents a risk): GET /api/billing/subscription refreshes from
    // the gateway and, when Stripe answers "not found", it PERSISTS the local subscription as
    // canceled. A test/live API-key mismatch (or a subscription created under a different key)
    // therefore silently flips every paying user to "canceled" on the first page load.
    [Fact]
    public async Task QaB14_GatewayLookupMiss_MarksLocalSubscriptionCanceled()
    {
        using var scope = Factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<MainDbContext>();
        db.Subscriptions.Add(new Subscription
        {
            UserId = TestDataSeeder.TestUserId,
            GatewaySubscriptionId = "sub_from_other_stripe_key",
            PlanSlug = "premium-monthly",
            Status = SubscriptionStatus.Active,
            CurrentPeriodEnd = DateTime.UtcNow.AddDays(20),
        });
        await db.SaveChangesAsync();

        var response = await Client.GetAsync("/api/billing/subscription");
        response.EnsureSuccessStatusCode();
        var body = await response.Content.ReadAsStringAsync();

        using (var checkScope = Factory.Services.CreateScope())
        {
            var checkDb = checkScope.ServiceProvider.GetRequiredService<MainDbContext>();
            var stored = await checkDb.Subscriptions.IgnoreQueryFilters()
                .FirstAsync(s => s.GatewaySubscriptionId == "sub_from_other_stripe_key");

            stored.Status.Should().Be(SubscriptionStatus.Canceled,
                "documents current behaviour: a gateway 404 is treated as cancellation and saved");
        }

        body.Should().Contain("\"canceled\"");
    }
}

/// <summary>
/// Free-user contract probe — isolated fixture so no other test creates a subscription.
/// </summary>
public class QaBillingFreeUserProbeTests : ApiTestBase
{
    public QaBillingFreeUserProbeTests(HealthspanApiFactory factory) : base(factory) { }

    // QA-B6. Spec §30: "La pantalla se construye desde GET /api/billing/subscription" with the
    // domain state `none`. The API answers 404 problem-details for a normal free user, so the
    // FE has to treat an error as a normal state.
    [Fact]
    public async Task QaB6_SubscriptionEndpoint_FreeUser_GetsStatusNoneNot404()
    {
        var response = await Client.GetAsync("/api/billing/subscription");
        var body = await response.Content.ReadAsStringAsync();

        response.StatusCode.Should().Be(HttpStatusCode.OK,
            $"free users are a normal state, not an error (got {(int)response.StatusCode}: {body})");

        using var json = JsonDocument.Parse(body);
        json.RootElement.TryGetProperty("status", out var status).Should().BeTrue();
        status.GetString().Should().Be("none");
    }
}

/// <summary>
/// Sponsored-offers probes (flag ON) — security/attribution.
/// </summary>
public class QaSponsoredProbeTests
{
    // QA-S1 (low/medium). The serve-time allowlist accepts http:// offers; an affiliate link
    // would be handed to users over plain HTTP.
    [Fact]
    public async Task QaS1_SponsoredSlot_IsNotServedForInsecureHttpOfferUrl()
    {
        using var factory = new SponsoredOffersTests.EnabledFactory();
        await SponsoredOffersTests.SeedSponsoredFixtureAsync(factory, deliveries: 10);

        // Flip the curated offer to plain http on the SAME allowlisted domain: everything else
        // (eligibility, caps, rotation) stays exactly as the passing baseline test.
        using (var seedScope = factory.Services.CreateScope())
        {
            var db = seedScope.ServiceProvider.GetRequiredService<MainDbContext>();
            var offer = await db.SponsoredOffers.FirstAsync(o => o.Slug == "creatina-monohidrato");
            offer.ExternalUrl = "http://store.example.com/creatina";
            await db.SaveChangesAsync();
        }

        using var client = factory.CreateClient();
        var readiness = await client.GetFromJsonAsync<ReadinessResource>($"/readiness?traineeId={TestDataSeeder.TestUserId}");

        readiness.Should().NotBeNull();
        readiness!.Sponsored.Should().NotBeNull(
            "the baseline fixture serves this slot; if it is null the probe is inconclusive");
        readiness.Sponsored!.ExternalUrl.Should().StartWith("https://",
            $"a sponsored affiliate link must never be served over plain http (got {readiness.Sponsored.ExternalUrl})");
    }

    // QA-S2 (privacy, expected to pass): a trainer querying a trainee's readiness must not
    // attribute impressions/deliveries to the trainee.
    [Fact]
    public async Task QaS2_ReadinessForTrainee_AttributesDeliveriesToRequesterOnly()
    {
        using var factory = new SponsoredOffersTests.EnabledFactory();
        await SponsoredOffersTests.SeedSponsoredFixtureAsync(factory, deliveries: 10);

        using var client = factory.CreateClient();
        var readiness = await client.GetFromJsonAsync<ReadinessResource>($"/readiness?traineeId={TestDataSeeder.TraineeUserId}");
        readiness.Should().NotBeNull();

        using var scope = factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<MainDbContext>();

        var traineeDeliveries = await db.RecommendationDeliveries
            .IgnoreQueryFilters()
            .CountAsync(d => d.UserId == TestDataSeeder.TraineeUserId);
        var requesterDeliveries = await db.RecommendationDeliveries
            .IgnoreQueryFilters()
            .CountAsync(d => d.UserId == TestDataSeeder.TestUserId);

        traineeDeliveries.Should().Be(0,
            "the trainee's data was read by the trainer; the impression belongs to the requester");
        requesterDeliveries.Should().BeGreaterThan(10);
    }
}
