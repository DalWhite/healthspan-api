using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace healthspan_api.Migrations
{
    /// <inheritdoc />
    public partial class ImproveBloodTestDataIntegrity : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "SourceContentHash",
                table: "BloodTests",
                type: "varchar(64)",
                maxLength: 64,
                nullable: true)
                .Annotation("MySql:CharSet", "utf8mb4");

            migrationBuilder.AddColumn<bool>(
                name: "ReferenceRangeFromReport",
                table: "BloodTestMarkerResults",
                type: "tinyint(1)",
                nullable: false,
                defaultValue: false);

            migrationBuilder.CreateIndex(
                name: "IX_BloodTests_UserId_SourceContentHash",
                table: "BloodTests",
                columns: new[] { "UserId", "SourceContentHash" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_BloodTests_UserId_SourceContentHash",
                table: "BloodTests");

            migrationBuilder.DropColumn(
                name: "SourceContentHash",
                table: "BloodTests");

            migrationBuilder.DropColumn(
                name: "ReferenceRangeFromReport",
                table: "BloodTestMarkerResults");
        }
    }
}
