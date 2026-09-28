using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace DocIntegrity.Api.Migrations
{
    /// <inheritdoc />
    public partial class AddBlockchainFields : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<long>(
                name: "BlockchainBlockNumber",
                table: "Documents",
                type: "bigint",
                nullable: false,
                defaultValue: 0L);

            migrationBuilder.AddColumn<string>(
                name: "BlockchainTransactionHash",
                table: "Documents",
                type: "text",
                nullable: false,
                defaultValue: "");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "BlockchainBlockNumber",
                table: "Documents");

            migrationBuilder.DropColumn(
                name: "BlockchainTransactionHash",
                table: "Documents");
        }
    }
}
