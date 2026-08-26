using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace TraceFold.Media.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class Stage5BLibraryQuery : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<bool>(
                name: "library_sort_descending",
                table: "app_settings",
                type: "INTEGER",
                nullable: false,
                defaultValue: true);

            migrationBuilder.AddColumn<string>(
                name: "library_sort_field",
                table: "app_settings",
                type: "TEXT",
                maxLength: 32,
                nullable: false,
                defaultValue: "created_at");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "library_sort_descending",
                table: "app_settings");

            migrationBuilder.DropColumn(
                name: "library_sort_field",
                table: "app_settings");
        }
    }
}
