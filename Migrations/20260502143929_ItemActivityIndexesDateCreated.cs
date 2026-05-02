using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Jellystat_API_Core.Migrations
{
    /// <inheritdoc />
    public partial class ItemActivityIndexesDateCreated : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "idx_activity_item_lookup",
                table: "Activity");

            migrationBuilder.CreateIndex(
                name: "idx_activity_item_date",
                table: "Activity",
                column: "ItemId")
                .Annotation("Npgsql:IndexInclude", new[] { "DateCreated" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "idx_activity_item_date",
                table: "Activity");

            migrationBuilder.CreateIndex(
                name: "idx_activity_item_lookup",
                table: "Activity",
                column: "ItemId")
                .Annotation("Npgsql:IndexInclude", new[] { "Duration" });
        }
    }
}
