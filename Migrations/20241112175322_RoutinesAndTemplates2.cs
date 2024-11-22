using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace healthspan_api.Migrations
{
    /// <inheritdoc />
    public partial class RoutinesAndTemplates2 : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_WorkoutTemplate_Routine_RoutineId",
                table: "WorkoutTemplate");

            migrationBuilder.DropIndex(
                name: "IX_WorkoutTemplate_RoutineId",
                table: "WorkoutTemplate");

            migrationBuilder.DropColumn(
                name: "RoutineId",
                table: "WorkoutTemplate");

            migrationBuilder.CreateTable(
                name: "RoutineWorkoutTemplate",
                columns: table => new
                {
                    RoutinesId = table.Column<Guid>(type: "char(36)", nullable: false),
                    WorkoutTemplatesId = table.Column<int>(type: "int", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_RoutineWorkoutTemplate", x => new { x.RoutinesId, x.WorkoutTemplatesId });
                    table.ForeignKey(
                        name: "FK_RoutineWorkoutTemplate_Routine_RoutinesId",
                        column: x => x.RoutinesId,
                        principalTable: "Routine",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_RoutineWorkoutTemplate_WorkoutTemplate_WorkoutTemplatesId",
                        column: x => x.WorkoutTemplatesId,
                        principalTable: "WorkoutTemplate",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                })
                .Annotation("MySQL:Charset", "utf8mb4");

            migrationBuilder.CreateIndex(
                name: "IX_RoutineWorkoutTemplate_WorkoutTemplatesId",
                table: "RoutineWorkoutTemplate",
                column: "WorkoutTemplatesId");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "RoutineWorkoutTemplate");

            migrationBuilder.AddColumn<Guid>(
                name: "RoutineId",
                table: "WorkoutTemplate",
                type: "char(36)",
                nullable: true);

            migrationBuilder.CreateIndex(
                name: "IX_WorkoutTemplate_RoutineId",
                table: "WorkoutTemplate",
                column: "RoutineId");

            migrationBuilder.AddForeignKey(
                name: "FK_WorkoutTemplate_Routine_RoutineId",
                table: "WorkoutTemplate",
                column: "RoutineId",
                principalTable: "Routine",
                principalColumn: "Id");
        }
    }
}
