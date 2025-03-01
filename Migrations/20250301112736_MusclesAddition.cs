using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace healthspan_api.Migrations
{
    /// <inheritdoc />
    public partial class MusclesAddition : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.RenameColumn(
                name: "Frequency",
                table: "WorkoutTemplate",
                newName: "DaysOfWeek");

            migrationBuilder.AddColumn<string>(
                name: "Goal",
                table: "WorkoutTemplate",
                type: "longtext",
                nullable: true);

            migrationBuilder.AddColumn<DateTime>(
                name: "CompletionDate",
                table: "Routines",
                type: "datetime(6)",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "Status",
                table: "Routines",
                type: "longtext",
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "DifficultyLevel",
                table: "ExerciseTypes",
                type: "int",
                nullable: false,
                defaultValue: 0);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "Goal",
                table: "WorkoutTemplate");

            migrationBuilder.DropColumn(
                name: "CompletionDate",
                table: "Routines");

            migrationBuilder.DropColumn(
                name: "Status",
                table: "Routines");

            migrationBuilder.DropColumn(
                name: "DifficultyLevel",
                table: "ExerciseTypes");

            migrationBuilder.RenameColumn(
                name: "DaysOfWeek",
                table: "WorkoutTemplate",
                newName: "Frequency");
        }
    }
}
