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
                              COALESCE(summary.total_count, 0::bigint) AS ""PlayCount"",
                              COALESCE(summary.total_duration, 0::numeric) AS ""PlayDuration"",
                              summary.""LastPlayedDate"",
                              i.*
                          FROM ""Items"" i
                          LEFT JOIN LATERAL ( 
                              SELECT 
                                  count(act.""Id"") AS total_count,
                                  sum(act.""Duration"") AS total_duration,
                                  max(act.""DateCreated"") AS ""LastPlayedDate""
                              FROM ""ItemsPath"" p
                              -- Join activity based on the pre-calculated descendants list
                              JOIN ""Activity"" act ON act.""ItemId"" = p.""DescendantId""
                              -- Filter where the 'Scope' is the current item in the list
                              WHERE p.""AncestorId"" = i.""Id""
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
