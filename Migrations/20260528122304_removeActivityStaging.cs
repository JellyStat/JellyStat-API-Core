using System;
using System.Collections.Generic;
using Jellystat_API_Core.Models.API.Base;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Jellystat_API_Core.Migrations
{
    /// <inheritdoc />
    public partial class removeActivityStaging : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "ActivityStaging");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "ActivityStaging",
                columns: table => new
                {
                    Id = table.Column<string>(type: "text", nullable: false, defaultValueSql: "gen_random_uuid()"),
                    Client = table.Column<string>(type: "text", nullable: true),
                    Container = table.Column<string>(type: "text", nullable: true),
                    DateCreated = table.Column<DateTime>(type: "timestamp with time zone", nullable: false, defaultValueSql: "CURRENT_TIMESTAMP"),
                    Device = table.Column<string>(type: "text", nullable: true),
                    DeviceId = table.Column<string>(type: "text", nullable: true),
                    DirectPlay = table.Column<bool>(type: "boolean", nullable: false, defaultValue: true),
                    Duration = table.Column<long>(type: "bigint", nullable: false, defaultValue: 0L),
                    Imported = table.Column<bool>(type: "boolean", nullable: false, defaultValue: false),
                    IpAddress = table.Column<string>(type: "text", nullable: false),
                    IsPaused = table.Column<bool>(type: "boolean", nullable: false, defaultValue: false),
                    ItemId = table.Column<string>(type: "text", nullable: false),
                    LastPaused = table.Column<DateTime>(type: "timestamp with time zone", nullable: false, defaultValueSql: "CURRENT_TIMESTAMP"),
                    MediaStreams = table.Column<IList<BaseMediaStream>>(type: "jsonb", nullable: false, defaultValueSql: "'[]'"),
                    Name = table.Column<string>(type: "text", nullable: false),
                    PlayMethod = table.Column<string>(type: "text", nullable: false),
                    RuntimeTicks = table.Column<long>(type: "bigint", nullable: false, defaultValue: 0L),
                    SeasonId = table.Column<string>(type: "text", nullable: true),
                    SeriesId = table.Column<string>(type: "text", nullable: true),
                    SeriesName = table.Column<string>(type: "text", nullable: true),
                    ServerId = table.Column<string>(type: "text", nullable: false),
                    TranscodingInfo = table.Column<BaseTranscodingInfo>(type: "jsonb", nullable: true),
                    UserId = table.Column<string>(type: "text", nullable: false),
                    UserName = table.Column<string>(type: "text", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ActivityStaging", x => x.Id);
                });
        }
    }
}
