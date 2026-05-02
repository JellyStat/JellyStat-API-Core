using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Jellystat_API_Core.Migrations
{
    /// <inheritdoc />
    public partial class ItemsWithParentData : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql(
                 @"CREATE VIEW ""ItemsWithParentData"" AS 
                          SELECT 
                              i.*,
                              to_jsonb(root_item) AS ""Parent""
                          FROM public.""Items"" i
                          LEFT JOIN LATERAL (
                              -- Look up all ancestors for the current item
                              SELECT a.*
                              FROM public.""ItemsPath"" ip
                              JOIN public.""Items"" a ON a.""Id"" = ip.""AncestorId""
                              WHERE ip.""DescendantId"" = i.""Id""
                              -- The 'Absolute Top Parent' is the one with the maximum distance (Depth)
                              ORDER BY ip.""Depth"" DESC
                              LIMIT 1
                          ) root_item ON TRUE
                          "
                 );
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql("DROP VIEW \"ItemsWithParentData\"");
        }
    }
}

