using System.Net;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text;
using System.Text.Json;
using FluentAssertions;
using healthspan_api.Options;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Services;
using Xunit;

namespace healthspan_api.tests.Regression;

/// <summary>
/// Permanent regression tests for the QA payment re-test findings (t_183251da),
/// one fixture (own database) per scenario so webhook events cannot pollute
/// each other. Each test inverts a QA probe: it must FAIL if the fix regresses.
/// </summary>
public static class QaFixPayloads
{
    public static string CheckoutCompleted(string eventId, string subscriptionId, string customerId, string planSlug = "premium-monthly")
        => JsonSerializer.Serialize(new
        {
            id = eventId,
            type = "checkout.session.completed",
            data = new
            {
                @object = new
                {
                    id = $"cs_{eventId}",
                    subscription = subscriptionId,
                    customer = customerId,
                    metadata = new
                    {
                        userId = HealthspanApiFactory.TestUserId,
                        planSlug,
                    },
                },
            },
        });

    public static string SubscriptionUpdated(string eventId, string subscriptionId, string customerId, string status)
        => JsonSerializer.Serialize(new
        {
            id = eventId,
            type = "customer.subscription.updated",
            data = new
            {
                @object = new
                {
                    id = subscriptionId,
                    customer = customerId,
                    status,
                    current_period_end = DateTimeOffset.UtcNow.ToUnixTimeSeconds() + 30 * 24 * 3600,
                    cancel_at_period_end = false,
                },
            },
        });

    public static string ChargeRefunded(string eventId, string chargeId, string customerId)
        => JsonSerializer.Serialize(new
        {
            id = eventId,
            type = "charge.refunded",
            data = new
            {
                @object = new
                {
                    id = chargeId,
                    customer = customerId,
                    status = "refunded",
                    amount_refunded = 999,
                },
            },
        });

    public static string InvoicePaymentFailed(string eventId, string invoiceId, string subscriptionId, string customerId)
        => JsonSerializer.Serialize(new
        {
            id = eventId,
            type = "invoice.payment_failed",
            data = new
            {
                @object = new
                {
                    id = invoiceId,
                    customer = customerId,
                    subscription = subscriptionId,
                    attempt_count = 2,
                },
            },
        });
}

/// <summary>
/// QA-B2 regression: replaying an Idempotency-Key with a DIFFERENT plan must
/// 409 (idempotency_conflict) — never silently return the original session,
/// which would charge a plan the user never asked for.
/// </summary>
public class BillingIdempotencyConflictTests : ApiTestBase
{
    public BillingIdempotencyConflictTests(HealthspanApiFactory factory) : base(factory) { }

    private async Task<(HttpStatusCode StatusCode, JsonElement Body)> PostCheckoutSessionAsync(string plan, string key)
    {
        using var request = new HttpRequestMessage(HttpMethod.Post, "/api/billing/checkout-session")
        {
            Content = new StringContent(JsonSerializer.Serialize(new { plan }), Encoding.UTF8, "application/json"),
        };
        request.Headers.TryAddWithoutValidation("Idempotency-Key", key);

        var response = await Client.SendAsync(request);
        return (response.StatusCode, JsonDocument.Parse(await response.Content.ReadAsStringAsync()).RootElement.Clone());
    }

    [Fact]
    public async Task CheckoutSession_ReplayedKeyWithDifferentPlan_Returns409Conflict()
    {
        var (firstStatus, firstBody) = await PostCheckoutSessionAsync("premium-monthly", "idem-b2-key");
        firstStatus.Should().Be(HttpStatusCode.OK);

        var (conflictStatus, conflictBody) = await PostCheckoutSessionAsync("premium-annual", "idem-b2-key");

        conflictStatus.Should().Be(HttpStatusCode.Conflict);
        conflictBody.GetProperty("code").GetString().Should().Be("idempotency_conflict");

        // The original session is untouched: same-key same-plan replay still works.
        var (replayStatus, replayBody) = await PostCheckoutSessionAsync("premium-monthly", "idem-b2-key");
        replayStatus.Should().Be(HttpStatusCode.OK);
        replayBody.GetProperty("sessionId").GetString().Should().Be(firstBody.GetProperty("sessionId").GetString());
    }
}

/// <summary>
/// QA-B3 regression (product decision: one live subscription per user): a
/// second checkout while the mirror is active must 409 (subscription_active)
/// and must NOT orphan or cancel the previous gateway subscription.
/// </summary>
public class BillingActiveSubscriptionTests : ApiTestBase
{
    public BillingActiveSubscriptionTests(HealthspanApiFactory factory) : base(factory) { }

