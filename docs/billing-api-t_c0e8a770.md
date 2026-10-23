# Billing API — Stripe integration (t_c0e8a770)

Base URL: `/api`. All billing endpoints require a Bearer token (Identity JWT).
All errors are ProblemDetails with a machine-readable `code` extension.

## Configuration (server-side, never exposed to the FE)

```
Stripe:SecretKey                   sk_...          (user secrets in dev, env var in prod)
Stripe:WebhookSigningSecret        whsec_...       (user secrets in dev, env var in prod)
Stripe:PriceIds                    { "premium-monthly": "price_..." }
Stripe:SuccessUrl / Stripe:CancelUrl   checkout redirects (dev defaults in appsettings)
```

Without `SecretKey`, checkout returns `503 billing_not_configured`.
Without `WebhookSigningSecret`, webhooks return `400 invalid_webhook_signature`.

## Plans (seeded, DB-driven)

| slug | price | interval |
|---|---|---|
| `premium-monthly` | 999 ct EUR | month |
| `premium-annual` | 8999 ct EUR | year |

## Endpoints

### POST /api/billing/checkout-session
Auth: required. Header `Idempotency-Key: <client key>` **required**.
Body: `{ "plan": "premium-monthly" }`

- `200` → `{ sessionId, checkoutUrl, plan }` — no Stripe secrets are ever returned to the client.
- `400 idempotency_key_required` — missing header.
- `400 plan_required` — missing body field.
- `404 plan_not_found` — unknown slug.
- `503 billing_not_configured` — Stripe key not set up.

Idempotency: replaying the same `Idempotency-Key` (per user) returns the stored
session and never calls Stripe twice.

### POST /api/billing/webhook/stripe
Auth: none (Stripe calls it). Header `Stripe-Signature: t=...,v1=...` verified
with HMAC-SHA256 against `WebhookSigningSecret` (constant-time, 300 s tolerance).

Handled events (idempotent by event id — Stripe retries are deduped):
- `checkout.session.completed` → creates local subscription mirror + customer link.
- `customer.subscription.updated` → mirrors status / period end / cancel flag.
- `customer.subscription.deleted` → marks `canceled`.

Responses: `200 { received: true, outcome: "processed" | "duplicate" | "ignored" }`,
`400 invalid_webhook_signature` (bad/missing/tampered signature).

### GET /api/billing/subscription
- `200` → `{ plan, status, currentPeriodEnd, cancelAtPeriodEnd }`
- `404 subscription_not_found`

Refreshes from the gateway when the local copy is stale (>60 s).

### POST /api/billing/subscription/cancel
Cancel-at-period-end (access kept until `currentPeriodEnd`). No body.
- `200` → subscription DTO with `cancelAtPeriodEnd: true`
- `404 subscription_not_found`
- `503 billing_not_configured`

### GET /api/billing/receipts
- `200` → `[{ id, number, amountCents, currency, issuedAt, receiptUrl }]`
  (empty array when there is no subscription yet).

### Trainer payment links (Stripe-hosted only)
- `PUT /api/trainers/me/payment-link` — body `{ "url": "https://..." }`
  - `200` → `{ url, verifiedDomain, updatedAt }`
  - `400 payment_link_not_allowed` — anything that is not https on
    `checkout.stripe.com`, `buy.stripe.com`, `paypal.me`, `paypal.com`
    (lookalike domains, ports, credentials-in-URL and non-allowlisted hosts
    are rejected).
- `GET /api/trainers/me/payment-link` → `200` DTO or `404 payment_link_not_found`
- `DELETE /api/trainers/me/payment-link` → `204`
- `GET /api/trainers/{trainerId}/payment-link` → public read for trainees:
  `200 { url, verifiedDomain, updatedAt }` or `404`.

## Data model (migration `AddBillingSupport`)

- `Plans` — server-owned catalog (slug, price cents, currency, interval).
- `Subscriptions` — local mirror; `UserId` unique (one subscription per user).
- `BillingEvents` — webhook idempotency ledger (`EventId` unique).
- `CheckoutSessionRequests` — checkout idempotency ledger
  (`UserId` + `IdempotencyKey` unique).
- `TrainerPaymentLinks` — published links (`TrainerId` unique).

## Stripe dashboard setup (dev)

1. Test-mode API key → user secrets `Stripe:SecretKey`.
2. `stripe listen --forward-to https://localhost:7278/api/billing/webhook/stripe`
   → gives the `whsec_...` → user secrets `Stripe:WebhookSigningSecret`.
3. Checkout metadata must carry `userId` and `planSlug` (the API sets both).
