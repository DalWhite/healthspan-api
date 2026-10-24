# Decisión de monetización: pago entrenador↔trainee, anuncios y patrocinados

Task: t_f625fb1c · Fecha: 2026-10-23 · Estado: DECIDIDO (accionable para backend, FE y QA)

## 1. Decisión

**Modelo híbrido en dos fases. Fase 1 ahora, Fase 2 solo si la demanda lo justifica.**

### Fase 1 (implementar ahora)

1. **Healthspan Premium — suscripción propia de la plataforma.**
   - La plataforma vende su propio plan (capa de análisis/coach sobre el log gratuito). La plataforma es merchant of record de su propio servicio: **no aplica DAC7** (no hay sellers terceros).
   - Pago vía **Stripe Checkout** (hosted). Sin datos de tarjeta en la plataforma (PCI SAQ-A).
   - Free tier = registrar + log + trainer↔trainee básico. Pago = análisis avanzado. (Evidencia benchmark: TrainingPeaks [19], Strava [34][36] — paywall solo sobre análisis, nunca sobre registrar; trial honesto sin tarjeta estilo Fitbod [17].)

2. **Entrenador↔trainee: pago FUERA de la plataforma.**
   - El entrenador publica un **enlace de pago externo** (Stripe Payment Links, PayPal u otro de una allowlist). El trainee paga al entrenador directamente; Healthspan solo gestiona la relación (acceso, rutinas, permisos).
   - La plataforma **no cobra, no retiene ni transfiere dinero** → no es plataforma de reporte para esas transacciones (el consideration no es conocido por la plataforma; la plataforma solo redirige — exclusiones DAC7 verificadas: Belastingdienst "software only used to process payments, advertise, redirect" y "fee the platform is not aware of"). El entrenador gestiona su propio ingreso como ya lo hace hoy. Esto cumple el requisito del epic: **comodidad sin obligaciones declarativas para la plataforma**.
   - Consecuencia aceptada: en Fase 1 la plataforma **no cobra comisión** al trainer↔trainee. Ingresos Fase 1 = Premium + patrocinados (Fase 1b).

### Fase 2 (aplazada, exige revisión legal antes de arrancar)

- **Suscripción in-platform a entrenadores concretos** vía **Stripe Connect Standard**: Stripe onboarda/KYC al entrenador y ofrece *Platform Tax Reporting* para DAC7 (docs.stripe.com/connect/platform-tax-reporting; en **private preview**, y Stripe declina responsabilidad — la obligación legal sigue siendo de la plataforma).
- DAC7 aplica a "personal services" **sin umbral** (Directiva (EU) 2021/514; coaching entra). Si la plataforma cobra y transfiere al entrenador, pasa a ser Reporting Platform Operator: due diligence, TIN del seller, reporte anual ≤31 enero. Solo se activa si el volumen de trainers lo justifica.

### Descartado

- **Créditos internos / wallet**: lo peor legal y operativamente (dinero electrónico PSD2, float, y DAC7 no desaparece). Descartado.
- **Pago único como modelo primario**: sin ingreso recurrente. Encaja después como "packs de coaching" dentro de Connect (Fase 2), no como base.

## 2. API contract básica (para t_c0e8a770)

Proveedor aislado tras interfaz interna `IBillingGateway` (implementación Stripe; sustituible).

Modelos nuevos:
- `Plan` (slug, displayName, priceCents, currency, interval: monthly|annual, active)
- `Subscription` (userId, gatewaySubscriptionId, planSlug, status, currentPeriodEnd, cancelAtPeriodEnd)
- `BillingEvent` (gatewayEventId **unique**, type, payloadHash, processedAt) → idempotencia de webhooks
- `TrainerPaymentLink` (userId, url, verifiedDomain, updatedAt)

