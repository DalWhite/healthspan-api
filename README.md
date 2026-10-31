# Healthspan API

Backend for **Healthspan**, a self-hosted personal training & health-tracking platform. It powers the [Healthspan web app](https://github.com/DalWhite/healthspan-fe) and exposes a REST API for workouts, an exercise catalogue with an offline-first mirror, blood-test analysis (OCR + marker trends), running assessments, readiness and billing.

**Who it's for:** fitness-oriented people who want their training and health data on their own infrastructure, and developers building their own coach/tracking product on top of a ready-made domain model.

## What it does

- **Auth** — email/password and Google sign-in (PKCE), JWT access + refresh tokens.
- **Training** — workouts, exercise library (~1,000 seeded exercise types), routines, trainer/trainee relationships, CSV workout import, and a sync endpoint that feeds the PWA's offline SQLite mirror.
- **Health analytics** — blood-test uploads with OCR extraction (Azure Document Intelligence) and optional LLM marker normalization; marker history and trends; Galpin running-adaptation assessments; readiness and wellness activities.
- **Billing** — Stripe Checkout + webhooks (optional, disabled unless configured).
- Uploaded documents are archived to Azure Blob Storage or, when disabled, the local filesystem.

## Built with

- .NET 9 / ASP.NET Core MVC + ASP.NET Core Identity
- Entity Framework Core + MySQL/MariaDB (code-first migrations)
- Azure Document Intelligence (optional OCR), OpenRouter (optional LLM post-processing), Stripe (optional billing)
- xUnit test suite under `Tests/`

## Running it locally

1. Start a database (MariaDB 11 is fine):

   ```bash
   docker run -e "MARIADB_ROOT_PASSWORD=***" -e "MARIADB_DATABASE=HealthspanDb" \
     -p 3306:3306 --name healthspan-db -d mariadb:11
   ```

2. Configure settings. `appsettings.json` ships with empty placeholders; real values go into user-secrets or environment variables (see [.env.example](.env.example) for the full list):

   ```bash
   dotnet user-secrets set "DbConnectionString" "Server=localhost;Database=HealthspanDb;User ID=root;Password=***"
   ```

   A `DevQaSeed` block seeds a `qa@dev.local` login for local testing; it is `Enabled: false` in committed config and only activates inside Development when you set a local password yourself.

3. Apply migrations and run:

   ```bash
   dotnet run    # https://localhost:5001
   ```

4. Run the tests:

   ```bash
   dotnet test Tests/healthspan-api.tests
   ```

## Key endpoints

| Area | Endpoints |
|---|---|
| Auth | `POST /login`, `POST /register`, `POST /refresh`, `GET /auth/google/challenge`, `POST /auth/google/exchange`, `POST /Logout` |
| Training | `/Workouts`, `/Exercises`, `/ExerciseTypes`, `/Routines`, `/Trainers`, `/Data` (CSV import), `/Sync` (offline mirror) |
| Health | `/BloodTests` (upload / manual / review / markers / trends), `/GalpinAssessments` (catalog / latest / trends), `/Dashboard/summary`, `/Readiness`, `/Activities` |
| Billing | `/Billing` (Stripe Checkout sessions + webhook) |

### Registration allowlist (deployed instances)

`Auth:RegistrationMode` gates new-account creation on `POST /register` and the Google
exchange/callback: `Allowlist` (the default in `appsettings.json`) only creates accounts for
emails in `Auth:AllowedAccounts` — anything else gets a `403` ProblemDetails with
`code=registration_closed`. The default is fail-safe: a missing or misspelled mode is treated
as `Allowlist`. Local development stays `Open` (`appsettings.Development.json`); a dev App
Service can opt out via the app-setting `Auth:RegistrationMode=Open`. Login for existing
accounts is never gated.

## Layout

| Path | What lives there |
|---|---|
| `Controllers/` | REST endpoints |
| `Offline/` | offline catalogue mirror + cache handler used by the PWA |
| `Migrations/` | EF Core migrations |
| `docs/` | design notes and decision records |

## Roadmap

See [ROADMAP.md](ROADMAP.md).

## Privacy & eligibility

**Whose data does it process?** Only that of the person operating the instance. This is a
self-hosted, personal-use platform: deployed instances ship with public registration
closed (`Auth:RegistrationMode=Allowlist`, the fail-safe default), so only the operator's
allowlisted emails can ever have an account. The operator is the sole data subject.

**Third-party processors are opt-in.** OCR (Azure Document Intelligence), LLM marker
normalization (OpenRouter) and billing (Stripe) stay disabled unless the operator
explicitly configures credentials. Uploaded health documents go to the operator's own
storage (Azure Blob or the local filesystem).

**Nothing sensitive is committed.** No production database, real user data, or live
secrets are in this repository; seed/sample data (`qa@dev.local`) is synthetic and
disabled in committed config. Credentials live in user-secrets or environment variables.

**Eligibility.** The project is personally and solely owned by its author (every commit
is authored by the author, assisted by their own AI tooling) — no employer, client,
university, or company owns any part of it, and nothing here is under NDA. It is open
source under GPL-3.0, an OSI-approved license. Because access is restricted to the
operator's own allowlisted accounts, the project does not process other people's
personal data.

## License

GPL-3.0 — see [LICENSE](LICENSE).
