using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace TraceFold.Media.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class Stage4ConflictRecovery : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_media_assets_sha256",
                table: "media_assets");

            migrationBuilder.AddColumn<Guid>(
                name: "asset_id",
                table: "ingest_operations",
                type: "TEXT",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "backup_relative_path",
                table: "ingest_operations",
                type: "TEXT",
                maxLength: 4096,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "backup_sha256",
                table: "ingest_operations",
                type: "TEXT",
                maxLength: 64,
                nullable: true);

            migrationBuilder.AddColumn<long>(
                name: "backup_size",
                table: "ingest_operations",
                type: "INTEGER",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "backup_state",
                table: "ingest_operations",
                type: "TEXT",
                maxLength: 64,
                nullable: false,
                defaultValue: "notrequired");

            migrationBuilder.AddColumn<int>(
                name: "cleanup_attempt_count",
                table: "ingest_operations",
                type: "INTEGER",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.AddColumn<Guid>(
                name: "conflict_case_id",
                table: "ingest_operations",
                type: "TEXT",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "disposition",
                table: "ingest_operations",
                type: "TEXT",
                maxLength: 64,
                nullable: false,
                defaultValue: "create");

            migrationBuilder.AddColumn<DateTimeOffset>(
                name: "expected_target_last_write_time_utc",
                table: "ingest_operations",
                type: "TEXT",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "expected_target_relative_path",
                table: "ingest_operations",
                type: "TEXT",
                maxLength: 4096,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "expected_target_sha256",
                table: "ingest_operations",
                type: "TEXT",
                maxLength: 64,
                nullable: true);

            migrationBuilder.AddColumn<long>(
                name: "expected_target_size",
                table: "ingest_operations",
                type: "INTEGER",
                nullable: true);

            migrationBuilder.AddColumn<DateTimeOffset>(
                name: "next_cleanup_attempt_at_utc",
                table: "ingest_operations",
                type: "TEXT",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "old_asset_snapshot_json",
                table: "ingest_operations",
                type: "TEXT",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "recovery_blocked_from_stage",
                table: "ingest_operations",
                type: "TEXT",
                maxLength: 64,
                nullable: true);

            migrationBuilder.AddColumn<Guid>(
                name: "replacement_asset_id",
                table: "ingest_operations",
                type: "TEXT",
                nullable: true);

            migrationBuilder.AddColumn<long>(
                name: "replacement_asset_metadata_version",
                table: "ingest_operations",
                type: "INTEGER",
                nullable: true);

            migrationBuilder.AddColumn<DateTimeOffset>(
                name: "last_attempt_at_utc",
                table: "ingest_cleanup_entries",
                type: "TEXT",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "last_error_code",
                table: "ingest_cleanup_entries",
                type: "TEXT",
                maxLength: 128,
                nullable: true);

            migrationBuilder.CreateTable(
                name: "conflict_cases",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "TEXT", nullable: false),
                    inbox_item_id = table.Column<Guid>(type: "TEXT", nullable: false),
                    active_inbox_item_id = table.Column<Guid>(type: "TEXT", nullable: true),
                    status = table.Column<string>(type: "TEXT", maxLength: 64, nullable: false),
                    input_path = table.Column<string>(type: "TEXT", maxLength: 4096, nullable: false),
                    input_size = table.Column<long>(type: "INTEGER", nullable: false),
                    input_last_write_time_utc = table.Column<DateTimeOffset>(type: "TEXT", nullable: false),
                    input_sha256 = table.Column<string>(type: "TEXT", maxLength: 64, nullable: false),
                    target_filename = table.Column<string>(type: "TEXT", maxLength: 1024, nullable: false),
                    metadata_version = table.Column<long>(type: "INTEGER", nullable: false),
                    resolution = table.Column<string>(type: "TEXT", maxLength: 64, nullable: true),
                    selected_match_id = table.Column<Guid>(type: "TEXT", nullable: true),
                    operation_id = table.Column<Guid>(type: "TEXT", nullable: true),
                    invalidation_reason = table.Column<string>(type: "TEXT", maxLength: 128, nullable: true),
                    created_at_utc = table.Column<DateTimeOffset>(type: "TEXT", nullable: false),
                    updated_at_utc = table.Column<DateTimeOffset>(type: "TEXT", nullable: false),
                    resolved_at_utc = table.Column<DateTimeOffset>(type: "TEXT", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_conflict_cases", x => x.id);
                    table.ForeignKey(
                        name: "FK_conflict_cases_inbox_items_inbox_item_id",
                        column: x => x.inbox_item_id,
                        principalTable: "inbox_items",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "conflict_matches",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "TEXT", nullable: false),
                    conflict_case_id = table.Column<Guid>(type: "TEXT", nullable: false),
                    reason = table.Column<int>(type: "INTEGER", nullable: false),
                    is_registered = table.Column<bool>(type: "INTEGER", nullable: false),
                    asset_id = table.Column<Guid>(type: "TEXT", nullable: true),
                    asset_metadata_version = table.Column<long>(type: "INTEGER", nullable: true),
                    relative_path = table.Column<string>(type: "TEXT", maxLength: 4096, nullable: false),
                    actual_filename = table.Column<string>(type: "TEXT", maxLength: 1024, nullable: false),
                    display_name = table.Column<string>(type: "TEXT", maxLength: 1024, nullable: true),
                    media_kind = table.Column<string>(type: "TEXT", maxLength: 32, nullable: false),
                    size = table.Column<long>(type: "INTEGER", nullable: false),
                    last_write_time_utc = table.Column<DateTimeOffset>(type: "TEXT", nullable: false),
                    sha256 = table.Column<string>(type: "TEXT", maxLength: 64, nullable: false),
                    metadata_snapshot_json = table.Column<string>(type: "TEXT", nullable: false),
                    technical_snapshot_json = table.Column<string>(type: "TEXT", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_conflict_matches", x => x.id);
                    table.ForeignKey(
                        name: "FK_conflict_matches_conflict_cases_conflict_case_id",
                        column: x => x.conflict_case_id,
                        principalTable: "conflict_cases",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_media_assets_sha256",
                table: "media_assets",
                column: "sha256");

            migrationBuilder.CreateIndex(
                name: "IX_ingest_operations_asset_id",
                table: "ingest_operations",
                column: "asset_id");

            migrationBuilder.CreateIndex(
                name: "IX_ingest_operations_conflict_case_id",
                table: "ingest_operations",
                column: "conflict_case_id",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_ingest_operations_next_cleanup_attempt_at_utc",
                table: "ingest_operations",
                column: "next_cleanup_attempt_at_utc");

            migrationBuilder.CreateIndex(
                name: "IX_ingest_operations_replacement_asset_id",
                table: "ingest_operations",
                column: "replacement_asset_id");

            migrationBuilder.CreateIndex(
                name: "IX_conflict_cases_active_inbox_item_id",
                table: "conflict_cases",
                column: "active_inbox_item_id",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_conflict_cases_inbox_item_id",
                table: "conflict_cases",
                column: "inbox_item_id");

            migrationBuilder.CreateIndex(
                name: "IX_conflict_cases_input_sha256",
                table: "conflict_cases",
                column: "input_sha256");

            migrationBuilder.CreateIndex(
                name: "IX_conflict_cases_operation_id",
                table: "conflict_cases",
                column: "operation_id",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_conflict_matches_conflict_case_id",
                table: "conflict_matches",
                column: "conflict_case_id");

            migrationBuilder.AddForeignKey(
                name: "FK_ingest_operations_media_assets_asset_id",
                table: "ingest_operations",
                column: "asset_id",
                principalTable: "media_assets",
                principalColumn: "id",
                onDelete: ReferentialAction.Restrict);

            migrationBuilder.AddForeignKey(
                name: "FK_ingest_operations_media_assets_replacement_asset_id",
                table: "ingest_operations",
                column: "replacement_asset_id",
                principalTable: "media_assets",
                principalColumn: "id",
                onDelete: ReferentialAction.Restrict);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_ingest_operations_media_assets_asset_id",
                table: "ingest_operations");

            migrationBuilder.DropForeignKey(
                name: "FK_ingest_operations_media_assets_replacement_asset_id",
                table: "ingest_operations");

            migrationBuilder.DropTable(
                name: "conflict_matches");

            migrationBuilder.DropTable(
                name: "conflict_cases");

            migrationBuilder.DropIndex(
                name: "IX_media_assets_sha256",
                table: "media_assets");

            migrationBuilder.DropIndex(
                name: "IX_ingest_operations_asset_id",
                table: "ingest_operations");

            migrationBuilder.DropIndex(
                name: "IX_ingest_operations_conflict_case_id",
                table: "ingest_operations");

            migrationBuilder.DropIndex(
                name: "IX_ingest_operations_next_cleanup_attempt_at_utc",
                table: "ingest_operations");

            migrationBuilder.DropIndex(
                name: "IX_ingest_operations_replacement_asset_id",
                table: "ingest_operations");

            migrationBuilder.DropColumn(
                name: "asset_id",
                table: "ingest_operations");

            migrationBuilder.DropColumn(
                name: "backup_relative_path",
                table: "ingest_operations");

            migrationBuilder.DropColumn(
                name: "backup_sha256",
                table: "ingest_operations");

            migrationBuilder.DropColumn(
                name: "backup_size",
                table: "ingest_operations");

            migrationBuilder.DropColumn(
                name: "backup_state",
                table: "ingest_operations");

            migrationBuilder.DropColumn(
                name: "cleanup_attempt_count",
                table: "ingest_operations");

            migrationBuilder.DropColumn(
                name: "conflict_case_id",
                table: "ingest_operations");

            migrationBuilder.DropColumn(
                name: "disposition",
                table: "ingest_operations");

            migrationBuilder.DropColumn(
                name: "expected_target_last_write_time_utc",
                table: "ingest_operations");

            migrationBuilder.DropColumn(
                name: "expected_target_relative_path",
                table: "ingest_operations");

            migrationBuilder.DropColumn(
                name: "expected_target_sha256",
                table: "ingest_operations");

            migrationBuilder.DropColumn(
                name: "expected_target_size",
                table: "ingest_operations");

            migrationBuilder.DropColumn(
                name: "next_cleanup_attempt_at_utc",
                table: "ingest_operations");

            migrationBuilder.DropColumn(
                name: "old_asset_snapshot_json",
                table: "ingest_operations");

            migrationBuilder.DropColumn(
                name: "recovery_blocked_from_stage",
                table: "ingest_operations");

            migrationBuilder.DropColumn(
                name: "replacement_asset_id",
                table: "ingest_operations");

            migrationBuilder.DropColumn(
                name: "replacement_asset_metadata_version",
                table: "ingest_operations");

            migrationBuilder.DropColumn(
                name: "last_attempt_at_utc",
                table: "ingest_cleanup_entries");

            migrationBuilder.DropColumn(
                name: "last_error_code",
                table: "ingest_cleanup_entries");

            // Recreating Stage 3 uniqueness intentionally fails when Stage 4
            // duplicate-hash assets exist. Downgrade never deletes user assets.
            migrationBuilder.CreateIndex(
                name: "IX_media_assets_sha256",
                table: "media_assets",
                column: "sha256",
                unique: true);
        }
    }
}