Endpoints:
- `POST /api/billing/checkout-session` `{plan}` → `{sessionId, checkoutUrl}` (Stripe Checkout Session; header `Idempotency-Key`)
- `GET /api/billing/subscription` → `{status, plan, currentPeriodEnd, cancelAtPeriodEnd}`
- `POST /api/billing/subscription/cancel` → cancelación al final del periodo (sin refunds automáticos en MVP)
- `GET /api/billing/receipts` → `[{id, date, amountCents, receiptUrl}]`
- `POST /api/billing/webhook/stripe` → verificación de firma + dedupe por `BillingEvent.gatewayEventId`
- `PUT /api/trainers/me/payment-link` `{url}` → valida allowlist de dominios (checkout.stripe.com, buy.stripe.com, paypal.me, paypal.com); `GET /api/trainers/{id}` expone `paymentLink` solo si el entrenador lo publicó
- `GET /api/trainers/me/earnings` → **fuera de MVP** (Fase 2/Connect)

Reglas: sin PAN/datos de tarjeta en BD; sin exposición de datos personales en receipts más allá del propio usuario; todos los endpoints de billing bajo auth y solo sobre `me` (o admin).

## 3. Política inicial de anuncios y patrocinados (para t_45c5213f)

**Fase 1: sin anuncios de terceros ni ad networks.** Audiencia pequeña = revenue ínfimo y riesgo de confianza/privacidad alto (el log de salud es dato sensible).

Recomendaciones patrocinadas (suplementos/equipo), únicas permitidas:
- Etiquetado visible obligatorio **"Patrocinado"** (no confundible con recomendación orgánica; requisito DSA de transparencia).
- Frecuencia máxima: **1 slot patrocinado por cada 10 recomendaciones** y **1 por día** por usuario. Configurable.
- Solo afiliados **pre-aprobados manualmente** (whitelist); nada self-serve en MVP.
- Prohibido: contexto médico o de marcadores de salud, usuarios menores, cualquier health/nutrition claim (Reg. CE 1924/2006; en España suplementos regulados por RD 1907/1996).
- Métricas mínimas: impresiones, clics, conversión afiliada, tasa de dismiss. Feature flag **off por defecto**.

## 4. Prioridad

- **P1**: UX de pago (t_5b46e650) sobre el modelo de Fase 1 — ya puede diseñarse.
- **P1**: backend billing MVP (t_c0e8a770): Premium Checkout + payment links + webhook. Es una rebanada pequeña (~1 semana), no bloquea la migración de páginas ni el rediseño del design system; se encaja en paralelo o justo después.
- **P2**: patrocinados (t_45c5213f): implementar modelo de datos + slots con flag **off**; activación solo cuando haya afiliados reales.
- **P3/condicional**: Connect/Fase 2 — no se implementa sin decisión de negocio + revisión legal.

## 5. Riesgos

1. **DAC7 en Fase 2**: la herramienta de Stripe está en private preview y no exime a la plataforma. Mitigación: no arrancar Fase 2 sin asesoría legal; Fase 1 está fuera por diseño.
2. **Pagos off-platform**: disputas trainer↔trainee invisibles para la plataforma; cero comisión en Fase 1. Mitigación: la relación (rutinas, permisos) sigue siendo el gancho; métrica de conversión trainer→Premium.
3. **Paywall agresivo erosiona confianza** (evidencia Strava [36], rechazo a suscripción en Hevy [37]). Mitigación: paywall solo sobre análisis; trial sin tarjeta con timeline explícito (Fitbod [17]).
4. **Publicidad de suplementos**: claims ilegales → sanción. Mitigación: whitelist + prohibición de claims + flag off.
5. **Dependencia de Stripe**: mitigada con `IBillingGateway`.

## 6. Siguiente paso

Dispatch inmediato de los hijos con estas decisiones (ya comentadas en cada tarjeta): t_5b46e650 (UX Fase 1), t_c0e8a770 (backend billing Fase 1 con esta contract), t_45c5213f (modelo patrocinados, flag off). Gate manual antes de cualquier trabajo de Connect/Fase 2.

Fuentes: docs.stripe.com/connect/platform-tax-reporting (DAC7 personal services sin umbral; private preview) · taxation-customs.ec.europa.eu DAC7 · Belastingdienst (exclusiones: redirect / fee desconocida) · docs/design/benchmark-informe-final.md (citaciones [17][19][32][34]-[37]).
