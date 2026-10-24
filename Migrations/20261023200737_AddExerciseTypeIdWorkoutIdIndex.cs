using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace healthspan_api.Migrations
{
    /// <inheritdoc />
    public partial class AddExerciseTypeIdWorkoutIdIndex : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            // Create the composite index first: MySQL refuses to drop
            // IX_Exercises_TypeId (FK support) until the composite index,
            // whose TypeId prefix covers the FK, exists.
            migrationBuilder.CreateIndex(
                name: "IX_Exercises_TypeId_WorkoutId",
                table: "Exercises",
                columns: new[] { "TypeId", "WorkoutId" });

            migrationBuilder.DropIndex(
                name: "IX_Exercises_TypeId",
                table: "Exercises");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateIndex(
                name: "IX_Exercises_TypeId",
                table: "Exercises",
                column: "TypeId");

            migrationBuilder.DropIndex(
                name: "IX_Exercises_TypeId_WorkoutId",
                table: "Exercises");
        }
    }
}