    [Fact]
    public async Task CheckoutSession_WhileSubscriptionActive_Returns409_AndLeavesGatewaySubscriptionIntact()
    {
        var completed = QaFixPayloads.CheckoutCompleted("evt_b3_completed", "sub_b3", "cus_b3");
        var (completedStatus, _) = await PostWebhookAsync(completed);
        completedStatus.Should().Be(HttpStatusCode.OK);

        var updated = QaFixPayloads.SubscriptionUpdated("evt_b3_active", "sub_b3", "cus_b3", "active");
        var (updatedStatus, _) = await PostWebhookAsync(updated);
        updatedStatus.Should().Be(HttpStatusCode.OK);

        using var request = new HttpRequestMessage(HttpMethod.Post, "/api/billing/checkout-session")
        {
            Content = new StringContent(JsonSerializer.Serialize(new { plan = "premium-annual" }), Encoding.UTF8, "application/json"),
        };
        request.Headers.TryAddWithoutValidation("Idempotency-Key", "idem-b3-second");

        var response = await Client.SendAsync(request);
        var body = JsonDocument.Parse(await response.Content.ReadAsStringAsync()).RootElement;

        response.StatusCode.Should().Be(HttpStatusCode.Conflict);
        body.GetProperty("code").GetString().Should().Be("subscription_active");

        // No orphaning: the existing gateway subscription is still active and not
        // scheduled for cancellation.
        var gatewaySubscription = await Factory.BillingGateway.GetSubscriptionAsync("sub_b3");
        gatewaySubscription.Should().NotBeNull();
        gatewaySubscription!.Status.Should().Be("active");
        gatewaySubscription.CancelAtPeriodEnd.Should().BeFalse();
    }

    private async Task<(HttpStatusCode StatusCode, JsonElement Body)> PostWebhookAsync(string payload)
    {
        using var request = new HttpRequestMessage(HttpMethod.Post, "/api/billing/webhook/stripe")
        {
            Content = new StringContent(payload, Encoding.UTF8, "application/json"),
        };
        request.Headers.TryAddWithoutValidation("Stripe-Signature", FakeBillingGateway.Sign(payload));

        var response = await Client.SendAsync(request);
        return (response.StatusCode, JsonDocument.Parse(await response.Content.ReadAsStringAsync()).RootElement.Clone());
    }
}

/// <summary>
/// QA-B4 regression: charge.refunded must revoke Premium — the local mirror
/// goes to canceled with the Refunded flag, and GET exposes both.
/// </summary>
public class BillingRefundTests : ApiTestBase
{
    public BillingRefundTests(HealthspanApiFactory factory) : base(factory) { }

    [Fact]
    public async Task Webhook_ChargeRefunded_CancelsAccess_AndFlagsSubscriptionRefunded()
    {
        var completed = QaFixPayloads.CheckoutCompleted("evt_b4_completed", "sub_b4", "cus_b4");
        (await PostWebhookAsync(completed)).StatusCode.Should().Be(HttpStatusCode.OK);

        var updated = QaFixPayloads.SubscriptionUpdated("evt_b4_active", "sub_b4", "cus_b4", "active");
        (await PostWebhookAsync(updated)).StatusCode.Should().Be(HttpStatusCode.OK);

        var refund = QaFixPayloads.ChargeRefunded("evt_b4_refund", "ch_b4", "cus_b4");
        var (refundStatus, refundBody) = await PostWebhookAsync(refund);

        refundStatus.Should().Be(HttpStatusCode.OK);
        refundBody.GetProperty("outcome").GetString().Should().Be("processed");

        using (var scope = Factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<MainDbContext>();
            var subscription = db.Subscriptions.IgnoreQueryFilters().Single(s => s.GatewaySubscriptionId == "sub_b4");
            subscription.Status.Should().Be(SubscriptionStatus.Canceled);
            subscription.Refunded.Should().BeTrue();
        }

        var getResponse = await Client.GetAsync("/api/billing/subscription");
        getResponse.StatusCode.Should().Be(HttpStatusCode.OK);

        var body = JsonDocument.Parse(await getResponse.Content.ReadAsStringAsync()).RootElement;
        body.GetProperty("status").GetString().Should().Be("canceled");
        body.GetProperty("refunded").GetBoolean().Should().BeTrue();
    }

    private async Task<(HttpStatusCode StatusCode, JsonElement Body)> PostWebhookAsync(string payload)
    {
        using var request = new HttpRequestMessage(HttpMethod.Post, "/api/billing/webhook/stripe")
        {
            Content = new StringContent(payload, Encoding.UTF8, "application/json"),
        };
        request.Headers.TryAddWithoutValidation("Stripe-Signature", FakeBillingGateway.Sign(payload));

        var response = await Client.SendAsync(request);
        return (response.StatusCode, JsonDocument.Parse(await response.Content.ReadAsStringAsync()).RootElement.Clone());
    }
}

