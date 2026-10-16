using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace healthspan_api.Migrations
{
    /// <inheritdoc />
    public partial class AddOfflineSyncSupport : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<Guid>(
                name: "ClientEntityId",
                table: "Workouts",
                type: "char(36)",
                nullable: true,
                collation: "ascii_general_ci");

            migrationBuilder.AddColumn<Guid>(
                name: "ClientEntityId",
                table: "Exercises",
                type: "char(36)",
                nullable: true,
                collation: "ascii_general_ci");

            migrationBuilder.CreateTable(
                name: "SyncOperations",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "char(36)", nullable: false, collation: "ascii_general_ci"),
                    UserId = table.Column<string>(type: "varchar(255)", nullable: true)
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    OpId = table.Column<Guid>(type: "char(36)", nullable: false, collation: "ascii_general_ci"),
                    Entity = table.Column<string>(type: "longtext", nullable: true)
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    Action = table.Column<string>(type: "longtext", nullable: true)
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    ClientEntityId = table.Column<Guid>(type: "char(36)", nullable: true, collation: "ascii_general_ci"),
                    ServerEntityId = table.Column<Guid>(type: "char(36)", nullable: true, collation: "ascii_general_ci"),
                    AppliedAtUtc = table.Column<DateTime>(type: "datetime(6)", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_SyncOperations", x => x.Id);
                })
                .Annotation("MySql:CharSet", "utf8mb4");

            migrationBuilder.CreateIndex(
                name: "IX_Workouts_ClientEntityId",
                table: "Workouts",
                column: "ClientEntityId",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_Exercises_ClientEntityId",
                table: "Exercises",
                column: "ClientEntityId",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_SyncOperations_UserId_OpId",
                table: "SyncOperations",
                columns: new[] { "UserId", "OpId" },
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "SyncOperations");

            migrationBuilder.DropIndex(
                name: "IX_Workouts_ClientEntityId",
                table: "Workouts");

            migrationBuilder.DropIndex(
                name: "IX_Exercises_ClientEntityId",
                table: "Exercises");

            migrationBuilder.DropColumn(
                name: "ClientEntityId",
                table: "Workouts");

            migrationBuilder.DropColumn(
                name: "ClientEntityId",
                table: "Exercises");
        }
    }
}
