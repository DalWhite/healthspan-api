using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace healthspan_api.Migrations
{
    /// <inheritdoc />
    public partial class FixWorkoutTemplateToExTypeRelation : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_ExerciseTypes_WorkoutTemplate_WorkoutTemplateId",
                table: "ExerciseTypes");

            migrationBuilder.DropIndex(
                name: "IX_ExerciseTypes_WorkoutTemplateId",
                table: "ExerciseTypes");

            migrationBuilder.DropColumn(
                name: "WorkoutTemplateId",
                table: "ExerciseTypes");

            migrationBuilder.CreateTable(
                name: "ExerciseTypeWorkoutTemplate",
                columns: table => new
                {
                    ExerciseTypesId = table.Column<int>(type: "int", nullable: false),
                    WorkoutTemplatesId = table.Column<int>(type: "int", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ExerciseTypeWorkoutTemplate", x => new { x.ExerciseTypesId, x.WorkoutTemplatesId });
                    table.ForeignKey(
                        name: "FK_ExerciseTypeWorkoutTemplate_ExerciseTypes_ExerciseTypesId",
                        column: x => x.ExerciseTypesId,
                        principalTable: "ExerciseTypes",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_ExerciseTypeWorkoutTemplate_WorkoutTemplate_WorkoutTemplates~",
                        column: x => x.WorkoutTemplatesId,
                        principalTable: "WorkoutTemplate",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                })
                .Annotation("MySQL:Charset", "utf8mb4");

            migrationBuilder.CreateIndex(
                name: "IX_ExerciseTypeWorkoutTemplate_WorkoutTemplatesId",
                table: "ExerciseTypeWorkoutTemplate",
                column: "WorkoutTemplatesId");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "ExerciseTypeWorkoutTemplate");

            migrationBuilder.AddColumn<int>(
                name: "WorkoutTemplateId",
                table: "ExerciseTypes",
                type: "int",
                nullable: true);

            migrationBuilder.CreateIndex(
                name: "IX_ExerciseTypes_WorkoutTemplateId",
                table: "ExerciseTypes",
                column: "WorkoutTemplateId");

            migrationBuilder.AddForeignKey(
                name: "FK_ExerciseTypes_WorkoutTemplate_WorkoutTemplateId",
                table: "ExerciseTypes",
                column: "WorkoutTemplateId",
                principalTable: "WorkoutTemplate",
                principalColumn: "Id");
        }
    }
}