/// <summary>
/// QA-B5 regression: invoice.payment_failed must move the mirror to past_due
/// (UX state 'pago fallado'), recoverable — not canceled.
/// </summary>
public class BillingPaymentFailedTests : ApiTestBase
{
    public BillingPaymentFailedTests(HealthspanApiFactory factory) : base(factory) { }

    [Fact]
    public async Task Webhook_InvoicePaymentFailed_MovesSubscriptionToPastDue()
    {
        var completed = QaFixPayloads.CheckoutCompleted("evt_b5_completed", "sub_b5", "cus_b5");
        (await PostWebhookAsync(completed)).StatusCode.Should().Be(HttpStatusCode.OK);

        var updated = QaFixPayloads.SubscriptionUpdated("evt_b5_active", "sub_b5", "cus_b5", "active");
        (await PostWebhookAsync(updated)).StatusCode.Should().Be(HttpStatusCode.OK);

        var failed = QaFixPayloads.InvoicePaymentFailed("evt_b5_failed", "in_b5", "sub_b5", "cus_b5");
        var (failedStatus, failedBody) = await PostWebhookAsync(failed);

        failedStatus.Should().Be(HttpStatusCode.OK);
        failedBody.GetProperty("outcome").GetString().Should().Be("processed");

        using (var scope = Factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<MainDbContext>();
            db.Subscriptions.IgnoreQueryFilters().Single(s => s.GatewaySubscriptionId == "sub_b5")
                .Status.Should().Be(SubscriptionStatus.PastDue);
        }

        var getResponse = await Client.GetAsync("/api/billing/subscription");
        var body = JsonDocument.Parse(await getResponse.Content.ReadAsStringAsync()).RootElement;
        body.GetProperty("status").GetString().Should().Be("past_due");
    }

    private async Task<(HttpStatusCode StatusCode, JsonElement Body)> PostWebhookAsync(string payload)
    {
        using var request = new HttpRequestMessage(HttpMethod.Post, "/api/billing/webhook/stripe")
        {
            Content = new StringContent(payload, Encoding.UTF8, "application/json"),
        };
        request.Headers.TryAddWithoutValidation("Stripe-Signature", FakeBillingGateway.Sign(payload));

        var response = await Client.SendAsync(request);
        return (response.StatusCode, JsonDocument.Parse(await response.Content.ReadAsStringAsync()).RootElement.Clone());
    }
}

/// <summary>
/// QA-B13 regression: a trialing subscription must expose trialEnd so the FE
/// TrialBanner can render "Prueba Premium · termina el {fecha}" / "Día X de 7".
/// </summary>
public class BillingTrialDisplayTests : ApiTestBase
{
    public BillingTrialDisplayTests(HealthspanApiFactory factory) : base(factory) { }

    [Fact]
    public async Task GetSubscription_Trialing_ExposesTrialEnd()
    {
        var trialEnd = DateTime.UtcNow.AddDays(7);

        using (var scope = Factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<MainDbContext>();
            db.Subscriptions.Add(new Subscription
            {
                UserId = HealthspanApiFactory.TestUserId,
                GatewaySubscriptionId = "sub_b13",
                GatewayCustomerId = "cus_b13",
                PlanSlug = "premium-monthly",
                Status = SubscriptionStatus.Trialing,
                CurrentPeriodEnd = trialEnd,
            });
            await db.SaveChangesAsync();
        }

        var response = await Client.GetAsync("/api/billing/subscription");
        response.StatusCode.Should().Be(HttpStatusCode.OK);

        var body = JsonDocument.Parse(await response.Content.ReadAsStringAsync()).RootElement;
        body.GetProperty("status").GetString().Should().Be("trialing");
        body.TryGetProperty("trialEnd", out var trialEndEl).Should().BeTrue();
        trialEndEl.GetDateTime().Should().BeCloseTo(trialEnd, TimeSpan.FromSeconds(1));
    }
}

/// <summary>
/// QA-B14 regression: a gateway lookup miss (e.g. API-key mismatch) must NOT
/// cancel the local mirror. Only an explicit customer.subscription.deleted
/// webhook may cancel it.
/// </summary>
public class BillingGatewayMissTests : ApiTestBase
{
    public BillingGatewayMissTests(HealthspanApiFactory factory) : base(factory) { }

