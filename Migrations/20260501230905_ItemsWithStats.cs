using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Jellystat_API_Core.Migrations
{
    /// <inheritdoc />
    public partial class ItemsWithStats : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {

            migrationBuilder.Sql(
                 @"CREATE VIEW ""ItemsWithStats"" AS 
                        SELECT 
                            COALESCE(summary.total_count, 0) AS ""PlayCount"",
                            COALESCE(summary.total_duration, 0) AS ""PlayDuration"",
                            summary.""LastPlayedDate"",
                            i.""Id"",
                            i.""Name"",
                            i.""ServerId"",
                            i.""LibraryId"",
                            i.""DateCreated"",
                            i.""MediaStreams"",
                            i.""Duration"",
                            i.""Type"",
                            i.""ImageTag"",
                            i.""ImageHash"",
                            i.""Genres"",
                            i.""Path"",
                            i.""Size"",
                            i.""Bitrate"",
                            i.""ParentId"",
                            i.""ParentIndex"",
                            i.""Index"",
                            i.archived
                        FROM ""Items"" i
                        LEFT JOIN LATERAL (
                            SELECT 
                                count(*) AS total_count,
                                sum(act.""Duration"") AS total_duration,
                                max(act.""DateCreated"") AS ""LastPlayedDate""
                            FROM ""Activity"" act
                            WHERE 
                                -- This replaces the recursion:
                                act.""ItemId"" = i.""Id"" 
                                OR act.""SeriesId"" = i.""Id"" 
                                OR act.""SeasonId"" = i.""Id""
                        ) summary ON true;
                          "
                 );

        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql("DROP VIEW \"ItemsWithStats\"");
        }
    }
}
