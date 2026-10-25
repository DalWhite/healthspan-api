#nullable enable

using System;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using System.Threading.Tasks;
using healthspan_api.Options;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;

namespace Services;

/// <summary>
/// Ensures the QA test account exists with deterministic seed activities
/// (strength + health + mobility, with dynamic per-type metadata fields).
/// Runs ONLY when DevQaSeed.Enabled is true (dev deploy pipeline sets it;
/// the committed default is false, so production can never seed this account).
/// Idempotent: a second run neither duplicates rows nor resurrects data QA
/// mutated during verification — activities are seeded once and left alone.
/// </summary>
public static class DevQaAccountSeeder
{
    private static readonly Guid SeedNamespace =
        Guid.Parse("6f3a4d02-9d4a-4c0a-9b1e-6105e2d00042");

    public static async Task UpsertAsync(
        MainDbContext db,
        UserManager<User> userManager,
        DevQaSeedSettings settings)
    {
        if (settings is null || !settings.Enabled || string.IsNullOrWhiteSpace(settings.Email))
        {
            return;
        }

        if (string.IsNullOrWhiteSpace(settings.Password) || settings.Password.Length < 8)
        {
            throw new InvalidOperationException(
                "DevQaSeed.Enabled is true but DevQaSeed.Password is missing or shorter than 8 characters.");
        }

        var user = await userManager.Users
            .IgnoreQueryFilters()
            .FirstOrDefaultAsync(u => u.NormalizedEmail == settings.Email.ToUpperInvariant());

        if (user is null)
        {
            user = new User
            {
                Id = StableGuid(settings.Email).ToString(),
                UserName = settings.Email,
                Email = settings.Email,
                EmailConfirmed = true,
                Name = "QA Dev Seed",
            };

            var createResult = await userManager.CreateAsync(user, settings.Password);
            if (!createResult.Succeeded)
            {
                throw new InvalidOperationException(
                    $"Dev QA seed user creation failed: {string.Join("; ", createResult.Errors.Select(e => e.Description))}");
            }
        }

        foreach (var seed in SeedActivities(user.Id))
        {
            var exists = await db.WellnessActivities
                .IgnoreQueryFilters()
                .AnyAsync(a => a.Id == seed.Id);

            if (!exists)
            {
                db.WellnessActivities.Add(seed);
            }
        }

        await db.SaveChangesAsync();
    }

    private static WellnessActivity[] SeedActivities(string userId)
    {
        return new[]
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
                Metadata = new System.Collections.Generic.Dictionary<string, object?>
                {
                    ["exercises"] = new[]
                    {
                        new System.Collections.Generic.Dictionary<string, object?>
                        {
                            ["name"] = "Sentadilla",
                            ["sets"] = 4,
                            ["reps"] = 8,
                            ["weightKg"] = 80.5,
                        },
                        new System.Collections.Generic.Dictionary<string, object?>
                        {
                            ["name"] = "Peso muerto",
                            ["sets"] = 3,
                            ["reps"] = 5,
                            ["weightKg"] = 110,
                        },
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
                Metadata = new System.Collections.Generic.Dictionary<string, object?>
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
                Metadata = new System.Collections.Generic.Dictionary<string, object?>
                {
                    ["focus_areas"] = new[] { "cadera", "hombro", "columna" },
                    ["pain_free"] = true,
                    ["range_improvement_deg"] = 12,
                },
            },
        };
    }

    /// <summary>Deterministic v5-style UUID so reseeding is stable across restarts.</summary>
    private static Guid StableGuid(string seed)
    {
        using var hmac = new HMACSHA1(SeedNamespace.ToByteArray());
        var hash = hmac.ComputeHash(Encoding.UTF8.GetBytes(seed));
        var bytes = new byte[16];
        Array.Copy(hash, bytes, 16);
        bytes[6] = (byte)((bytes[6] & 0x0F) | 0x50); // version 5
        bytes[8] = (byte)((bytes[8] & 0x3F) | 0x80); // RFC 4122 variant
        return new Guid(bytes);
    }
}