    [Fact]
    public async Task GetSubscription_GatewayLookupMiss_KeepsLocalStateUnchanged()
    {
        using (var scope = Factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<MainDbContext>();
            db.Subscriptions.Add(new Subscription
            {
                UserId = HealthspanApiFactory.TestUserId,
                // Unknown to the gateway: GetSubscriptionAsync will miss.
                GatewaySubscriptionId = "sub_b14_missing",
                GatewayCustomerId = "cus_b14",
                PlanSlug = "premium-monthly",
                Status = SubscriptionStatus.Active,
                CurrentPeriodEnd = DateTime.UtcNow.AddDays(20),
            });
            await db.SaveChangesAsync();
        }

        var response = await Client.GetAsync("/api/billing/subscription");
        response.StatusCode.Should().Be(HttpStatusCode.OK);

        var body = JsonDocument.Parse(await response.Content.ReadAsStringAsync()).RootElement;
        body.GetProperty("status").GetString().Should().Be("active");

        using (var scope = Factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<MainDbContext>();
            db.Subscriptions.IgnoreQueryFilters().Single(s => s.GatewaySubscriptionId == "sub_b14_missing")
                .Status.Should().Be(SubscriptionStatus.Active, "a gateway miss is not evidence of cancellation");
        }
    }
}

/// <summary>
/// QA-B9 regression (product decision): Stripe checkout requests a 7-day trial
/// and collects the card upfront (no payment_method_collection=if_required).
/// </summary>
public class StripeCheckoutFormTests
{
    private sealed class CapturingHandler : HttpMessageHandler
    {
        public string? LastFormBody { get; private set; }

        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            LastFormBody = request.Content is null
                ? null
                : await request.Content.ReadAsStringAsync(cancellationToken);

            return new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent(
                    JsonSerializer.Serialize(new { id = "cs_b9", url = "https://checkout.stripe.com/c/pay/cs_b9" }),
                    Encoding.UTF8,
                    "application/json"),
            };
        }
    }

    [Fact]
    public async Task CreateCheckoutSession_RequestsSevenDayTrial_WithCardCollectedUpfront()
    {
        var handler = new CapturingHandler();
        var settings = Microsoft.Extensions.Options.Options.Create(new StripeSettings
        {
            SecretKey = "sk_test_b9",
            WebhookSigningSecret = "whsec_b9",
            PriceIds = { ["premium-monthly"] = "price_b9" },
        });

        using var httpClient = new HttpClient(handler);
        var gateway = new StripeBillingGateway(httpClient, settings);

        var session = await gateway.CreateCheckoutSessionAsync(
            "user-b9", "premium-monthly", "price_b9", "idem-b9");

        session.SessionId.Should().Be("cs_b9");

        var body = Uri.UnescapeDataString(handler.LastFormBody ?? string.Empty);
        body.Should().Contain("subscription_data[trial_period_days]=7");
        body.Should().NotContain("payment_method_collection",
            "the trial collects the card upfront: Stripe cannot start a subscription trial without a payment method");
    }
}

/// <summary>
/// QA-S1 regression: a sponsored offer whose ExternalUrl is http:// must never
/// be served, even when the domain is allowlisted.
/// </summary>
public class SponsoredHttpsOnlyTests
{
    private sealed class HttpOfferFactory : SponsoredOffersTests.EnabledFactory
    {
        protected override void ConfigureWebHost(Microsoft.AspNetCore.Hosting.IWebHostBuilder builder)
        {
            base.ConfigureWebHost(builder);
            // Allowlist covers the host; the scheme under test is http.
            builder.UseSetting("SponsoredOffersAllowedDomains:0", "store.example.com");
        }
    }

    [Fact]
    public async Task SponsoredSlot_IsNotServed_ForHttpExternalUrl_OnAllowlistedDomain()
    {
        using var factory = new HttpOfferFactory();

        using (var seedScope = factory.Services.CreateScope())
        {
            await TestDataSeeder.SeedAsync(seedScope.ServiceProvider);
        }

        using (var scope = factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<MainDbContext>();

            db.SponsoredOffers.Add(new SponsoredOffer
            {
                Slug = "creatina-http",
                DisplayName = "Creatina (http)",
                Description = "Enlace sin TLS.",
                Category = SponsoredOfferCategory.Supplement,
                ExternalUrl = "http://store.example.com/creatina",
                Active = true,
            });

            for (var i = 0; i < 10; i++)
            {
                db.RecommendationDeliveries.Add(new RecommendationDelivery
                {
                    UserId = TestDataSeeder.TestUserId,
                    SuggestionCount = 1,
                    OccurredAtUtc = DateTime.UtcNow.AddMinutes(-30 - i),
                });
            }

            await db.SaveChangesAsync();
        }

        using var client = factory.CreateClient();
        var readiness = await client.GetFromJsonAsync<ReadinessResource>($"/readiness?traineeId={TestDataSeeder.TestUserId}");

        readiness.Should().NotBeNull();
        readiness!.Suggestions.Should().NotBeEmpty("organic suggestions must exist so the slot could attach if allowed");
        readiness.Sponsored.Should().BeNull("http:// affiliate links must never be served (QA-S1)");
    }
}
