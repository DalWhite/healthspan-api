using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace healthspan_api.Migrations
{
    /// <inheritdoc />
    public partial class AddExerciseBodyweightBreakdown : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<double>(
                name: "AddedWeight",
                table: "Exercises",
                type: "double",
                nullable: true);

            migrationBuilder.AddColumn<double>(
                name: "BodyWeight",
                table: "Exercises",
                type: "double",
                nullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "AddedWeight",
                table: "Exercises");

            migrationBuilder.DropColumn(
                name: "BodyWeight",
                table: "Exercises");
        }
    }
}
