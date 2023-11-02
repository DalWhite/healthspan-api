using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace healthspan_api.Migrations
{
    /// <inheritdoc />
    public partial class ExerciseComputatedFormulas : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AlterColumn<double>(
                name: "Weight",
                table: "Exercises",
                type: "double",
                nullable: true,
                oldClrType: typeof(int),
                oldType: "int",
                oldNullable: true);

            migrationBuilder.AddColumn<int>(
                name: "Intensity",
                table: "Exercises",
                type: "int",
                nullable: true);

            migrationBuilder.AddColumn<double>(
                name: "MaxRepetition",
                table: "Exercises",
                type: "double",
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "Volume",
                table: "Exercises",
                type: "int",
                nullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "Intensity",
                table: "Exercises");

            migrationBuilder.DropColumn(
                name: "MaxRepetition",
                table: "Exercises");

            migrationBuilder.DropColumn(
                name: "Volume",
                table: "Exercises");

            migrationBuilder.AlterColumn<int>(
                name: "Weight",
                table: "Exercises",
                type: "int",
                nullable: true,
                oldClrType: typeof(double),
                oldType: "double",
                oldNullable: true);
        }
    }
}
