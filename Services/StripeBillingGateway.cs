#nullable enable

using System.Net.Http.Headers;
using System.Text.Json;
using healthspan_api.Options;
using Microsoft.Extensions.Options;

namespace Services;

/// <summary>
/// Stripe implementation of <see cref="IBillingGateway"/> using the raw REST API
/// over HttpClient — no Stripe SDK dependency. Stripe keys come from configuration;
/// the gateway is registered only when configured, and every other layer depends
/// exclusively on the interface.
/// </summary>
public class StripeBillingGateway : IBillingGateway
{
    private const string ApiBase = "https://api.stripe.com/v1";

    private readonly HttpClient _httpClient;
    private readonly StripeSettings _settings;

    public StripeBillingGateway(HttpClient httpClient, IOptions<StripeSettings> settings)
    {
        _httpClient = httpClient;
        _settings = settings.Value;
    }

    public bool IsConfigured => _settings.IsConfigured;

    public async Task<GatewayCheckoutSession> CreateCheckoutSessionAsync(
        string userId,
        string planSlug,
        string priceId,
        string idempotencyKey,
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(priceId))
        {
            throw new InvalidOperationException($"No Stripe price configured for plan '{planSlug}'.");
        }

        var form = new Dictionary<string, string>
        {
            ["mode"] = "subscription",
            ["line_items[0][price]"] = priceId,
            ["line_items[0][quantity]"] = "1",
            // QA-B9 (product decision): 7-day trial WITH a card collected upfront.
            // Stripe hosted checkout cannot start a subscription trial without a
            // payment method, so 'trial sin tarjeta' is not realizable; the honest
            // copy is "7 días gratis, luego se cobra automáticamente. Cancela
            // cuando quieras."
            ["subscription_data[trial_period_days]"] = "7",
            ["metadata[userId]"] = userId,
            ["metadata[planSlug]"] = planSlug,
            ["success_url"] = _settings.SuccessUrl,
            ["cancel_url"] = _settings.CancelUrl,
        };

        using var response = await PostAsync("/checkout/sessions", form, idempotencyKey, cancellationToken);
        var json = await ReadJsonAsync(response, "create a checkout session", cancellationToken);

        var sessionId = json.TryGetProperty("id", out var id) ? id.GetString() : null;
        var url = json.TryGetProperty("url", out var u) ? u.GetString() : null;

        if (string.IsNullOrWhiteSpace(sessionId) || string.IsNullOrWhiteSpace(url))
        {
            throw new InvalidOperationException("Stripe did not return a checkout session url.");
        }

