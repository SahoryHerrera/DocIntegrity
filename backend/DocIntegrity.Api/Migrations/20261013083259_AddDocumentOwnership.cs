using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace DocIntegrity.Api.Migrations
{
    /// <inheritdoc />
    public partial class AddDocumentOwnership : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<bool>(
                name: "BlockchainEvidenceReused",
                table: "Documents",
                type: "boolean",
                nullable: false,
                defaultValue: false);

            migrationBuilder.AddColumn<Guid>(
                name: "RegisteredByUserId",
                table: "Documents",
                type: "uuid",
                nullable: true);

            migrationBuilder.CreateIndex(
                name: "IX_Documents_RegisteredByUserId_RegisteredAt",
                table: "Documents",
                columns: new[] { "RegisteredByUserId", "RegisteredAt" });

            migrationBuilder.CreateIndex(
                name: "UX_Documents_Owner_Sha256",
                table: "Documents",
                columns: new[] { "RegisteredByUserId", "Sha256" },
                unique: true,
                filter: "\"RegisteredByUserId\" IS NOT NULL");

            migrationBuilder.AddForeignKey(
                name: "FK_Documents_Users_RegisteredByUserId",
                table: "Documents",
                column: "RegisteredByUserId",
                principalTable: "Users",
                principalColumn: "Id",
                onDelete: ReferentialAction.Restrict);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_Documents_Users_RegisteredByUserId",
                table: "Documents");

            migrationBuilder.DropIndex(
                name: "IX_Documents_RegisteredByUserId_RegisteredAt",
                table: "Documents");

            migrationBuilder.DropIndex(
                name: "UX_Documents_Owner_Sha256",
                table: "Documents");

            migrationBuilder.DropColumn(
                name: "BlockchainEvidenceReused",
                table: "Documents");

            migrationBuilder.DropColumn(
                name: "RegisteredByUserId",
                table: "Documents");
        }
    }
}
