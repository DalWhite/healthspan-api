#nullable enable

using Microsoft.EntityFrameworkCore;

namespace Services;

/// <summary>
/// Ensures the two Healthspan Premium plans exist. Prices here are display
/// metadata; the charged amount is defined by the Stripe price configured for
/// each plan slug.
/// </summary>
public static class BillingPlanSeeder
{
    public static async Task UpsertAsync(MainDbContext db)
    {
        var existing = await db.Plans.ToListAsync();

        Upsert(existing, db, new Plan
        {
            Slug = "premium-monthly",
            DisplayName = "Healthspan Premium mensual",
            PriceCents = 999,
            Currency = "eur",
            Interval = BillingInterval.Monthly,
            Active = true,
        });

        Upsert(existing, db, new Plan
        {
            Slug = "premium-annual",
            DisplayName = "Healthspan Premium anual",
            PriceCents = 8999,
            Currency = "eur",
            Interval = BillingInterval.Annual,
            Active = true,
        });

        await db.SaveChangesAsync();
    }

    private static void Upsert(List<Plan> existing, MainDbContext db, Plan plan)
    {
        var current = existing.FirstOrDefault(p => p.Slug == plan.Slug);

        if (current is null)
        {
            db.Plans.Add(plan);
            return;
        }

        current.DisplayName = plan.DisplayName;
        current.PriceCents = plan.PriceCents;
        current.Currency = plan.Currency;
        current.Interval = plan.Interval;
        current.Active = plan.Active;
    }
}
