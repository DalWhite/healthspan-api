using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace healthspan_api.Migrations
{
    /// <inheritdoc />
    public partial class AddWorkoutDailySessionKey : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "DailySessionKey",
                table: "Workouts",
                type: "varchar(64)",
                maxLength: 64,
                nullable: true)
                .Annotation("MySql:CharSet", "utf8mb4");

            migrationBuilder.CreateIndex(
                name: "IX_Workouts_DailySessionKey",
                table: "Workouts",
                column: "DailySessionKey",
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_Workouts_DailySessionKey",
                table: "Workouts");

            migrationBuilder.DropColumn(
                name: "DailySessionKey",
                table: "Workouts");
        }
    }
}
