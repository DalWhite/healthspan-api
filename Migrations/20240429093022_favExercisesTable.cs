using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace healthspan_api.Migrations
{
    /// <inheritdoc />
    public partial class favExercisesTable : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "ExerciseTypeUser",
                columns: table => new
                {
                    FavoriteExercisesId = table.Column<int>(type: "int", nullable: false),
                    FavoritedUsersId = table.Column<string>(type: "varchar(255)", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ExerciseTypeUser", x => new { x.FavoriteExercisesId, x.FavoritedUsersId });
                    table.ForeignKey(
                        name: "FK_ExerciseTypeUser_AspNetUsers_FavoritedUsersId",
                        column: x => x.FavoritedUsersId,
                        principalTable: "AspNetUsers",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_ExerciseTypeUser_ExerciseTypes_FavoriteExercisesId",
                        column: x => x.FavoriteExercisesId,
                        principalTable: "ExerciseTypes",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                })
                .Annotation("MySQL:Charset", "utf8mb4");

            migrationBuilder.CreateIndex(
                name: "IX_ExerciseTypeUser_FavoritedUsersId",
                table: "ExerciseTypeUser",
                column: "FavoritedUsersId");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "ExerciseTypeUser");
        }
    }
}
