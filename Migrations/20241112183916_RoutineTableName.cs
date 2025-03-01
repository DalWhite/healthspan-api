using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace healthspan_api.Migrations
{
    /// <inheritdoc />
    public partial class RoutineTableName : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_ExerciseTypes_Muscle_PrimaryMuscleId",
                table: "ExerciseTypes");

            migrationBuilder.DropForeignKey(
                name: "FK_ExerciseTypes_Muscle_SecondaryMuscleId",
                table: "ExerciseTypes");

            migrationBuilder.DropForeignKey(
                name: "FK_Routine_AspNetUsers_UserId",
                table: "Routine");

            migrationBuilder.DropForeignKey(
                name: "FK_RoutineWorkoutTemplate_Routine_RoutinesId",
                table: "RoutineWorkoutTemplate");

            migrationBuilder.DropForeignKey(
                name: "FK_Workouts_Routine_RoutineId",
                table: "Workouts");

            migrationBuilder.DropPrimaryKey(
                name: "PK_Routine",
                table: "Routine");

            migrationBuilder.DropPrimaryKey(
                name: "PK_Muscle",
                table: "Muscle");

            migrationBuilder.RenameTable(
                name: "Routine",
                newName: "Routines");

            migrationBuilder.RenameTable(
                name: "Muscle",
                newName: "Muscles");

            migrationBuilder.RenameIndex(
                name: "IX_Routine_UserId",
                table: "Routines",
                newName: "IX_Routines_UserId");

            migrationBuilder.AddPrimaryKey(
                name: "PK_Routines",
                table: "Routines",
                column: "Id");

            migrationBuilder.AddPrimaryKey(
                name: "PK_Muscles",
                table: "Muscles",
                column: "Id");

            migrationBuilder.AddForeignKey(
                name: "FK_ExerciseTypes_Muscles_PrimaryMuscleId",
                table: "ExerciseTypes",
                column: "PrimaryMuscleId",
                principalTable: "Muscles",
                principalColumn: "Id");

            migrationBuilder.AddForeignKey(
                name: "FK_ExerciseTypes_Muscles_SecondaryMuscleId",
                table: "ExerciseTypes",
                column: "SecondaryMuscleId",
                principalTable: "Muscles",
                principalColumn: "Id");

            migrationBuilder.AddForeignKey(
                name: "FK_Routines_AspNetUsers_UserId",
                table: "Routines",
                column: "UserId",
                principalTable: "AspNetUsers",
                principalColumn: "Id");

            migrationBuilder.AddForeignKey(
                name: "FK_RoutineWorkoutTemplate_Routines_RoutinesId",
                table: "RoutineWorkoutTemplate",
                column: "RoutinesId",
                principalTable: "Routines",
                principalColumn: "Id",
                onDelete: ReferentialAction.Cascade);

            migrationBuilder.AddForeignKey(
                name: "FK_Workouts_Routines_RoutineId",
                table: "Workouts",
                column: "RoutineId",
                principalTable: "Routines",
                principalColumn: "Id");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_ExerciseTypes_Muscles_PrimaryMuscleId",
                table: "ExerciseTypes");

            migrationBuilder.DropForeignKey(
                name: "FK_ExerciseTypes_Muscles_SecondaryMuscleId",
                table: "ExerciseTypes");

            migrationBuilder.DropForeignKey(
                name: "FK_Routines_AspNetUsers_UserId",
                table: "Routines");

            migrationBuilder.DropForeignKey(
                name: "FK_RoutineWorkoutTemplate_Routines_RoutinesId",
                table: "RoutineWorkoutTemplate");

            migrationBuilder.DropForeignKey(
                name: "FK_Workouts_Routines_RoutineId",
                table: "Workouts");

            migrationBuilder.DropPrimaryKey(
                name: "PK_Routines",
                table: "Routines");

            migrationBuilder.DropPrimaryKey(
                name: "PK_Muscles",
                table: "Muscles");

            migrationBuilder.RenameTable(
                name: "Routines",
                newName: "Routine");

            migrationBuilder.RenameTable(
                name: "Muscles",
                newName: "Muscle");

            migrationBuilder.RenameIndex(
                name: "IX_Routines_UserId",
                table: "Routine",
                newName: "IX_Routine_UserId");

            migrationBuilder.AddPrimaryKey(
                name: "PK_Routine",
                table: "Routine",
                column: "Id");

            migrationBuilder.AddPrimaryKey(
                name: "PK_Muscle",
                table: "Muscle",
                column: "Id");

            migrationBuilder.AddForeignKey(
                name: "FK_ExerciseTypes_Muscle_PrimaryMuscleId",
                table: "ExerciseTypes",
                column: "PrimaryMuscleId",
                principalTable: "Muscle",
                principalColumn: "Id");

            migrationBuilder.AddForeignKey(
                name: "FK_ExerciseTypes_Muscle_SecondaryMuscleId",
                table: "ExerciseTypes",
                column: "SecondaryMuscleId",
                principalTable: "Muscle",
                principalColumn: "Id");

            migrationBuilder.AddForeignKey(
                name: "FK_Routine_AspNetUsers_UserId",
                table: "Routine",
                column: "UserId",
                principalTable: "AspNetUsers",
                principalColumn: "Id");

            migrationBuilder.AddForeignKey(
                name: "FK_RoutineWorkoutTemplate_Routine_RoutinesId",
                table: "RoutineWorkoutTemplate",
                column: "RoutinesId",
                principalTable: "Routine",
                principalColumn: "Id",
                onDelete: ReferentialAction.Cascade);

            migrationBuilder.AddForeignKey(
                name: "FK_Workouts_Routine_RoutineId",
                table: "Workouts",
                column: "RoutineId",
                principalTable: "Routine",
                principalColumn: "Id");
        }
    }
}
