using Microsoft.EntityFrameworkCore.Metadata;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace healthspan_api.Migrations
{
    /// <inheritdoc />
    public partial class RedesignExerciseTypeMuscles : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_ExerciseTypes_Muscles_PrimaryMuscleId",
                table: "ExerciseTypes");

            migrationBuilder.DropForeignKey(
                name: "FK_ExerciseTypes_Muscles_SecondaryMuscleId",
                table: "ExerciseTypes");

            migrationBuilder.DropIndex(
                name: "IX_ExerciseTypes_PrimaryMuscleId",
                table: "ExerciseTypes");

            migrationBuilder.DropIndex(
                name: "IX_ExerciseTypes_SecondaryMuscleId",
                table: "ExerciseTypes");

            migrationBuilder.DropColumn(
                name: "MuscleGroup",
                table: "ExerciseTypes");

            migrationBuilder.DropColumn(
                name: "PrimaryMuscleId",
                table: "ExerciseTypes");

            migrationBuilder.DropColumn(
                name: "SecondaryMuscleId",
                table: "ExerciseTypes");

            migrationBuilder.AddColumn<string>(
                name: "Slug",
                table: "Muscles",
                type: "varchar(255)",
                nullable: true)
                .Annotation("MySql:CharSet", "utf8mb4");

            migrationBuilder.AddColumn<int>(
                name: "SortOrder",
                table: "Muscles",
                type: "int",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.CreateTable(
                name: "ExerciseTypeMuscleAssignments",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("MySql:ValueGenerationStrategy", MySqlValueGenerationStrategy.IdentityColumn),
                    ExerciseTypeId = table.Column<int>(type: "int", nullable: false),
                    MuscleId = table.Column<int>(type: "int", nullable: false),
                    Role = table.Column<string>(type: "longtext", nullable: false)
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    SortOrder = table.Column<int>(type: "int", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ExerciseTypeMuscleAssignments", x => x.Id);
                    table.ForeignKey(
                        name: "FK_ExerciseTypeMuscleAssignments_ExerciseTypes_ExerciseTypeId",
                        column: x => x.ExerciseTypeId,
                        principalTable: "ExerciseTypes",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_ExerciseTypeMuscleAssignments_Muscles_MuscleId",
                        column: x => x.MuscleId,
                        principalTable: "Muscles",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                })
                .Annotation("MySql:CharSet", "utf8mb4");

            migrationBuilder.CreateIndex(
                name: "IX_Muscles_Slug",
                table: "Muscles",
                column: "Slug",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_ExerciseTypeMuscleAssignments_ExerciseTypeId_MuscleId",
                table: "ExerciseTypeMuscleAssignments",
                columns: new[] { "ExerciseTypeId", "MuscleId" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_ExerciseTypeMuscleAssignments_MuscleId",
                table: "ExerciseTypeMuscleAssignments",
                column: "MuscleId");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "ExerciseTypeMuscleAssignments");

            migrationBuilder.DropIndex(
                name: "IX_Muscles_Slug",
                table: "Muscles");

            migrationBuilder.DropColumn(
                name: "Slug",
                table: "Muscles");

            migrationBuilder.DropColumn(
                name: "SortOrder",
                table: "Muscles");

            migrationBuilder.AddColumn<int>(
                name: "MuscleGroup",
                table: "ExerciseTypes",
                type: "int",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.AddColumn<int>(
                name: "PrimaryMuscleId",
                table: "ExerciseTypes",
                type: "int",
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "SecondaryMuscleId",
                table: "ExerciseTypes",
                type: "int",
                nullable: true);

            migrationBuilder.CreateIndex(
                name: "IX_ExerciseTypes_PrimaryMuscleId",
                table: "ExerciseTypes",
                column: "PrimaryMuscleId");

            migrationBuilder.CreateIndex(
                name: "IX_ExerciseTypes_SecondaryMuscleId",
                table: "ExerciseTypes",
                column: "SecondaryMuscleId");

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
        }
    }
}