        return new GatewayCheckoutSession(sessionId, url);
    }

    public async Task<GatewaySubscriptionSnapshot?> GetSubscriptionAsync(
        string gatewaySubscriptionId,
        CancellationToken cancellationToken = default)
    {
        using var response = await GetAsync($"/subscriptions/{Uri.EscapeDataString(gatewaySubscriptionId)}", cancellationToken);
        if (response.StatusCode == System.Net.HttpStatusCode.NotFound)
        {
            return null;
        }

        var json = await ReadJsonAsync(response, "read a subscription", cancellationToken);
        return ParseSubscription(json);
    }

    public async Task<GatewaySubscriptionSnapshot> CancelSubscriptionAsync(
        string gatewaySubscriptionId,
        CancellationToken cancellationToken = default)
    {
        var form = new Dictionary<string, string> { ["cancel_at_period_end"] = "true" };

        using var response = await PostAsync(
            $"/subscriptions/{Uri.EscapeDataString(gatewaySubscriptionId)}",
            form,
            idempotencyKey: null,
            cancellationToken);

        var json = await ReadJsonAsync(response, "cancel a subscription", cancellationToken);
        return ParseSubscription(json);
    }

    public async Task<IReadOnlyList<GatewayReceipt>> ListReceiptsAsync(
        string gatewayCustomerId,
        CancellationToken cancellationToken = default)
    {
        using var response = await GetAsync(
            $"/invoices?customer={Uri.EscapeDataString(gatewayCustomerId)}&status=paid&limit=100",
            cancellationToken);

        var json = await ReadJsonAsync(response, "list invoices", cancellationToken);

        var receipts = new List<GatewayReceipt>();
        if (json.TryGetProperty("data", out var data) && data.ValueKind == JsonValueKind.Array)
        {
            foreach (var invoice in data.EnumerateArray())
            {
                var id = invoice.TryGetProperty("id", out var idEl) ? idEl.GetString() : null;
                if (string.IsNullOrWhiteSpace(id))
                {
                    continue;
                }

                var created = invoice.TryGetProperty("created", out var createdEl) && createdEl.TryGetInt64(out var createdUnix)
                    ? DateTimeOffset.FromUnixTimeSeconds(createdUnix).UtcDateTime
                    : DateTime.MinValue;

                var amount = invoice.TryGetProperty("amount_paid", out var amountEl) && amountEl.TryGetInt32(out var amountPaid)
                    ? amountPaid
                    : (invoice.TryGetProperty("amount_due", out var dueEl) && dueEl.TryGetInt32(out var amountDue) ? amountDue : 0);

                var receiptUrl = invoice.TryGetProperty("receipt_url", out var receiptEl) ? receiptEl.GetString() : null;

                receipts.Add(new GatewayReceipt(id, created, amount, receiptUrl));
            }
        }

        return receipts;
    }

    public ParsedGatewayEvent ParseWebhookEvent(string payload, string? signatureHeader)
    {
        StripeWebhookVerifier.Verify(payload, signatureHeader, _settings.WebhookSigningSecret);

        JsonDocument json;
        try
        {
            json = JsonDocument.Parse(payload);
        }
        catch (JsonException ex)
        {
            throw new GatewayWebhookException($"Webhook payload is not valid JSON: {ex.Message}");
        }

        using (json)
        {
            var root = json.RootElement;

            var eventId = root.TryGetProperty("id", out var idEl) ? idEl.GetString() : null;
            var eventType = root.TryGetProperty("type", out var typeEl) ? typeEl.GetString() : null;

            if (string.IsNullOrWhiteSpace(eventId) || string.IsNullOrWhiteSpace(eventType))
            {
                throw new GatewayWebhookException("Webhook payload is missing id or type.");
            }

            GatewaySubscriptionSnapshot? subscription = null;
            string? sessionId = null;
            string? metadataUserId = null;
            string? metadataPlanSlug = null;
            string? gatewayCustomerId = null;

            if (root.TryGetProperty("data", out var data) && data.TryGetProperty("object", out var obj))
            {
                if (obj.ValueKind == JsonValueKind.Object)
                {
                    gatewayCustomerId = obj.TryGetProperty("customer", out var custEl)
                        ? custEl.ValueKind == JsonValueKind.Object
                            ? custEl.TryGetProperty("id", out var cid) ? cid.GetString() : null
                            : custEl.GetString()
                        : null;

                    if (eventType.StartsWith("customer.subscription"))
                    {
                        subscription = ParseSubscription(obj);
                    }
                    else if (eventType == "checkout.session.completed")
                    {
                        sessionId = obj.TryGetProperty("id", out var sessionEl) ? sessionEl.GetString() : null;
                        var subscriptionId = obj.TryGetProperty("subscription", out var subEl) ? subEl.GetString() : null;
                        var customerId = obj.TryGetProperty("customer", out var custEl2) ? custEl2.GetString() : null;

                        if (!string.IsNullOrWhiteSpace(subscriptionId))
                        {
                            // The checkout event only carries ids; status is filled by the
                            // matching customer.subscription.* event that Stripe also sends.
                            subscription = new GatewaySubscriptionSnapshot(
                                subscriptionId,
                                customerId,
                                Status: "incomplete",
                                CurrentPeriodEnd: null,
                                CancelAtPeriodEnd: false);
                        }

                        if (obj.TryGetProperty("metadata", out var metadata) && metadata.ValueKind == JsonValueKind.Object)
                        {
                            metadataUserId = metadata.TryGetProperty("userId", out var userIdEl) ? userIdEl.GetString() : null;
                            metadataPlanSlug = metadata.TryGetProperty("planSlug", out var planSlugEl) ? planSlugEl.GetString() : null;
                        }
                    }
                    else if (eventType == "invoice.payment_failed" || eventType == "charge.refunded")
                    {
                        // QA-B4/B5: invoice and charge objects reference the subscription
                        // and customer by id; BillingService derives the state change from
                        // the event type itself.
                        var relatedSubscriptionId = obj.TryGetProperty("subscription", out var relatedEl) ? relatedEl.GetString() : null;

                        if (!string.IsNullOrWhiteSpace(relatedSubscriptionId))
                        {
                            subscription = new GatewaySubscriptionSnapshot(
                                relatedSubscriptionId,
                                gatewayCustomerId,
                                Status: eventType == "invoice.payment_failed" ? "past_due" : "canceled",
                                CurrentPeriodEnd: null,
                                CancelAtPeriodEnd: false);
                        }
                    }
                }
            }

            return new ParsedGatewayEvent(eventId, eventType, payload, subscription, sessionId, metadataUserId, metadataPlanSlug, gatewayCustomerId);
        }
    }

    private static GatewaySubscriptionSnapshot ParseSubscription(JsonElement json)
    {
        var id = json.TryGetProperty("id", out var idEl) ? idEl.GetString() : null;
        if (string.IsNullOrWhiteSpace(id))
        {
            throw new InvalidOperationException("Stripe subscription object is missing an id.");
        }

        var customerId = json.TryGetProperty("customer", out var customerEl)
            ? customerEl.ValueKind == JsonValueKind.Object
                ? customerEl.TryGetProperty("id", out var cid) ? cid.GetString() : null
                : customerEl.GetString()
            : null;

        var status = json.TryGetProperty("status", out var statusEl) ? statusEl.GetString() ?? "incomplete" : "incomplete";

        DateTime? periodEnd = null;
        if (json.TryGetProperty("current_period_end", out var periodEl) && periodEl.TryGetInt64(out var periodUnix))
        {
            periodEnd = DateTimeOffset.FromUnixTimeSeconds(periodUnix).UtcDateTime;
        }

        var cancelAtPeriodEnd = json.TryGetProperty("cancel_at_period_end", out var cancelEl) && cancelEl.GetBoolean();

        return new GatewaySubscriptionSnapshot(id, customerId, status, periodEnd, cancelAtPeriodEnd);
    }

    private async Task<HttpResponseMessage> GetAsync(string path, CancellationToken cancellationToken)
    {
        using var request = new HttpRequestMessage(HttpMethod.Get, ApiBase + path);
        ApplyAuth(request);
        return await _httpClient.SendAsync(request, cancellationToken);
    }

    private async Task<HttpResponseMessage> PostAsync(
        string path,
        Dictionary<string, string> form,
        string? idempotencyKey,
        CancellationToken cancellationToken)
    {
        using var request = new HttpRequestMessage(HttpMethod.Post, ApiBase + path)
        {
            Content = new FormUrlEncodedContent(form)
        };
        ApplyAuth(request);
        if (!string.IsNullOrWhiteSpace(idempotencyKey))
        {
            request.Headers.TryAddWithoutValidation("Idempotency-Key", idempotencyKey);
        }
        return await _httpClient.SendAsync(request, cancellationToken);
    }

    private void ApplyAuth(HttpRequestMessage request)
    {
        if (!IsConfigured)
        {
            throw new InvalidOperationException("Stripe billing gateway is not configured.");
        }

        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", _settings.SecretKey);
    }

    private static async Task<JsonElement> ReadJsonAsync(
        HttpResponseMessage response,
        string operation,
        CancellationToken cancellationToken)
    {
        var body = await response.Content.ReadAsStringAsync(cancellationToken);

        if (!response.IsSuccessStatusCode)
        {
            // Stripe error bodies may contain request ids only; never surface card-adjacent details.
            throw new InvalidOperationException($"Stripe failed to {operation} (HTTP {(int)response.StatusCode}).");
        }

        using var json = JsonDocument.Parse(body);
        return json.RootElement.Clone();
    }
}
