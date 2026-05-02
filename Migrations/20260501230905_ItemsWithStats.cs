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

    COALESCE(a.total_count, 0) AS ""PlayCount"",
    COALESCE(a.total_duration, 0) AS ""PlayDuration"",
	    i.*
FROM ""Items"" i
LEFT JOIN LATERAL (
    -- For every item, find all its children (and itself) recursively
    WITH RECURSIVE descendants AS (
        SELECT ""Id"" FROM ""Items"" WHERE ""Id"" = i.""Id""
        UNION ALL
        SELECT child.""Id"" FROM ""Items"" child
        JOIN descendants d ON child.""ParentId"" = d.""Id""
    )
    -- Sum activities only for those specific child IDs
    SELECT 
        COUNT(*) AS total_count,
        SUM(act.""Duration"") AS total_duration
    FROM ""Activity"" act
    WHERE act.""ItemId"" IN (SELECT ""Id"" FROM descendants)
) a ON true
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
