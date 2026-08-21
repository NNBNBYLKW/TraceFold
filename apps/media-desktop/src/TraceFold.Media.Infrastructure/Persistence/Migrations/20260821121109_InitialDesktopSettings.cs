using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace TraceFold.Media.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class InitialDesktopSettings : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "app_settings",
                columns: table => new
                {
                    Id = table.Column<int>(type: "INTEGER", nullable: false)
                        .Annotation("Sqlite:Autoincrement", true),
                    InboxDirectory = table.Column<string>(type: "TEXT", maxLength: 4096, nullable: false),
                    LibraryDirectory = table.Column<string>(type: "TEXT", maxLength: 4096, nullable: false),
                    WorkDirectory = table.Column<string>(type: "TEXT", maxLength: 4096, nullable: false),
                    UpdatedAtUtc = table.Column<DateTimeOffset>(type: "TEXT", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_app_settings", x => x.Id);
                });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "app_settings");
        }
    }
}
