using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Jellystat_API_Core.Migrations
{
    /// <inheritdoc />
    public partial class ItemActivityIndexes : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateIndex(
                name: "idx_items_parent_lookup",
                table: "Items",
                columns: new[] { "ParentId", "Id" });

            migrationBuilder.CreateIndex(
                name: "idx_activity_item_lookup",
                table: "Activity",
                column: "ItemId")
                .Annotation("Npgsql:IndexInclude", new[] { "Duration" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "idx_items_parent_lookup",
                table: "Items");

            migrationBuilder.DropIndex(
                name: "idx_activity_item_lookup",
                table: "Activity");
        }
    }
}
