using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Jellystat_API_Core.Migrations
{
    /// <inheritdoc />
    public partial class addRunTimeTicks : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<long>(
                name: "RuntimeTicks",
                table: "ActivityStaging",
                type: "bigint",
                nullable: false,
                defaultValue: 0L);

            migrationBuilder.AddColumn<long>(
                name: "RuntimeTicks",
                table: "Activity",
                type: "bigint",
                nullable: false,
                defaultValue: 0L);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "RuntimeTicks",
                table: "ActivityStaging");

            migrationBuilder.DropColumn(
                name: "RuntimeTicks",
                table: "Activity");
        }
    }
}
