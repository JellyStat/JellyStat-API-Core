using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Jellystat_API_Core.Migrations
{
    /// <inheritdoc />
    public partial class ItemActivityIndexesDateCreatedForSeries : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateIndex(
                name: "idx_activity_season_date",
                table: "Activity",
                column: "SeasonId")
                .Annotation("Npgsql:IndexInclude", new[] { "DateCreated" });

            migrationBuilder.CreateIndex(
                name: "idx_activity_series_date",
                table: "Activity",
                column: "SeriesId")
                .Annotation("Npgsql:IndexInclude", new[] { "DateCreated" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "idx_activity_season_date",
                table: "Activity");

            migrationBuilder.DropIndex(
                name: "idx_activity_series_date",
                table: "Activity");
        }
    }
}
