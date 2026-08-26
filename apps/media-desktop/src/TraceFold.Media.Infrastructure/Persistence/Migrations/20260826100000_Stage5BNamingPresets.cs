using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace TraceFold.Media.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class Stage5BNamingPresets : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "naming_presets",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "TEXT", nullable: false),
                    display_name = table.Column<string>(type: "TEXT", maxLength: 512, nullable: false),
                    normalized_name = table.Column<string>(type: "TEXT", maxLength: 512, nullable: false),
                    schema_version = table.Column<int>(type: "INTEGER", nullable: false),
                    source = table.Column<string>(type: "TEXT", maxLength: 32, nullable: false),
                    rules_json = table.Column<string>(type: "TEXT", nullable: false),
                    created_at_utc = table.Column<DateTimeOffset>(type: "TEXT", nullable: false),
                    updated_at_utc = table.Column<DateTimeOffset>(type: "TEXT", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_naming_presets", x => x.id);
                });

            migrationBuilder.CreateIndex(
                name: "IX_naming_presets_normalized_name",
                table: "naming_presets",
                column: "normalized_name",
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "naming_presets");
        }
    }
}
