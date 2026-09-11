using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

#pragma warning disable CA1814 // Prefer jagged arrays over multidimensional

namespace Jellystat_API_Core.Migrations
{
    /// <inheritdoc />
    public partial class AddGlobalTasks : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "GlobalTasks",
                columns: table => new
                {
                    Task = table.Column<int>(type: "integer", nullable: false),
                    IntervalMinutes = table.Column<int>(type: "integer", nullable: false),
                    Enabled = table.Column<bool>(type: "boolean", nullable: false, defaultValue: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_GlobalTasks", x => x.Task);
                });

            migrationBuilder.InsertData(
                table: "GlobalTasks",
                columns: new[] { "Task", "Enabled", "IntervalMinutes" },
                values: new object[,]
                {
                    { 0, true, 1440 },
                    { 4, true, 0 }
                });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "GlobalTasks");
        }
    }
}
