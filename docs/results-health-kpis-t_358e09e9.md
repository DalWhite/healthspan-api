# Spec: KPIs de resultados de salud en la vista coach↔trainee

Task t_358e09e9 · Owner: @pau · Status: DECIDIDO (implementadores: t_4882e760 API, t_33c412cf UX; QA t_268ca483)
Sembrarra este doc en `healthspan-api/docs/results-health-kpis-t_358e09e9.md` en el PR del API task.

## 1. Purpose (por qué, argumento de retención)

Hoy la vista coach↔trainee (`/trainees/{id}/exercises`) muestra **logs de actividad**: el coach verificacióna que el trainee registró, y that all the product demonstrates. The WHOOP↔Abu Dhabi signal (21-oct-2026) supports the direction: continuous data + coaching → demonstrable health results. This spec turns "registró" into 3 monthly numbers that make the value of the coach visible: **consistency of sessions, registered weight/body-composition trend, and strength progress in basics**. Product rule: only KPIs **of outcomes** with neutral visuals — no medical interpretation, no promises.

## 2. KPIs — exact definitions

Shared convention: **month = UTC calendar month** `YYYY-MM`. Week for consistency: Monday-start. Everything deterministic and pure: same DB state ⇒ same response.

### K1 · consistency_sessions — Consistency of sessions (0–1)

