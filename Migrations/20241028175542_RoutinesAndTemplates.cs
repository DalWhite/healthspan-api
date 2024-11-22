using System;
using Microsoft.EntityFrameworkCore.Migrations;
using MySql.EntityFrameworkCore.Metadata;

#nullable disable

namespace healthspan_api.Migrations
{
    /// <inheritdoc />
    public partial class RoutinesAndTemplates : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<Guid>(
                name: "RoutineId",
                table: "Workouts",
                type: "char(36)",
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "MuscleGroup",
                table: "ExerciseTypes",
                type: "int",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.AddColumn<int>(
                name: "WorkoutTemplateId",
                table: "ExerciseTypes",
                type: "int",
                nullable: true);

            migrationBuilder.CreateTable(
                name: "Routine",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "char(36)", nullable: false),
                    Description = table.Column<string>(type: "longtext", nullable: true),
                    UserId = table.Column<string>(type: "varchar(255)", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Routine", x => x.Id);
                    table.ForeignKey(
                        name: "FK_Routine_AspNetUsers_UserId",
                        column: x => x.UserId,
                        principalTable: "AspNetUsers",
                        principalColumn: "Id");
                })
                .Annotation("MySQL:Charset", "utf8mb4");

            migrationBuilder.CreateTable(
                name: "WorkoutTemplate",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("MySQL:ValueGenerationStrategy", MySQLValueGenerationStrategy.IdentityColumn),
                    Type = table.Column<string>(type: "longtext", nullable: true),
                    Frequency = table.Column<int>(type: "int", nullable: false),
                    RoutineId = table.Column<Guid>(type: "char(36)", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_WorkoutTemplate", x => x.Id);
                    table.ForeignKey(
                        name: "FK_WorkoutTemplate_Routine_RoutineId",
                        column: x => x.RoutineId,
                        principalTable: "Routine",
                        principalColumn: "Id");
                })
                .Annotation("MySQL:Charset", "utf8mb4");

            migrationBuilder.CreateIndex(
                name: "IX_Workouts_RoutineId",
                table: "Workouts",
                column: "RoutineId");

            migrationBuilder.CreateIndex(
                name: "IX_ExerciseTypes_WorkoutTemplateId",
                table: "ExerciseTypes",
                column: "WorkoutTemplateId");

            migrationBuilder.CreateIndex(
                name: "IX_Routine_UserId",
                table: "Routine",
                column: "UserId");

            migrationBuilder.CreateIndex(
                name: "IX_WorkoutTemplate_RoutineId",
                table: "WorkoutTemplate",
                column: "RoutineId");

            migrationBuilder.AddForeignKey(
                name: "FK_ExerciseTypes_WorkoutTemplate_WorkoutTemplateId",
                table: "ExerciseTypes",
                column: "WorkoutTemplateId",
                principalTable: "WorkoutTemplate",
                principalColumn: "Id");

            migrationBuilder.AddForeignKey(
                name: "FK_Workouts_Routine_RoutineId",
                table: "Workouts",
                column: "RoutineId",
                principalTable: "Routine",
                principalColumn: "Id");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_ExerciseTypes_WorkoutTemplate_WorkoutTemplateId",
                table: "ExerciseTypes");

            migrationBuilder.DropForeignKey(
                name: "FK_Workouts_Routine_RoutineId",
                table: "Workouts");

            migrationBuilder.DropTable(
                name: "WorkoutTemplate");

            migrationBuilder.DropTable(
                name: "Routine");

            migrationBuilder.DropIndex(
                name: "IX_Workouts_RoutineId",
                table: "Workouts");

            migrationBuilder.DropIndex(
                name: "IX_ExerciseTypes_WorkoutTemplateId",
                table: "ExerciseTypes");

            migrationBuilder.DropColumn(
                name: "RoutineId",
                table: "Workouts");

            migrationBuilder.DropColumn(
                name: "MuscleGroup",
                table: "ExerciseTypes");

            migrationBuilder.DropColumn(
                name: "WorkoutTemplateId",
                table: "ExerciseTypes");
        }
    }
}
