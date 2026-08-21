using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace TraceFold.Media.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class Stage2MediaLibrary : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<bool>(
                name: "AutoScanEnabled",
                table: "app_settings",
                type: "INTEGER",
                nullable: false,
                defaultValue: false);

            migrationBuilder.AddColumn<DateTimeOffset>(
                name: "LastScanAtUtc",
                table: "app_settings",
                type: "TEXT",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "LastScanSummary",
                table: "app_settings",
                type: "TEXT",
                maxLength: 1024,
                nullable: true);

            migrationBuilder.CreateTable(
                name: "inbox_items",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "TEXT", nullable: false),
                    state = table.Column<string>(type: "TEXT", maxLength: 64, nullable: false),
                    media_kind = table.Column<string>(type: "TEXT", maxLength: 32, nullable: false),
                    actual_filename = table.Column<string>(type: "TEXT", maxLength: 1024, nullable: false),
                    grouping_rule = table.Column<string>(type: "TEXT", maxLength: 128, nullable: false),
                    required_capability = table.Column<string>(type: "TEXT", maxLength: 128, nullable: true),
                    error_code = table.Column<string>(type: "TEXT", maxLength: 128, nullable: true),
                    is_retryable = table.Column<bool>(type: "INTEGER", nullable: false),
                    created_at_utc = table.Column<DateTimeOffset>(type: "TEXT", nullable: false),
                    updated_at_utc = table.Column<DateTimeOffset>(type: "TEXT", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_inbox_items", x => x.id);
                });

            migrationBuilder.CreateTable(
                name: "taxonomy_terms",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "TEXT", nullable: false),
                    kind = table.Column<string>(type: "TEXT", maxLength: 32, nullable: false),
                    display_name = table.Column<string>(type: "TEXT", maxLength: 512, nullable: false),
                    normalized_name = table.Column<string>(type: "TEXT", maxLength: 512, nullable: false),
                    created_at_utc = table.Column<DateTimeOffset>(type: "TEXT", nullable: false),
                    updated_at_utc = table.Column<DateTimeOffset>(type: "TEXT", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_taxonomy_terms", x => x.id);
                });

            migrationBuilder.CreateTable(
                name: "ingest_operations",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "TEXT", nullable: false),
                    inbox_item_id = table.Column<Guid>(type: "TEXT", nullable: false),
                    active_inbox_item_id = table.Column<Guid>(type: "TEXT", nullable: true),
                    stage = table.Column<string>(type: "TEXT", maxLength: 64, nullable: false),
                    source_path = table.Column<string>(type: "TEXT", maxLength: 4096, nullable: false),
                    source_size = table.Column<long>(type: "INTEGER", nullable: false),
                    source_last_write_time_utc = table.Column<DateTimeOffset>(type: "TEXT", nullable: false),
                    source_sha256 = table.Column<string>(type: "TEXT", maxLength: 64, nullable: false),
                    target_filename = table.Column<string>(type: "TEXT", maxLength: 1024, nullable: false),
                    staged_path = table.Column<string>(type: "TEXT", maxLength: 4096, nullable: true),
                    target_temporary_path = table.Column<string>(type: "TEXT", maxLength: 4096, nullable: true),
                    metadata_snapshot_json = table.Column<string>(type: "TEXT", nullable: false),
                    error_code = table.Column<string>(type: "TEXT", maxLength: 128, nullable: true),
                    created_at_utc = table.Column<DateTimeOffset>(type: "TEXT", nullable: false),
                    updated_at_utc = table.Column<DateTimeOffset>(type: "TEXT", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ingest_operations", x => x.id);
                    table.ForeignKey(
                        name: "FK_ingest_operations_inbox_items_inbox_item_id",
                        column: x => x.inbox_item_id,
                        principalTable: "inbox_items",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "source_files",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "TEXT", nullable: false),
                    inbox_item_id = table.Column<Guid>(type: "TEXT", nullable: false),
                    normalized_path = table.Column<string>(type: "TEXT", maxLength: 4096, nullable: false),
                    active_path_key = table.Column<string>(type: "TEXT", maxLength: 4096, nullable: true),
                    filename = table.Column<string>(type: "TEXT", maxLength: 1024, nullable: false),
                    extension = table.Column<string>(type: "TEXT", maxLength: 32, nullable: false),
                    size = table.Column<long>(type: "INTEGER", nullable: false),
                    last_write_time_utc = table.Column<DateTimeOffset>(type: "TEXT", nullable: false),
                    first_observed_at_utc = table.Column<DateTimeOffset>(type: "TEXT", nullable: false),
                    stable_observation_count = table.Column<int>(type: "INTEGER", nullable: false),
                    sha256 = table.Column<string>(type: "TEXT", maxLength: 64, nullable: true),
                    is_present = table.Column<bool>(type: "INTEGER", nullable: false),
                    is_primary = table.Column<bool>(type: "INTEGER", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_source_files", x => x.id);
                    table.ForeignKey(
                        name: "FK_source_files_inbox_items_inbox_item_id",
                        column: x => x.inbox_item_id,
                        principalTable: "inbox_items",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "inbox_draft_authors",
                columns: table => new
                {
                    inbox_item_id = table.Column<Guid>(type: "TEXT", nullable: false),
                    author_id = table.Column<Guid>(type: "TEXT", nullable: false),
                    ordinal = table.Column<int>(type: "INTEGER", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_inbox_draft_authors", x => new { x.inbox_item_id, x.author_id });
                    table.ForeignKey(
                        name: "FK_inbox_draft_authors_inbox_items_inbox_item_id",
                        column: x => x.inbox_item_id,
                        principalTable: "inbox_items",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_inbox_draft_authors_taxonomy_terms_author_id",
                        column: x => x.author_id,
                        principalTable: "taxonomy_terms",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "inbox_draft_tags",
                columns: table => new
                {
                    inbox_item_id = table.Column<Guid>(type: "TEXT", nullable: false),
                    tag_id = table.Column<Guid>(type: "TEXT", nullable: false),
                    ordinal = table.Column<int>(type: "INTEGER", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_inbox_draft_tags", x => new { x.inbox_item_id, x.tag_id });
                    table.ForeignKey(
                        name: "FK_inbox_draft_tags_inbox_items_inbox_item_id",
                        column: x => x.inbox_item_id,
                        principalTable: "inbox_items",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_inbox_draft_tags_taxonomy_terms_tag_id",
                        column: x => x.tag_id,
                        principalTable: "taxonomy_terms",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "media_assets",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "TEXT", nullable: false),
                    media_kind = table.Column<string>(type: "TEXT", maxLength: 32, nullable: false),
                    actual_filename = table.Column<string>(type: "TEXT", maxLength: 1024, nullable: false),
                    relative_path = table.Column<string>(type: "TEXT", maxLength: 4096, nullable: false),
                    normalized_relative_path = table.Column<string>(type: "TEXT", maxLength: 4096, nullable: false),
                    display_name = table.Column<string>(type: "TEXT", maxLength: 1024, nullable: true),
                    size = table.Column<long>(type: "INTEGER", nullable: false),
                    sha256 = table.Column<string>(type: "TEXT", maxLength: 64, nullable: false),
                    series_id = table.Column<Guid>(type: "TEXT", nullable: true),
                    metadata_version = table.Column<long>(type: "INTEGER", nullable: false),
                    created_at_utc = table.Column<DateTimeOffset>(type: "TEXT", nullable: false),
                    updated_at_utc = table.Column<DateTimeOffset>(type: "TEXT", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_media_assets", x => x.id);
                    table.ForeignKey(
                        name: "FK_media_assets_taxonomy_terms_series_id",
                        column: x => x.series_id,
                        principalTable: "taxonomy_terms",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "metadata_drafts",
                columns: table => new
                {
                    inbox_item_id = table.Column<Guid>(type: "TEXT", nullable: false),
                    display_name = table.Column<string>(type: "TEXT", maxLength: 1024, nullable: true),
                    series_id = table.Column<Guid>(type: "TEXT", nullable: true),
                    version = table.Column<long>(type: "INTEGER", nullable: false),
                    updated_at_utc = table.Column<DateTimeOffset>(type: "TEXT", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_metadata_drafts", x => x.inbox_item_id);
                    table.ForeignKey(
                        name: "FK_metadata_drafts_inbox_items_inbox_item_id",
                        column: x => x.inbox_item_id,
                        principalTable: "inbox_items",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_metadata_drafts_taxonomy_terms_series_id",
                        column: x => x.series_id,
                        principalTable: "taxonomy_terms",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "media_asset_authors",
                columns: table => new
                {
                    media_asset_id = table.Column<Guid>(type: "TEXT", nullable: false),
                    author_id = table.Column<Guid>(type: "TEXT", nullable: false),
                    ordinal = table.Column<int>(type: "INTEGER", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_media_asset_authors", x => new { x.media_asset_id, x.author_id });
                    table.ForeignKey(
                        name: "FK_media_asset_authors_media_assets_media_asset_id",
                        column: x => x.media_asset_id,
                        principalTable: "media_assets",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_media_asset_authors_taxonomy_terms_author_id",
                        column: x => x.author_id,
                        principalTable: "taxonomy_terms",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "media_asset_tags",
                columns: table => new
                {
                    media_asset_id = table.Column<Guid>(type: "TEXT", nullable: false),
                    tag_id = table.Column<Guid>(type: "TEXT", nullable: false),
                    ordinal = table.Column<int>(type: "INTEGER", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_media_asset_tags", x => new { x.media_asset_id, x.tag_id });
                    table.ForeignKey(
                        name: "FK_media_asset_tags_media_assets_media_asset_id",
                        column: x => x.media_asset_id,
                        principalTable: "media_assets",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_media_asset_tags_taxonomy_terms_tag_id",
                        column: x => x.tag_id,
                        principalTable: "taxonomy_terms",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "IX_inbox_draft_authors_author_id",
                table: "inbox_draft_authors",
                column: "author_id");

            migrationBuilder.CreateIndex(
                name: "IX_inbox_draft_tags_tag_id",
                table: "inbox_draft_tags",
                column: "tag_id");

            migrationBuilder.CreateIndex(
                name: "IX_ingest_operations_active_inbox_item_id",
                table: "ingest_operations",
                column: "active_inbox_item_id",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_ingest_operations_inbox_item_id",
                table: "ingest_operations",
                column: "inbox_item_id");

            migrationBuilder.CreateIndex(
                name: "IX_media_asset_authors_author_id",
                table: "media_asset_authors",
                column: "author_id");

            migrationBuilder.CreateIndex(
                name: "IX_media_asset_tags_tag_id",
                table: "media_asset_tags",
                column: "tag_id");

            migrationBuilder.CreateIndex(
                name: "IX_media_assets_normalized_relative_path",
                table: "media_assets",
                column: "normalized_relative_path",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_media_assets_series_id",
                table: "media_assets",
                column: "series_id");

            migrationBuilder.CreateIndex(
                name: "IX_media_assets_sha256",
                table: "media_assets",
                column: "sha256",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_metadata_drafts_series_id",
                table: "metadata_drafts",
                column: "series_id");

            migrationBuilder.CreateIndex(
                name: "IX_source_files_active_path_key",
                table: "source_files",
                column: "active_path_key",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_source_files_inbox_item_id",
                table: "source_files",
                column: "inbox_item_id");

            migrationBuilder.CreateIndex(
                name: "IX_taxonomy_terms_kind_normalized_name",
                table: "taxonomy_terms",
                columns: new[] { "kind", "normalized_name" },
                unique: true);

        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "inbox_draft_authors");

            migrationBuilder.DropTable(
                name: "inbox_draft_tags");

            migrationBuilder.DropTable(
                name: "ingest_operations");

            migrationBuilder.DropTable(
                name: "media_asset_authors");

            migrationBuilder.DropTable(
                name: "media_asset_tags");

            migrationBuilder.DropTable(
                name: "metadata_drafts");

            migrationBuilder.DropTable(
                name: "source_files");

            migrationBuilder.DropTable(
                name: "media_assets");

            migrationBuilder.DropTable(
                name: "inbox_items");

            migrationBuilder.DropTable(
                name: "taxonomy_terms");

            migrationBuilder.DropColumn(
                name: "AutoScanEnabled",
                table: "app_settings");

            migrationBuilder.DropColumn(
                name: "LastScanAtUtc",
                table: "app_settings");

            migrationBuilder.DropColumn(
                name: "LastScanSummary",
                table: "app_settings");
        }
    }
}
