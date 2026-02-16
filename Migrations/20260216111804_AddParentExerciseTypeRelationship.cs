using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace healthspan_api.Migrations
{
    /// <inheritdoc />
    public partial class AddParentExerciseTypeRelationship : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<int>(
                name: "ParentExerciseTypeId",
                table: "ExerciseTypes",
                type: "int",
                nullable: true);

            migrationBuilder.CreateIndex(
                name: "IX_ExerciseTypes_ParentExerciseTypeId",
                table: "ExerciseTypes",
                column: "ParentExerciseTypeId");

            migrationBuilder.AddForeignKey(
                name: "FK_ExerciseTypes_ExerciseTypes_ParentExerciseTypeId",
                table: "ExerciseTypes",
                column: "ParentExerciseTypeId",
                principalTable: "ExerciseTypes",
                principalColumn: "Id",
                onDelete: ReferentialAction.Restrict);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_ExerciseTypes_ExerciseTypes_ParentExerciseTypeId",
                table: "ExerciseTypes");

            migrationBuilder.DropIndex(
                name: "IX_ExerciseTypes_ParentExerciseTypeId",
                table: "ExerciseTypes");

            migrationBuilder.DropColumn(
                name: "ParentExerciseTypeId",
                table: "ExerciseTypes");
        }
    }
}
