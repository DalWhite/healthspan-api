using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace healthspan_api.Migrations
{
    /// <inheritdoc />
    public partial class SecondMigration : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_Exercises_Workouts_WorkoutId1",
                table: "Exercises");

            migrationBuilder.DropIndex(
                name: "IX_Exercises_WorkoutId1",
                table: "Exercises");

            migrationBuilder.DropColumn(
                name: "WorkoutId1",
                table: "Exercises");

            migrationBuilder.AlterColumn<Guid>(
                name: "WorkoutId",
                table: "Exercises",
                type: "char(36)",
                nullable: true,
                oldClrType: typeof(int),
                oldType: "int",
                oldNullable: true);

            migrationBuilder.CreateIndex(
                name: "IX_Exercises_WorkoutId",
                table: "Exercises",
                column: "WorkoutId");

            migrationBuilder.AddForeignKey(
                name: "FK_Exercises_Workouts_WorkoutId",
                table: "Exercises",
                column: "WorkoutId",
                principalTable: "Workouts",
                principalColumn: "Id");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_Exercises_Workouts_WorkoutId",
                table: "Exercises");

            migrationBuilder.DropIndex(
                name: "IX_Exercises_WorkoutId",
                table: "Exercises");

            migrationBuilder.AlterColumn<int>(
                name: "WorkoutId",
                table: "Exercises",
                type: "int",
                nullable: true,
                oldClrType: typeof(Guid),
                oldType: "char(36)",
                oldNullable: true);

            migrationBuilder.AddColumn<Guid>(
                name: "WorkoutId1",
                table: "Exercises",
                type: "char(36)",
                nullable: true);

            migrationBuilder.CreateIndex(
                name: "IX_Exercises_WorkoutId1",
                table: "Exercises",
                column: "WorkoutId1");

            migrationBuilder.AddForeignKey(
                name: "FK_Exercises_Workouts_WorkoutId1",
                table: "Exercises",
                column: "WorkoutId1",
                principalTable: "Workouts",
                principalColumn: "Id");
        }
    }
}
