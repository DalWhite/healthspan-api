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

## Layout

| Path | What lives there |
|---|---|
| `Controllers/` | REST endpoints |
| `Offline/` | offline catalogue mirror + cache handler used by the PWA |
| `Migrations/` | EF Core migrations |
| `docs/` | design notes and decision records |

## Roadmap

See [ROADMAP.md](ROADMAP.md).

## License

GPL-3.0 — see [LICENSE](LICENSE).
