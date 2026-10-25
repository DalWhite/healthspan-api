#nullable enable

// One-shot QA seed runner (t_ebb6967c) — writes directly into the dev MySQL.
// Usage: DbConnectionString='...' dotnet run --project Tools/QaSeedTool -- <email> <password>
// NOT wired into the web app: the DevQaSeed config gate of the app is
// untouched (Enabled defaults false, prod impossible). This tool performs the
// same Identity writes (PBKDF2 hash via PasswordHasher) that POST /login
// verifies. Password is passed on the CLI, never stored in the repo.

using System.Security.Cryptography;
using System.Text;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

if (args.Length != 2)
{
    Console.Error.WriteLine("usage: <email> <password>  (env DbConnectionString required)");
    return 2;
}

var email = args[0];
var password = args[1];
var dbConnectionString = Environment.GetEnvironmentVariable("DbConnectionString");
if (string.IsNullOrWhiteSpace(dbConnectionString))
{
    Console.Error.WriteLine("DbConnectionString env var is required");
    return 2;
}

var services = new ServiceCollection();
services.AddDataProtection();
services.AddHttpContextAccessor();
services.AddSingleton<Services.CurrentUserProvider>();
services.AddDbContext<MainDbContext>(options =>
    options.UseMySql(dbConnectionString, ServerVersion.AutoDetect(dbConnectionString)));

var provider = services.BuildServiceProvider();
await using var scope = provider.CreateAsyncScope();
var db = scope.ServiceProvider.GetRequiredService<MainDbContext>();

var hasher = new PasswordHasher<User>();

var user = await db.Users.IgnoreQueryFilters()
    .FirstOrDefaultAsync(u => u.NormalizedEmail == email.ToUpperInvariant());

if (user is null)
{
    user = new User
    {
        Id = StableGuid(email).ToString(),
        UserName = email,
        NormalizedUserName = email.ToUpperInvariant(),
        Email = email,
        NormalizedEmail = email.ToUpperInvariant(),
        EmailConfirmed = true,
        Name = "QA Dev Seed",
        SecurityStamp = Guid.NewGuid().ToString("N"),
        ConcurrencyStamp = Guid.NewGuid().ToString("N"),
    };
    user.PasswordHash = hasher.HashPassword(user, password);
    db.Users.Add(user);
    await db.SaveChangesAsync();
    Console.WriteLine($"CREATED user={email} id={user.Id}");
}
else
{
    // Reset the password to the requested one (idempotent reseeding).
    user.PasswordHash = hasher.HashPassword(user, password);
    user.SecurityStamp = Guid.NewGuid().ToString("N");
    await db.SaveChangesAsync();
    Console.WriteLine($"RESET user={email} id={user.Id}");
}

foreach (var seed in SeedActivities(user.Id))
{
    if (!await db.WellnessActivities.IgnoreQueryFilters().AnyAsync(a => a.Id == seed.Id))
    {
        db.WellnessActivities.Add(seed);
    }
}
await db.SaveChangesAsync();

var rows = await db.WellnessActivities.IgnoreQueryFilters()
    .Where(a => a.UserId == user.Id).ToListAsync();
Console.WriteLine($"SEEDED activities={rows.Count}");
foreach (var a in rows)
{
    Console.WriteLine($"  {a.Dimension} type={a.Type} id={a.Id} metadataKeys={string.Join(",", (a.Metadata ?? new()).Keys)}");
}
return 0;

static WellnessActivity[] SeedActivities(string userId) => new[]
{
    new WellnessActivity
    {
        Id = StableGuid($"{userId}:strength"),
        UserId = userId,
        Type = "strength_session",
        Dimension = WellnessDimension.Strength,
        OccurredOnUtc = new DateTime(2026, 10, 19, 0, 0, 0, DateTimeKind.Utc),
        DurationMinutes = 55,
        Intensity = 7.5m,
        Notes = "QA seed: fuerza",
        Metadata = new Dictionary<string, object?>
        {
            ["exercises"] = new[]
            {
                new Dictionary<string, object?> { ["name"] = "Sentadilla", ["sets"] = 4, ["reps"] = 8, ["weightKg"] = 80.5 },
                new Dictionary<string, object?> { ["name"] = "Peso muerto", ["sets"] = 3, ["reps"] = 5, ["weightKg"] = 110 },
            },
            ["rpe"] = 8,
            ["rest_seconds"] = 120,
        },
    },
    new WellnessActivity
    {
        Id = StableGuid($"{userId}:health"),
        UserId = userId,
        Type = "sauna",
        Dimension = WellnessDimension.Health,
        OccurredOnUtc = new DateTime(2026, 10, 20, 0, 0, 0, DateTimeKind.Utc),
        DurationMinutes = 20,
        Intensity = 6m,
        TemperatureCelsius = 85.5m,
        Cycles = 3,
        Notes = "QA seed: salud",
        Metadata = new Dictionary<string, object?>
        {
            ["humidity_percent"] = 15,
            ["cool_down_minutes"] = 10,
            ["heart_rate_avg"] = 128,
        },
    },
    new WellnessActivity
    {
        Id = StableGuid($"{userId}:mobility"),
        UserId = userId,
        Type = "mobility_flow",
        Dimension = WellnessDimension.Mobility,
        OccurredOnUtc = new DateTime(2026, 10, 21, 0, 0, 0, DateTimeKind.Utc),
        DurationMinutes = 30,
        FrequencyPerWeek = 3m,
        Notes = "QA seed: movilidad",
        Metadata = new Dictionary<string, object?>
        {
            ["focus_areas"] = new[] { "cadera", "hombro", "columna" },
            ["pain_free"] = true,
            ["range_improvement_deg"] = 12,
        },
    },
};

static Guid StableGuid(string seed)
{
    using var hmac = new HMACSHA1(Guid.Parse("6f3a4d02-9d4a-4c0a-9b1e-6105e2d00042").ToByteArray());
    var hash = hmac.ComputeHash(Encoding.UTF8.GetBytes(seed));
    var bytes = new byte[16];
    Array.Copy(hash, bytes, 16);
    bytes[6] = (byte)((bytes[6] & 0x0F) | 0x50);
    bytes[8] = (byte)((bytes[8] & 0x3F) | 0x80);
    return new Guid(bytes);
}
