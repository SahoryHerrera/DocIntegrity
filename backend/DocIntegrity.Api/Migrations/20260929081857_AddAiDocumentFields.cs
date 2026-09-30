using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace DocIntegrity.Api.Migrations
{
    /// <inheritdoc />
    public partial class AddAiDocumentFields : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "AiDocumentType",
                table: "Documents",
                type: "text",
                nullable: false,
                defaultValue: "");

            migrationBuilder.AddColumn<string>(
                name: "AiMetadataJson",
                table: "Documents",
                type: "text",
                nullable: false,
                defaultValue: "");

            migrationBuilder.AddColumn<string>(
                name: "AiSummary",
                table: "Documents",
                type: "text",
                nullable: false,
                defaultValue: "");

            migrationBuilder.AddColumn<string>(
                name: "AiTitle",
                table: "Documents",
                type: "text",
                nullable: false,
                defaultValue: "");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "AiDocumentType",
                table: "Documents");

            migrationBuilder.DropColumn(
                name: "AiMetadataJson",
                table: "Documents");

            migrationBuilder.DropColumn(
                name: "AiSummary",
                table: "Documents");

            migrationBuilder.DropColumn(
                name: "AiTitle",
                table: "Documents");
        }
    }
}
