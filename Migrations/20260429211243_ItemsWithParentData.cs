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
                          to_jsonb(p) AS ""Parent"",
                          i.* 
                          FROM public.""Items"" i
                          LEFT JOIN  public.""Items"" s ON s.""Id"" = i.""ParentId""
                          LEFT JOIN  public.""Items"" p ON p.""Id"" = s.""ParentId""
                          Where i.""ParentId"" is not null
                          and i.""Type"" not in (3,5,6,7)
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
