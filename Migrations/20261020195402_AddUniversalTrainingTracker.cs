using System;
using Microsoft.EntityFrameworkCore.Metadata;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace healthspan_api.Migrations
{
    /// <inheritdoc />
    public partial class AddUniversalTrainingTracker : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "Goal",
                table: "Workouts",
                type: "longtext",
                nullable: true)
                .Annotation("MySql:CharSet", "utf8mb4");

            migrationBuilder.AddColumn<string>(
                name: "Modality",
                table: "Workouts",
                type: "longtext",
                nullable: true)
                .Annotation("MySql:CharSet", "utf8mb4");

            migrationBuilder.AddColumn<int>(
                name: "PerceivedFatigue",
                table: "Workouts",
                type: "int",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "SecondaryGoals",
                table: "Workouts",
                type: "longtext",
                nullable: true)
                .Annotation("MySql:CharSet", "utf8mb4");

            migrationBuilder.AddColumn<int>(
                name: "Attempts",
                table: "Exercises",
                type: "int",
                nullable: true);

            migrationBuilder.AddColumn<Guid>(
                name: "BlockId",
                table: "Exercises",
                type: "char(36)",
                nullable: true,
                collation: "ascii_general_ci");

            migrationBuilder.AddColumn<int>(
                name: "CompletedAttempts",
                table: "Exercises",
                type: "int",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "LoadType",
                table: "Exercises",
                type: "longtext",
                nullable: true)
                .Annotation("MySql:CharSet", "utf8mb4");

            migrationBuilder.AddColumn<int>(
                name: "Order",
                table: "Exercises",
                type: "int",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "QualityIntent",
                table: "Exercises",
                type: "varchar(200)",
                maxLength: 200,
                nullable: true)
                .Annotation("MySql:CharSet", "utf8mb4");

            migrationBuilder.CreateTable(
                name: "Adaptations",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false),
                    Type = table.Column<string>(type: "varchar(255)", nullable: false)
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    Order = table.Column<int>(type: "int", nullable: false),
                    Name = table.Column<string>(type: "varchar(100)", maxLength: 100, nullable: false)
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    Description = table.Column<string>(type: "longtext", nullable: true)
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    Color = table.Column<string>(type: "varchar(16)", maxLength: 16, nullable: false)
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    TargetSessionsPerWeek = table.Column<int>(type: "int", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Adaptations", x => x.Id);
                })
                .Annotation("MySql:CharSet", "utf8mb4");

            migrationBuilder.CreateTable(
                name: "WorkoutBlocks",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "char(36)", nullable: false, collation: "ascii_general_ci"),
                    ClientEntityId = table.Column<Guid>(type: "char(36)", nullable: true, collation: "ascii_general_ci"),
                    WorkoutId = table.Column<Guid>(type: "char(36)", nullable: false, collation: "ascii_general_ci"),
                    Order = table.Column<int>(type: "int", nullable: false),
                    Type = table.Column<string>(type: "longtext", nullable: false)
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    Label = table.Column<string>(type: "varchar(100)", maxLength: 100, nullable: true)
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    Rounds = table.Column<int>(type: "int", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_WorkoutBlocks", x => x.Id);
                    table.ForeignKey(
                        name: "FK_WorkoutBlocks_Workouts_WorkoutId",
                        column: x => x.WorkoutId,
                        principalTable: "Workouts",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                })
                .Annotation("MySql:CharSet", "utf8mb4");

            migrationBuilder.CreateTable(
                name: "ExerciseAdaptations",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("MySql:ValueGenerationStrategy", MySqlValueGenerationStrategy.IdentityColumn),
                    ExerciseId = table.Column<Guid>(type: "char(36)", nullable: false, collation: "ascii_general_ci"),
                    AdaptationId = table.Column<int>(type: "int", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ExerciseAdaptations", x => x.Id);
                    table.ForeignKey(
                        name: "FK_ExerciseAdaptations_Adaptations_AdaptationId",
                        column: x => x.AdaptationId,
                        principalTable: "Adaptations",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_ExerciseAdaptations_Exercises_ExerciseId",
                        column: x => x.ExerciseId,
                        principalTable: "Exercises",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                })
                .Annotation("MySql:CharSet", "utf8mb4");

            migrationBuilder.CreateTable(
                name: "ExerciseTypeAdaptations",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("MySql:ValueGenerationStrategy", MySqlValueGenerationStrategy.IdentityColumn),
                    ExerciseTypeId = table.Column<int>(type: "int", nullable: false),
                    AdaptationId = table.Column<int>(type: "int", nullable: false),
                    Role = table.Column<string>(type: "longtext", nullable: false)
                        .Annotation("MySql:CharSet", "utf8mb4")
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ExerciseTypeAdaptations", x => x.Id);
                    table.ForeignKey(
                        name: "FK_ExerciseTypeAdaptations_Adaptations_AdaptationId",
                        column: x => x.AdaptationId,
                        principalTable: "Adaptations",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_ExerciseTypeAdaptations_ExerciseTypes_ExerciseTypeId",
                        column: x => x.ExerciseTypeId,
                        principalTable: "ExerciseTypes",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                })
                .Annotation("MySql:CharSet", "utf8mb4");

            migrationBuilder.CreateIndex(
                name: "IX_Exercises_BlockId",
                table: "Exercises",
                column: "BlockId");

            migrationBuilder.CreateIndex(
                name: "IX_Adaptations_Type",
                table: "Adaptations",
                column: "Type",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_ExerciseAdaptations_AdaptationId",
                table: "ExerciseAdaptations",
                column: "AdaptationId");

            migrationBuilder.CreateIndex(
                name: "IX_ExerciseAdaptations_ExerciseId_AdaptationId",
                table: "ExerciseAdaptations",
                columns: new[] { "ExerciseId", "AdaptationId" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_ExerciseTypeAdaptations_AdaptationId",
                table: "ExerciseTypeAdaptations",
                column: "AdaptationId");

            migrationBuilder.CreateIndex(
                name: "IX_ExerciseTypeAdaptations_ExerciseTypeId_AdaptationId",
                table: "ExerciseTypeAdaptations",
                columns: new[] { "ExerciseTypeId", "AdaptationId" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_WorkoutBlocks_ClientEntityId",
                table: "WorkoutBlocks",
                column: "ClientEntityId",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_WorkoutBlocks_WorkoutId_Order",
                table: "WorkoutBlocks",
                columns: new[] { "WorkoutId", "Order" },
                unique: true);

            migrationBuilder.AddForeignKey(
                name: "FK_Exercises_WorkoutBlocks_BlockId",
                table: "Exercises",
                column: "BlockId",
                principalTable: "WorkoutBlocks",
                principalColumn: "Id",
                onDelete: ReferentialAction.SetNull);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_Exercises_WorkoutBlocks_BlockId",
                table: "Exercises");

            migrationBuilder.DropTable(
                name: "ExerciseAdaptations");

            migrationBuilder.DropTable(
                name: "ExerciseTypeAdaptations");

            migrationBuilder.DropTable(
                name: "WorkoutBlocks");

            migrationBuilder.DropTable(
                name: "Adaptations");

            migrationBuilder.DropIndex(
                name: "IX_Exercises_BlockId",
                table: "Exercises");

            migrationBuilder.DropColumn(
                name: "Goal",
                table: "Workouts");

            migrationBuilder.DropColumn(
                name: "Modality",
                table: "Workouts");

            migrationBuilder.DropColumn(
                name: "PerceivedFatigue",
                table: "Workouts");

            migrationBuilder.DropColumn(
                name: "SecondaryGoals",
                table: "Workouts");

            migrationBuilder.DropColumn(
                name: "Attempts",
                table: "Exercises");

            migrationBuilder.DropColumn(
                name: "BlockId",
                table: "Exercises");

            migrationBuilder.DropColumn(
                name: "CompletedAttempts",
                table: "Exercises");

            migrationBuilder.DropColumn(
                name: "LoadType",
                table: "Exercises");

            migrationBuilder.DropColumn(
                name: "Order",
                table: "Exercises");

            migrationBuilder.DropColumn(
                name: "QualityIntent",
                table: "Exercises");
        }
    }
}
