using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Jellystat_API_Core.Migrations
{
    /// <inheritdoc />
    public partial class MakeLibraryKeyComposite : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_ExcludedLibraries_Libraries_LibraryId",
                table: "ExcludedLibraries");

            migrationBuilder.DropForeignKey(
                name: "FK_Items_Libraries_LibraryId",
                table: "Items");

            migrationBuilder.DropForeignKey(
                name: "FK_ItemsStaging_Libraries_LibraryId",
                table: "ItemsStaging");

            migrationBuilder.DropPrimaryKey(
                name: "PK_Libraries",
                table: "Libraries");

            migrationBuilder.DropIndex(
                name: "IX_Libraries_ServerId",
                table: "Libraries");

            migrationBuilder.DropIndex(
                name: "IX_ItemsStaging_LibraryId",
                table: "ItemsStaging");

            migrationBuilder.DropIndex(
                name: "IX_ItemsStaging_ServerId",
                table: "ItemsStaging");

            migrationBuilder.DropIndex(
                name: "IX_Items_LibraryId",
                table: "Items");

            migrationBuilder.DropIndex(
                name: "IX_Items_ServerId",
                table: "Items");

            migrationBuilder.DropIndex(
                name: "IX_ExcludedLibraries_LibraryId",
                table: "ExcludedLibraries");

            migrationBuilder.AddPrimaryKey(
                name: "Id",
                table: "Libraries",
                columns: new[] { "ServerId", "Id" });

            migrationBuilder.CreateIndex(
                name: "IX_ItemsStaging_ServerId_LibraryId",
                table: "ItemsStaging",
                columns: new[] { "ServerId", "LibraryId" });

            migrationBuilder.CreateIndex(
                name: "IX_Items_ServerId_LibraryId",
                table: "Items",
                columns: new[] { "ServerId", "LibraryId" });

            migrationBuilder.AddForeignKey(
                name: "FK_ExcludedLibraries_Libraries_ServerId_LibraryId",
                table: "ExcludedLibraries",
                columns: new[] { "ServerId", "LibraryId" },
                principalTable: "Libraries",
                principalColumns: new[] { "ServerId", "Id" },
                onDelete: ReferentialAction.Cascade);

            migrationBuilder.AddForeignKey(
                name: "FK_Items_Libraries_ServerId_LibraryId",
                table: "Items",
                columns: new[] { "ServerId", "LibraryId" },
                principalTable: "Libraries",
                principalColumns: new[] { "ServerId", "Id" },
                onDelete: ReferentialAction.Cascade);

            migrationBuilder.AddForeignKey(
                name: "FK_ItemsStaging_Libraries_ServerId_LibraryId",
                table: "ItemsStaging",
                columns: new[] { "ServerId", "LibraryId" },
                principalTable: "Libraries",
                principalColumns: new[] { "ServerId", "Id" },
                onDelete: ReferentialAction.Cascade);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_ExcludedLibraries_Libraries_ServerId_LibraryId",
                table: "ExcludedLibraries");

            migrationBuilder.DropForeignKey(
                name: "FK_Items_Libraries_ServerId_LibraryId",
                table: "Items");

            migrationBuilder.DropForeignKey(
                name: "FK_ItemsStaging_Libraries_ServerId_LibraryId",
                table: "ItemsStaging");

            migrationBuilder.DropPrimaryKey(
                name: "Id",
                table: "Libraries");

            migrationBuilder.DropIndex(
                name: "IX_ItemsStaging_ServerId_LibraryId",
                table: "ItemsStaging");

            migrationBuilder.DropIndex(
                name: "IX_Items_ServerId_LibraryId",
                table: "Items");

            migrationBuilder.AddPrimaryKey(
                name: "PK_Libraries",
                table: "Libraries",
                column: "Id");

            migrationBuilder.CreateIndex(
                name: "IX_Libraries_ServerId",
                table: "Libraries",
                column: "ServerId");

            migrationBuilder.CreateIndex(
                name: "IX_ItemsStaging_LibraryId",
                table: "ItemsStaging",
                column: "LibraryId");

            migrationBuilder.CreateIndex(
                name: "IX_ItemsStaging_ServerId",
                table: "ItemsStaging",
                column: "ServerId");

            migrationBuilder.CreateIndex(
                name: "IX_Items_LibraryId",
                table: "Items",
                column: "LibraryId");

            migrationBuilder.CreateIndex(
                name: "IX_Items_ServerId",
                table: "Items",
                column: "ServerId");

            migrationBuilder.CreateIndex(
                name: "IX_ExcludedLibraries_LibraryId",
                table: "ExcludedLibraries",
                column: "LibraryId");

            migrationBuilder.AddForeignKey(
                name: "FK_ExcludedLibraries_Libraries_LibraryId",
                table: "ExcludedLibraries",
                column: "LibraryId",
                principalTable: "Libraries",
                principalColumn: "Id",
                onDelete: ReferentialAction.Cascade);

            migrationBuilder.AddForeignKey(
                name: "FK_Items_Libraries_LibraryId",
                table: "Items",
                column: "LibraryId",
                principalTable: "Libraries",
                principalColumn: "Id",
                onDelete: ReferentialAction.Cascade);

            migrationBuilder.AddForeignKey(
                name: "FK_ItemsStaging_Libraries_LibraryId",
                table: "ItemsStaging",
                column: "LibraryId",
                principalTable: "Libraries",
                principalColumn: "Id",
                onDelete: ReferentialAction.Cascade);
        }
    }
}
