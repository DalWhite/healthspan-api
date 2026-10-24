using System;
using Microsoft.EntityFrameworkCore.Metadata;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace healthspan_api.Migrations
{
    /// <inheritdoc />
    public partial class AddSubscriptionRefundedFlag : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            // NOTE (t_633ebd61): the originally generated version of this
            // migration re-created the entire billing model (BillingEvents,
            // CheckoutSessionRequests, Plans, TrainerPaymentLinks,
            // Subscriptions) because it was scaffolded from a stale designer
            // after the billing merge. Those tables already come from
            // AddBillingSupport/AddSponsoredOffers, so a fresh database
            // crashed with "Table 'BillingEvents' already exists". The only
            // real model diff in this migration is the Refunded flag on
            // Subscriptions.
            migrationBuilder.AddColumn<bool>(
                name: "Refunded",
                table: "Subscriptions",
                type: "tinyint(1)",
                nullable: false,
                defaultValue: false);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "Refunded",
                table: "Subscriptions");
        }
    }
}
