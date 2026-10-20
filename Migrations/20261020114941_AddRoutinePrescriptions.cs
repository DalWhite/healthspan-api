using System;
using Microsoft.EntityFrameworkCore.Metadata;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace healthspan_api.Migrations
{
    /// <inheritdoc />
    public partial class AddRoutinePrescriptions : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<DateTime>(
                name: "CompletedAt",
                table: "Workouts",
                type: "datetime(6)",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "TrainerId",
                table: "Routines",
                type: "varchar(255)",
                nullable: true)
                .Annotation("MySql:CharSet", "utf8mb4");

            migrationBuilder.AddColumn<int>(
                name: "Version",
                table: "Routines",
                type: "int",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.AddColumn<int>(
                name: "SourceTemplateExerciseId",
                table: "Exercises",
                type: "int",
                nullable: true);

            migrationBuilder.CreateTable(
                name: "WorkoutTemplateExercises",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("MySql:ValueGenerationStrategy", MySqlValueGenerationStrategy.IdentityColumn),
                    WorkoutTemplateId = table.Column<int>(type: "int", nullable: false),
                    ExerciseTypeId = table.Column<int>(type: "int", nullable: false),
                    Order = table.Column<int>(type: "int", nullable: false),
                    Sets = table.Column<int>(type: "int", nullable: true),
                    Reps = table.Column<int>(type: "int", nullable: true),
                    LoadKg = table.Column<double>(type: "double", nullable: true),
                    RestSeconds = table.Column<int>(type: "int", nullable: true),
                    Notes = table.Column<string>(type: "longtext", nullable: true)
                        .Annotation("MySql:CharSet", "utf8mb4")
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_WorkoutTemplateExercises", x => x.Id);
                    table.ForeignKey(
                        name: "FK_WorkoutTemplateExercises_ExerciseTypes_ExerciseTypeId",
                        column: x => x.ExerciseTypeId,
                        principalTable: "ExerciseTypes",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_WorkoutTemplateExercises_WorkoutTemplate_WorkoutTemplateId",
                        column: x => x.WorkoutTemplateId,
                        principalTable: "WorkoutTemplate",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                })
                .Annotation("MySql:CharSet", "utf8mb4");

            // Carry existing template->exercise associations into the new ordered
            // slots before dropping the old implicit join table.
            migrationBuilder.Sql(
                """
                INSERT INTO WorkoutTemplateExercises (WorkoutTemplateId, ExerciseTypeId, `Order`)
                SELECT WorkoutTemplatesId, ExerciseTypesId,
                       ROW_NUMBER() OVER (PARTITION BY WorkoutTemplatesId ORDER BY ExerciseTypesId)
                FROM ExerciseTypeWorkoutTemplate;
                """);

            migrationBuilder.DropTable(
                name: "ExerciseTypeWorkoutTemplate");

            migrationBuilder.CreateIndex(
                name: "IX_Routines_TrainerId",
                table: "Routines",
                column: "TrainerId");

            migrationBuilder.CreateIndex(
                name: "IX_Exercises_SourceTemplateExerciseId",
                table: "Exercises",
                column: "SourceTemplateExerciseId");

            migrationBuilder.CreateIndex(
                name: "IX_WorkoutTemplateExercises_ExerciseTypeId",
                table: "WorkoutTemplateExercises",
                column: "ExerciseTypeId");

            migrationBuilder.CreateIndex(
                name: "IX_WorkoutTemplateExercises_WorkoutTemplateId_Order",
                table: "WorkoutTemplateExercises",
                columns: new[] { "WorkoutTemplateId", "Order" },
                unique: true);

            migrationBuilder.AddForeignKey(
                name: "FK_Exercises_WorkoutTemplateExercises_SourceTemplateExerciseId",
                table: "Exercises",
                column: "SourceTemplateExerciseId",
                principalTable: "WorkoutTemplateExercises",
                principalColumn: "Id",
                onDelete: ReferentialAction.SetNull);

            migrationBuilder.AddForeignKey(
                name: "FK_Routines_AspNetUsers_TrainerId",
                table: "Routines",
                column: "TrainerId",
                principalTable: "AspNetUsers",
                principalColumn: "Id",
                onDelete: ReferentialAction.SetNull);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_Exercises_WorkoutTemplateExercises_SourceTemplateExerciseId",
                table: "Exercises");

            migrationBuilder.DropForeignKey(
                name: "FK_Routines_AspNetUsers_TrainerId",
                table: "Routines");

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
                .Annotation("MySql:CharSet", "utf8mb4");

            migrationBuilder.CreateIndex(
                name: "IX_ExerciseTypeWorkoutTemplate_WorkoutTemplatesId",
                table: "ExerciseTypeWorkoutTemplate",
                column: "WorkoutTemplatesId");

            migrationBuilder.Sql(
                """
                INSERT INTO ExerciseTypeWorkoutTemplate (ExerciseTypesId, WorkoutTemplatesId)
                SELECT ExerciseTypeId, WorkoutTemplateId
                FROM WorkoutTemplateExercises;
                """);

            migrationBuilder.DropTable(
                name: "WorkoutTemplateExercises");

            migrationBuilder.DropIndex(
                name: "IX_Routines_TrainerId",
                table: "Routines");

            // Design decision (routine-prescriptions-design.md §5.2): Down keeps
            // CompletedAt and SourceTemplateExerciseId so logging data is never
            // lost when the feature is rolled back.

            migrationBuilder.DropColumn(
                name: "TrainerId",
                table: "Routines");

            migrationBuilder.DropColumn(
                name: "Version",
                table: "Routines");
        }
    }
}
