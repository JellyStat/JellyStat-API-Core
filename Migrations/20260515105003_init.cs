using System;
using System.Collections.Generic;
using Jellystat_API_Core.Models.API.Base;
using Jellystat_API_Core.Models.Jellystat;
using Microsoft.EntityFrameworkCore.Migrations;
using Npgsql.EntityFrameworkCore.PostgreSQL.Metadata;

#nullable disable

namespace Jellystat_API_Core.Migrations
{
    /// <inheritdoc />
    public partial class init : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AlterDatabase()
                .Annotation("Npgsql:PostgresExtension:pg_trgm", ",,");

            migrationBuilder.CreateTable(
                name: "Activity",
                columns: table => new
                {
                    Id = table.Column<string>(type: "text", nullable: false, defaultValueSql: "gen_random_uuid()"),
                    ServerId = table.Column<string>(type: "text", nullable: false),
                    Name = table.Column<string>(type: "text", nullable: false),
                    SeriesName = table.Column<string>(type: "text", nullable: true),
                    UserId = table.Column<string>(type: "text", nullable: false),
                    UserName = table.Column<string>(type: "text", nullable: false),
                    Client = table.Column<string>(type: "text", nullable: true),
                    Device = table.Column<string>(type: "text", nullable: true),
                    DeviceId = table.Column<string>(type: "text", nullable: true),
                    ItemId = table.Column<string>(type: "text", nullable: false),
                    SeriesId = table.Column<string>(type: "text", nullable: true),
                    SeasonId = table.Column<string>(type: "text", nullable: true),
                    Duration = table.Column<long>(type: "bigint", nullable: false, defaultValue: 0L),
                    DirectPlay = table.Column<bool>(type: "boolean", nullable: false, defaultValue: true),
                    PlayMethod = table.Column<string>(type: "text", nullable: false),
                    MediaStreams = table.Column<IList<BaseMediaStream>>(type: "jsonb", nullable: false, defaultValueSql: "'[]'"),
                    TranscodingInfo = table.Column<BaseTranscodingInfo>(type: "jsonb", nullable: true),
                    Imported = table.Column<bool>(type: "boolean", nullable: false, defaultValue: false),
                    DateCreated = table.Column<DateTime>(type: "timestamp with time zone", nullable: false, defaultValueSql: "CURRENT_TIMESTAMP"),
                    Container = table.Column<string>(type: "text", nullable: true),
                    IpAddress = table.Column<string>(type: "text", nullable: false),
                    RuntimeTicks = table.Column<long>(type: "bigint", nullable: false, defaultValue: 0L)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Activity", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "ActivityStaging",
                columns: table => new
                {
                    Id = table.Column<string>(type: "text", nullable: false, defaultValueSql: "gen_random_uuid()"),
                    ServerId = table.Column<string>(type: "text", nullable: false),
                    Name = table.Column<string>(type: "text", nullable: false),
                    SeriesName = table.Column<string>(type: "text", nullable: true),
                    UserId = table.Column<string>(type: "text", nullable: false),
                    UserName = table.Column<string>(type: "text", nullable: false),
                    Client = table.Column<string>(type: "text", nullable: true),
                    Device = table.Column<string>(type: "text", nullable: true),
                    DeviceId = table.Column<string>(type: "text", nullable: true),
                    ItemId = table.Column<string>(type: "text", nullable: false),
                    SeriesId = table.Column<string>(type: "text", nullable: true),
                    SeasonId = table.Column<string>(type: "text", nullable: true),
                    Duration = table.Column<long>(type: "bigint", nullable: false, defaultValue: 0L),
                    DirectPlay = table.Column<bool>(type: "boolean", nullable: false, defaultValue: true),
                    PlayMethod = table.Column<string>(type: "text", nullable: false),
                    MediaStreams = table.Column<IList<BaseMediaStream>>(type: "jsonb", nullable: false, defaultValueSql: "'[]'"),
                    TranscodingInfo = table.Column<BaseTranscodingInfo>(type: "jsonb", nullable: true),
                    Imported = table.Column<bool>(type: "boolean", nullable: false, defaultValue: false),
                    DateCreated = table.Column<DateTime>(type: "timestamp with time zone", nullable: false, defaultValueSql: "CURRENT_TIMESTAMP"),
                    LastPaused = table.Column<DateTime>(type: "timestamp with time zone", nullable: false, defaultValueSql: "CURRENT_TIMESTAMP"),
                    Container = table.Column<string>(type: "text", nullable: true),
                    IpAddress = table.Column<string>(type: "text", nullable: false),
                    IsPaused = table.Column<bool>(type: "boolean", nullable: false, defaultValue: false),
                    RuntimeTicks = table.Column<long>(type: "bigint", nullable: false, defaultValue: 0L)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ActivityStaging", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "LocalUsers",
                columns: table => new
                {
                    Id = table.Column<string>(type: "text", nullable: false),
                    Username = table.Column<string>(type: "text", nullable: false),
                    Password = table.Column<string>(type: "text", nullable: false),
                    ServerId = table.Column<string>(type: "text", nullable: true, defaultValue: ""),
                    ImageTag = table.Column<string>(type: "text", nullable: true, defaultValue: "")
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_LocalUsers", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "Permissions",
                columns: table => new
                {
                    Id = table.Column<int>(type: "integer", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    PermissionKey = table.Column<string>(type: "text", nullable: false),
                    Description = table.Column<string>(type: "text", nullable: false),
                    Active = table.Column<bool>(type: "boolean", nullable: false, defaultValue: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Permissions", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "Servers",
                columns: table => new
                {
                    Id = table.Column<string>(type: "text", nullable: false),
                    URL = table.Column<string>(type: "text", nullable: false),
                    ExternalURL = table.Column<string>(type: "text", nullable: false, defaultValue: ""),
                    APIKey = table.Column<string>(type: "text", nullable: false),
                    Name = table.Column<string>(type: "text", nullable: false),
                    Type = table.Column<int>(type: "integer", nullable: false),
                    TaskSettings = table.Column<List<TaskSettings>>(type: "jsonb", nullable: false, defaultValueSql: "'[]'")
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Servers", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "UserPermissions",
                columns: table => new
                {
                    PermissionId = table.Column<int>(type: "integer", nullable: false),
                    UserId = table.Column<string>(type: "text", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_UserPermissions", x => new { x.PermissionId, x.UserId });
                    table.ForeignKey(
                        name: "FK_UserPermissions_LocalUsers_UserId",
                        column: x => x.UserId,
                        principalTable: "LocalUsers",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_UserPermissions_Permissions_PermissionId",
                        column: x => x.PermissionId,
                        principalTable: "Permissions",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "Libraries",
                columns: table => new
                {
                    Id = table.Column<string>(type: "text", nullable: false),
                    ServerId = table.Column<string>(type: "text", nullable: false),
                    Name = table.Column<string>(type: "text", nullable: false),
                    Type = table.Column<int>(type: "integer", nullable: false),
                    ImageTag = table.Column<string>(type: "text", nullable: true, defaultValue: ""),
                    ImageHash = table.Column<string>(type: "text", nullable: true, defaultValue: ""),
                    archived = table.Column<bool>(type: "boolean", nullable: false, defaultValue: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Libraries", x => x.Id);
                    table.ForeignKey(
                        name: "FK_Libraries_Servers_ServerId",
                        column: x => x.ServerId,
                        principalTable: "Servers",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "Users",
                columns: table => new
                {
                    Id = table.Column<string>(type: "text", nullable: false),
                    Username = table.Column<string>(type: "text", nullable: false),
                    ServerId = table.Column<string>(type: "text", nullable: false),
                    ImageTag = table.Column<string>(type: "text", nullable: true, defaultValue: "")
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Users", x => x.Id);
                    table.ForeignKey(
                        name: "FK_Users_Servers_ServerId",
                        column: x => x.ServerId,
                        principalTable: "Servers",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "ExcludedLibraries",
                columns: table => new
                {
                    ServerId = table.Column<string>(type: "text", nullable: false),
                    LibraryId = table.Column<string>(type: "text", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ExcludedLibraries", x => new { x.ServerId, x.LibraryId });
                    table.ForeignKey(
                        name: "FK_ExcludedLibraries_Libraries_LibraryId",
                        column: x => x.LibraryId,
                        principalTable: "Libraries",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_ExcludedLibraries_Servers_ServerId",
                        column: x => x.ServerId,
                        principalTable: "Servers",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "Items",
                columns: table => new
                {
                    Id = table.Column<string>(type: "text", nullable: false),
                    Name = table.Column<string>(type: "text", nullable: false),
                    ServerId = table.Column<string>(type: "text", nullable: false),
                    LibraryId = table.Column<string>(type: "text", nullable: false),
                    DateCreated = table.Column<DateTime>(type: "timestamp with time zone", nullable: true, defaultValueSql: "CURRENT_TIMESTAMP"),
                    MediaStreams = table.Column<IList<BaseMediaStream>>(type: "jsonb", nullable: false, defaultValueSql: "'[]'"),
                    Duration = table.Column<long>(type: "bigint", nullable: false, defaultValue: 0L),
                    Type = table.Column<int>(type: "integer", nullable: false),
                    ImageTag = table.Column<string>(type: "text", nullable: false, defaultValue: ""),
                    ImageHash = table.Column<string>(type: "text", nullable: false, defaultValue: ""),
                    Genres = table.Column<IList<string>>(type: "jsonb", nullable: false, defaultValueSql: "'[]'"),
                    Path = table.Column<string>(type: "text", nullable: true, defaultValue: ""),
                    Size = table.Column<long>(type: "bigint", nullable: false, defaultValue: 0L),
                    Bitrate = table.Column<long>(type: "bigint", nullable: false, defaultValue: 0L),
                    ParentId = table.Column<string>(type: "text", nullable: true, defaultValue: ""),
                    ParentIndex = table.Column<int>(type: "integer", nullable: true),
                    Index = table.Column<int>(type: "integer", nullable: true),
                    archived = table.Column<bool>(type: "boolean", nullable: false, defaultValue: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Items", x => x.Id);
                    table.ForeignKey(
                        name: "FK_Items_Libraries_LibraryId",
                        column: x => x.LibraryId,
                        principalTable: "Libraries",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_Items_Servers_ServerId",
                        column: x => x.ServerId,
                        principalTable: "Servers",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "ItemsStaging",
                columns: table => new
                {
                    Id = table.Column<string>(type: "text", nullable: false),
                    Name = table.Column<string>(type: "text", nullable: false),
                    ServerId = table.Column<string>(type: "text", nullable: false),
                    LibraryId = table.Column<string>(type: "text", nullable: false),
                    DateCreated = table.Column<DateTime>(type: "timestamp with time zone", nullable: true, defaultValueSql: "CURRENT_TIMESTAMP"),
                    MediaStreams = table.Column<IList<BaseMediaStream>>(type: "jsonb", nullable: false, defaultValueSql: "'[]'"),
                    Duration = table.Column<long>(type: "bigint", nullable: false, defaultValue: 0L),
                    Type = table.Column<int>(type: "integer", nullable: false),
                    ImageTag = table.Column<string>(type: "text", nullable: false, defaultValue: ""),
                    ImageHash = table.Column<string>(type: "text", nullable: false, defaultValue: ""),
                    Genres = table.Column<IList<string>>(type: "jsonb", nullable: false, defaultValueSql: "'[]'"),
                    Path = table.Column<string>(type: "text", nullable: true, defaultValue: ""),
                    Size = table.Column<long>(type: "bigint", nullable: false, defaultValue: 0L),
                    Bitrate = table.Column<long>(type: "bigint", nullable: false, defaultValue: 0L),
                    ParentId = table.Column<string>(type: "text", nullable: true, defaultValue: ""),
                    ParentIndex = table.Column<int>(type: "integer", nullable: true),
                    Index = table.Column<int>(type: "integer", nullable: true),
                    archived = table.Column<bool>(type: "boolean", nullable: false, defaultValue: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ItemsStaging", x => x.Id);
                    table.ForeignKey(
                        name: "FK_ItemsStaging_Libraries_LibraryId",
                        column: x => x.LibraryId,
                        principalTable: "Libraries",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_ItemsStaging_Servers_ServerId",
                        column: x => x.ServerId,
                        principalTable: "Servers",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "ExcludedUsers",
                columns: table => new
                {
                    ServerId = table.Column<string>(type: "text", nullable: false),
                    UserId = table.Column<string>(type: "text", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ExcludedUsers", x => new { x.ServerId, x.UserId });
                    table.ForeignKey(
                        name: "FK_ExcludedUsers_Servers_ServerId",
                        column: x => x.ServerId,
                        principalTable: "Servers",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_ExcludedUsers_Users_UserId",
                        column: x => x.UserId,
                        principalTable: "Users",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "ItemsPath",
                columns: table => new
                {
                    AncestorId = table.Column<string>(type: "text", nullable: false),
                    DescendantId = table.Column<string>(type: "text", nullable: false),
                    Depth = table.Column<int>(type: "integer", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ItemsPath", x => new { x.AncestorId, x.DescendantId });
                    table.ForeignKey(
                        name: "FK_ItemsPath_Items_AncestorId",
                        column: x => x.AncestorId,
                        principalTable: "Items",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_ItemsPath_Items_DescendantId",
                        column: x => x.DescendantId,
                        principalTable: "Items",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "idx_activity_item_lookup",
                table: "Activity",
                column: "ItemId")
                .Annotation("Npgsql:IndexInclude", new[] { "Duration" });

            migrationBuilder.CreateIndex(
                name: "IX_ExcludedLibraries_LibraryId",
                table: "ExcludedLibraries",
                column: "LibraryId");

            migrationBuilder.CreateIndex(
                name: "IX_ExcludedUsers_UserId",
                table: "ExcludedUsers",
                column: "UserId");

            migrationBuilder.CreateIndex(
                name: "IX_Items_LibraryId",
                table: "Items",
                column: "LibraryId");

            migrationBuilder.CreateIndex(
                name: "IX_Items_ServerId",
                table: "Items",
                column: "ServerId");

            migrationBuilder.CreateIndex(
                name: "idx_itempaths_ancestor",
                table: "ItemsPath",
                column: "AncestorId");

            migrationBuilder.CreateIndex(
                name: "idx_itempaths_ancestor_depth",
                table: "ItemsPath",
                columns: new[] { "AncestorId", "Depth" });

            migrationBuilder.CreateIndex(
                name: "idx_itempaths_descendant",
                table: "ItemsPath",
                column: "DescendantId");

            migrationBuilder.CreateIndex(
                name: "IX_ItemsStaging_LibraryId",
                table: "ItemsStaging",
                column: "LibraryId");

            migrationBuilder.CreateIndex(
                name: "IX_ItemsStaging_ServerId",
                table: "ItemsStaging",
                column: "ServerId");

            migrationBuilder.CreateIndex(
                name: "IX_Libraries_ServerId",
                table: "Libraries",
                column: "ServerId");

            migrationBuilder.CreateIndex(
                name: "IX_UserPermissions_UserId",
                table: "UserPermissions",
                column: "UserId");

            migrationBuilder.CreateIndex(
                name: "IX_Users_ServerId",
                table: "Users",
                column: "ServerId");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "Activity");

            migrationBuilder.DropTable(
                name: "ActivityStaging");

            migrationBuilder.DropTable(
                name: "ExcludedLibraries");

            migrationBuilder.DropTable(
                name: "ExcludedUsers");

            migrationBuilder.DropTable(
                name: "ItemsPath");

            migrationBuilder.DropTable(
                name: "ItemsStaging");

            migrationBuilder.DropTable(
                name: "UserPermissions");

            migrationBuilder.DropTable(
                name: "Users");

            migrationBuilder.DropTable(
                name: "Items");

            migrationBuilder.DropTable(
                name: "LocalUsers");

            migrationBuilder.DropTable(
                name: "Permissions");

            migrationBuilder.DropTable(
                name: "Libraries");

            migrationBuilder.DropTable(
                name: "Servers");
        }
    }
}
