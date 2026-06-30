using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Jellystat_API_Core.Migrations
{
    /// <inheritdoc />
    public partial class MakeServerIdOptionOnLogs : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_Logs_Servers_ServerId",
                table: "Logs");

            migrationBuilder.DropIndex(
                name: "IX_Logs_ServerId",
                table: "Logs");

            migrationBuilder.AlterColumn<string>(
                name: "ServerId",
                table: "Logs",
                type: "text",
                nullable: true,
                oldClrType: typeof(string),
                oldType: "text");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AlterColumn<string>(
                name: "ServerId",
                table: "Logs",
                type: "text",
                nullable: false,
                defaultValue: "",
                oldClrType: typeof(string),
                oldType: "text",
                oldNullable: true);

            migrationBuilder.CreateIndex(
                name: "IX_Logs_ServerId",
                table: "Logs",
                column: "ServerId");

            migrationBuilder.AddForeignKey(
                name: "FK_Logs_Servers_ServerId",
                table: "Logs",
                column: "ServerId",
                principalTable: "Servers",
                principalColumn: "Id",
                onDelete: ReferentialAction.Cascade);
        }
    }
}