- **Input**: `Workouts` (trainee's), completed session = `DailySessionKey` (dedup of the day) with at least one session **completed**. Completed = `CompletedAt != null` OR legacy (`CompletedAt == null && Exercises.Count > 0`) — the universal tracker keeps old logs without `CompletedAt` (documented in Workout.cs).
- **Formula** (month M): `active_weeks(M) / elapsed_weeks(M)`; a week counts when it has ≥1 completed session; a week counts as elapsed when it ended or it is the current one. Score with 2 decimals.
- **Secondary fields**: `delta_pp` = score(M) − score(M−1) in percentage points; `active_sessions` (absolute number, shown as chip).
- **States**:
  - `ok` → score 0–1 (0 is a **legitimo zero**: the trainee existed and didn't train; it supports the argument).
  - `not_started` → score `null`: M precedes the first completed session in the user's history (before onboarding). Null is NOT 0.
- **Edge cases**: month in progress → denominator counts only elapsed weeks (no dilution); 1 completed session in month → non-zero score, never `insufficient_data`; DST irrelevant (UTC).

### K2 · weight_trend — Weight trend (registered body composition)

- **New input required**: `User.Weight` is a overwritten scalar (`PATCH /HealthProfile`) — there is NO weight time series. **Decision: new table `BodyCompositionReading`**:
  - `{ Guid Id, string UserId, int HealthMetricDefinitionId (category = BodyComposition), decimal Value, string Source (manual|csv|sync), DateTime OccurredOnUtc, DateTime CreatedAtUtc }`.
  - Extension point for future body composition (weight is the seeded `HealthMetricDefinition`: key `weight_kg`, unit kg, category `BodyComposition`). Do not create a weight-only table.
  - Write: every `PATCH /HealthProfile` that sets `WeightKg` also creates a reading (same Save). CSV/sync import: same write if the metric arrives.
  - Ingest validation: reject `weight_kg` outside [25, 400] (400 validation, explicit; never a silent store).
  - No synthetic seeding: today's `User.Weight` stays as the single `current_value` and the first months legitimately have `state: insufficient_data`.
- **Formulas** (month M, per metric — only `weight_kg` for now):
  - `last_kg` = value of the last reading of M (same month with multiple → most recent `OccurredOnUtc`).
  - `delta_kg` = last(M) − last(last month M′ < M with a reading). GAPS are fine: delta against the previous real observation (product truth: "since the last registration").
  - `state`: `ok` when ≥2 readings in history; `current_only` when exactly 1 (`last_kg` shown, delta `null`); `insufficient_data` when 0.
- **Direction**: signed `delta_kg`, neutral visual (down arrow when negative). NO judgment "good/bad": weight goes down for the cardiometabolic goal in the WHOOP example and not in hypertrophy. The interpretation belongs to the coach, not the app.
- **Privacy (KPI most sensitive)** — see §5: direct trainer only, aggregate only, zero leakage.

### K3 · strength_progress — Progress of strength in basics

- **Set "basics" (decision, not free text)**: `ExerciseTag { Name: "basic", Category: "strength" }` seeded on the 5 canonical types: squat, deadlift, chest press, pull-up, muscle-up. Editable set by design (seeded, not hardcoded): KPIs are computed on tagged types with data, so community additions just work.
- **Formulas**:
  - Per session (reuse existing rule of `ExerciseMetricsService`, Workout.cs pitfall t_0f19c744): best 1RM of the session = max `MaxRepetition` over `WorkoutId`; **rows with `MaxRepetition` null or ≤0 never count** ("a 0 1RM is not a measurement").
  - Per type: `delta(M)` = best1RM(last session of M with data) − best1RM(last prior session of M−1); unit comes from `ExerciseType.WeightType` (kg, reps, …) — `unit` always returned and **never** mixed across types.
  - Relative: `delta_pct(M)` = delta ÷ baseline, baseline = first observed 1RM of that type (>0). First observation without baseline → `ok` with delta and `delta_pct: null`.
  - Aggregate `index_pct` = mean of `delta_pct` over types with `state: ok`. When 0 types qualify → `state: insufficient_data` (null, not 0).
- **States**: `ok` (≥1 type with 2 dated observations), `insufficient_data` (basics without load or single-session records — very common in beginners), `delta: 0` = plateau, legitimate and different from null.
- **Required fix** (acceptance in §7): the existing implementation of `CalculateRMDiferentialInLast3Months` (i) uses local `DateTime.Now` — everything uses `DateTime.UtcNow` (CI runs UTC vs local ⇒ nondeterminism); (ii) returns `0` when it lacks data — in the spec the absence of data is `null`; 0 is reserved for a true plateau.

## 3. API surface (implementation: t_4882e760)

New controller `ResultsController` in the API, `api/results` (OData routes as the rest):

- `GET /api/results/{traineeId}/month?month=YYYY-MM` → single-month snapshot.
- `GET /api/results/{traineeId}/series?months=N` → last N calendar months (default 6, cap 12).
- **Authorization: mandatory `[TraineeAccess]`** (existing `Filters/TraineeAccessFilter.cs`): if `traineeId` is neither `self` nor a direct trainee, 403 via existing `ApiProblemResult`. Same pattern used by `ExercisesController` `{exerciseTypeId}/kpis`.

Response shape (deterministic contract; `nulls` are **explicit** and every KPI always carries `state`):

```json
{
  "traineeId": "…", "generatedAtUtc": "…",
  "month": "2026-09",
  "consistency": { "state": "ok", "score": 0.75, "deltaPp": -10, "activeSessions": 3 },
  "weight":      { "state": "ok", "lastKg": 81.4, "deltaKg": -0.6 },
  "strength":    { "state": "ok", "indexPct": 4.2,
    "byType": [ { "typeId": 12, "key": "squat", "unit": "kg", "state": "ok", "delta": 2.5, "deltaPct": 8.3 } ] }
}
```

- `series`: `{ "months": ["2026-04", …, "2026-09"], "consistency": [...], "weight": [...], "strength": {"indexPct": [...], "byType": {...}} }` with same `state` per point.
- Contract rules: `byType` always ordered by `typeId` asc; month arrays ascending; no raw readings (see §5); KPIs never absent — a key with `state: "insufficient_data"`; numeric types: `double` with 2-decimal rounding, never strings (`decimal→double` regression PR#25).
- Zero ad-hoc client-side calculation: FE consumes the shape above (acceptance of t_4882e760).
- Unit tests for every state (all): deterministic `UtcNow` with `ApiTestBase` + `TestDataSeeder` (pattern of `TraineeAccessFilterTests.cs`, in Authorization/).

## 4. UX acceptance (t_33c412cf, @ux-designer)

- **Entry point**: in `/trainees` add a 4th button "Results" → new route **`/trainees/{id}/results`** (parallel to Exercises/Favourites/Rutinas). Do not touch the rest of the tabs.
- **Three cards** (MudCard, mobile-first at 390px — `healthspan-fe-ux-audit` standard):
  1. Consistency: score as `75%` + month-over-month chip (`↓ 10 pt`) + 6-month mini bars (CSS/SVG, **no new libs** — WASM payload ≤2MB budget, PR#37).
  2. Weight trend: current `81,4 kg` + signed chip (`0,6 kg ↓`) + line of monthly deltas.
  3. Strength: aggregate `+4,2 %` + per-type chips (icon per type + signed delta with unit).
- **A11y (repo standard, already in PR#41)**: meaningful numbers in real text (not only `aria-label`), decorative arrows `aria-hidden="true"`, chip with sign + word (`up/down` or signed `+/-`) so the delta is not color-only (WCAG 1.4.1), `prefers-reduced-motion` on sparklines (same splash pattern of PR t_70bfccd8).
- **All UI Spanish**; copy for `state`s owned by FE from fixed templates (no LLM):
  - `not_started` → "Todavía no completó sessions" (never an invented 0).
  - `insufficient_data` → "Insufficient data" (+ one line of why: "needs at least 2 registered sessions with load").
  - `current_only` → show current value and NO delta.
- **Product line (the card's goal)**: one fixed template that closes the loop, e.g. "In September: 9 sessions, −0,6 kg, +4 % in strength", built by FE from `state`s, never invented prose. The "improve cardiometabolic" interpretation is NOT copied: out of scope.
- Empty trainee (all 3 `not_started`) keeps the cards with the copy above — the visual must not lie.
- Coherence with existing design tokens; no new fonts/images (payload budget).

## 5. Privacy constraints (health data is PII; the app is health-sensitive)

1. **Audience**: only owner (self view) + **direct** trainer via `[TraineeAccess]` (validated relationship `User.Trainer/Trainees` — one level, no transitive chain). No public endpoints.
2. **Granularity**: KPI endpoints return **monthly aggregates**; never expose the `BodyCompositionReading` series, nor individual dates (trainee knows they registered; the coach sees outcomes). If a series is needed later, exact dates without `?months=` (not this PR).
3. **No cross-leakage**: numbers never surface in the `/trainees` list, in any `TrainerPaymentLink`/billing flow, or in search/OG metadata.
4. **Logging**: INFO logs only identifiers and `state`s — **never** values of weight/1RM. `ApiProblemResult` errors don't include data.
5. **Future opt-out (decision, not implementation now)**: extend `AnalyticsPermission` with `CanViewResultsKpis` (default: direct trainer = true); do not touch blood (`CanViewBloodAnalytics`) semantics.
6. **Export**: `Tus datos` (Settings import/export) already exports raw data of the user; KPIs add no new export path.

## 6. Edge cases (checklist for QA t_268ca483)

| # | Case | Expected |
|---|--------|-------------|
| E1 | weight: no readings ever | `state: insufficient_data`, no `lastKg` |
| E2 | weight: single reading (`User.Weight` seeded, no new PATCH) | `state: current_only`, `deltaKg: null` |
| E3 | consistency: 1 session in the month, 0 in month-1 | score > 0, positive `deltaPp` |
| E4 | consistency: user without completed sessions ever | `not_started`/`null` (never 0) |
| E5 | strength: rows with `MaxRepetition: 0/null` mixed | ignored (t_0f19c744 rule); all-zero case ⇒ `insufficient_data` |
| E6 | strength: basics with reps but no load | `delta: null`, `insufficient_data` |
| E7 | first month (in-progress) | elapsed weeks, no dilution |
| E8 | month with `lastKg == prevLastKg` | `deltaKg: 0` **legitimate** ≠ null |
| E9 | 1RM plateaus `delta: 0` | render as plateau ("±0"), not `insufficient_data` |
| E10 | `traineeId` = arbitrary stranger | 403 (TraineeAccessFilter) |
| E11 | non-UTC CI vs local | identical response (all `UtcNow`) |
| E12 | same month called twice (no writes) | byte-identical response (determinism) |

## 7. Global acceptance

**API (t_4882e760)**
- [ ] `[TraineeAccess]` routes with shape of §3; tests cover the 4 `state`s × KPI and E1–E12 as far as reproducible.
- [ ] `BodyCompositionReading` migration + seeding of `weight_kg` (`HealthMetricDefinition`); `PATCH /HealthProfile` writes the reading atomically; ingest validation [25, 400].
- [ ] Zero `DateTime.Now` (only `UtcNow`) and zero `return 0` for "no data" in new code.
- [ ] Deterministic CI: green local `dotnet test` and CI (same pattern as merged PRs).

**UX (t_33c412cf)**
- [ ] Spanish `/trainees/{id}/results`, 3 cards + template line; Spanish `state`s visible (no fake zeros).
- [ ] 390px Playwright audit (skill `healthspan-fe-ux-audit`) without a11y errors; no new payload libs.
- [ ] Consumes only §3 shape; no client-side computations.

**QA (t_268ca483)**
- [ ] Reproduce E1–E12 in deployed dev (seed `qa@dev.local`, skill `healthspan-seed-and-deploy`) with real browser.
- [ ] Approve ONLY if: correct numbers, null/0/insufficient distinguishable visually, aligned with health-result argument (no medical promises).

## 8. Explicit out-of-scope (no feature creep)

No medical/clinical interpretation, no BMI/body metrics beyond seeded `weight_kg`, no weekly KPIs, no cross-trainee rankings/leaderboard, no AI copy, no gamification/points. Rest, not more.
