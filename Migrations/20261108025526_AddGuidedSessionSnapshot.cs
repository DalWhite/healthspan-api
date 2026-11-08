using System;
using Microsoft.EntityFrameworkCore.Metadata;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace healthspan_api.Migrations
{
    /// <inheritdoc />
    public partial class AddGuidedSessionSnapshot : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            // NOTE: the scaffolded diff also re-created Billing/Plans/
            // Subscriptions/SponsoredOffers/WellnessActivities tables. Those
            // already exist via the October migrations (AddBillingSupport,
            // AddWellnessActivities, AddSponsoredOffers, ...) but the snapshot
            // of the then-latest migration (20261024075242_AddImportJobRecords.
            // Designer.cs) was scaffolded from a pre-billing branch and is
            // missing them, so `dotnet ef migrations add` diffed against an
            // incomplete model. Up/Down are hand-reduced to the only real
            // change; the regenerated Designer snapshot now carries the full
            // model, healing the migration chain for future scaffolds.
            migrationBuilder.AddColumn<string>(
                name: "GuidedSnapshotJson",
                table: "Workouts",
                type: "longtext",
                nullable: true)
                .Annotation("MySql:CharSet", "utf8mb4");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "GuidedSnapshotJson",
                table: "Workouts");
        }
    }
}
