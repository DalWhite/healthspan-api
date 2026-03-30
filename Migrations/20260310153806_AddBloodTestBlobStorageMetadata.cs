using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace healthspan_api.Migrations
{
    /// <inheritdoc />
    public partial class AddBloodTestBlobStorageMetadata : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "SourceBlobContainer",
                table: "BloodTests",
                type: "longtext",
                nullable: true)
                .Annotation("MySql:CharSet", "utf8mb4");

            migrationBuilder.AddColumn<string>(
                name: "SourceBlobName",
                table: "BloodTests",
                type: "varchar(255)",
                nullable: true)
                .Annotation("MySql:CharSet", "utf8mb4");

            migrationBuilder.AddColumn<string>(
                name: "SourceBlobUri",
                table: "BloodTests",
                type: "longtext",
                nullable: true)
                .Annotation("MySql:CharSet", "utf8mb4");

            migrationBuilder.CreateIndex(
                name: "IX_BloodTests_SourceBlobName",
                table: "BloodTests",
                column: "SourceBlobName");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_BloodTests_SourceBlobName",
                table: "BloodTests");

            migrationBuilder.DropColumn(
                name: "SourceBlobContainer",
                table: "BloodTests");

            migrationBuilder.DropColumn(
                name: "SourceBlobName",
                table: "BloodTests");

            migrationBuilder.DropColumn(
                name: "SourceBlobUri",
                table: "BloodTests");
        }
    }
}
