using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace TraceFold.Media.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class Stage3ProcessingPipeline : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "ingest_cleanup_entries",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "TEXT", nullable: false),
                    operation_id = table.Column<Guid>(type: "TEXT", nullable: false),
                    ordinal = table.Column<int>(type: "INTEGER", nullable: false),
                    cleanup_kind = table.Column<string>(type: "TEXT", maxLength: 128, nullable: false),
                    controlled_path = table.Column<string>(type: "TEXT", maxLength: 4096, nullable: false),
                    size = table.Column<long>(type: "INTEGER", nullable: false),
                    sha256 = table.Column<string>(type: "TEXT", maxLength: 64, nullable: false),
                    completed_at_utc = table.Column<DateTimeOffset>(type: "TEXT", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ingest_cleanup_entries", x => x.id);
                    table.ForeignKey(
                        name: "FK_ingest_cleanup_entries_ingest_operations_operation_id",
                        column: x => x.operation_id,
                        principalTable: "ingest_operations",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "processing_plans",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "TEXT", nullable: false),
                    inbox_item_id = table.Column<Guid>(type: "TEXT", nullable: false),
                    processor_key = table.Column<string>(type: "TEXT", maxLength: 128, nullable: false),
                    processor_version = table.Column<string>(type: "TEXT", maxLength: 128, nullable: false),
                    processor_display_name = table.Column<string>(type: "TEXT", maxLength: 1024, nullable: false),
                    capability = table.Column<string>(type: "TEXT", maxLength: 128, nullable: false),
                    output_filename = table.Column<string>(type: "TEXT", maxLength: 1024, nullable: false),
                    work_directory_relative_path = table.Column<string>(type: "TEXT", maxLength: 4096, nullable: false),
                    step_keys_json = table.Column<string>(type: "TEXT", nullable: false),
                    created_at_utc = table.Column<DateTimeOffset>(type: "TEXT", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_processing_plans", x => x.id);
                });

            migrationBuilder.CreateTable(
                name: "processing_jobs",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "TEXT", nullable: false),
                    plan_id = table.Column<Guid>(type: "TEXT", nullable: false),
                    inbox_item_id = table.Column<Guid>(type: "TEXT", nullable: false),
                    active_inbox_item_id = table.Column<Guid>(type: "TEXT", nullable: true),
                    attempt = table.Column<int>(type: "INTEGER", nullable: false),
                    status = table.Column<string>(type: "TEXT", maxLength: 64, nullable: false),
                    current_step_key = table.Column<string>(type: "TEXT", maxLength: 128, nullable: true),
                    progress_percent = table.Column<int>(type: "INTEGER", nullable: false),
                    error_code = table.Column<string>(type: "TEXT", maxLength: 128, nullable: true),
                    is_retryable = table.Column<bool>(type: "INTEGER", nullable: false),
                    correlation_id = table.Column<string>(type: "TEXT", maxLength: 64, nullable: false),
                    queued_at_utc = table.Column<DateTimeOffset>(type: "TEXT", nullable: false),
                    started_at_utc = table.Column<DateTimeOffset>(type: "TEXT", nullable: true),
                    finished_at_utc = table.Column<DateTimeOffset>(type: "TEXT", nullable: true),
                    updated_at_utc = table.Column<DateTimeOffset>(type: "TEXT", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_processing_jobs", x => x.id);
                    table.ForeignKey(
                        name: "FK_processing_jobs_inbox_items_inbox_item_id",
                        column: x => x.inbox_item_id,
                        principalTable: "inbox_items",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_processing_jobs_processing_plans_plan_id",
                        column: x => x.plan_id,
                        principalTable: "processing_plans",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "processing_plan_inputs",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "TEXT", nullable: false),
                    plan_id = table.Column<Guid>(type: "TEXT", nullable: false),
                    source_file_id = table.Column<Guid>(type: "TEXT", nullable: false),
                    role = table.Column<string>(type: "TEXT", maxLength: 128, nullable: false),
                    full_path = table.Column<string>(type: "TEXT", maxLength: 4096, nullable: false),
                    filename = table.Column<string>(type: "TEXT", maxLength: 1024, nullable: false),
                    size = table.Column<long>(type: "INTEGER", nullable: false),
                    last_write_time_utc = table.Column<DateTimeOffset>(type: "TEXT", nullable: false),
                    sha256 = table.Column<string>(type: "TEXT", maxLength: 64, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_processing_plan_inputs", x => x.id);
                    table.ForeignKey(
                        name: "FK_processing_plan_inputs_processing_plans_plan_id",
                        column: x => x.plan_id,
                        principalTable: "processing_plans",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "processed_outputs",
                columns: table => new
                {
                    job_id = table.Column<Guid>(type: "TEXT", nullable: false),
                    inbox_item_id = table.Column<Guid>(type: "TEXT", nullable: false),
                    active_inbox_item_id = table.Column<Guid>(type: "TEXT", nullable: true),
                    relative_path = table.Column<string>(type: "TEXT", maxLength: 4096, nullable: false),
                    size = table.Column<long>(type: "INTEGER", nullable: false),
                    last_write_time_utc = table.Column<DateTimeOffset>(type: "TEXT", nullable: false),
                    sha256 = table.Column<string>(type: "TEXT", maxLength: 64, nullable: false),
                    duration_ticks = table.Column<long>(type: "INTEGER", nullable: false),
                    has_video = table.Column<bool>(type: "INTEGER", nullable: false),
                    has_audio = table.Column<bool>(type: "INTEGER", nullable: false),
                    is_valid = table.Column<bool>(type: "INTEGER", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_processed_outputs", x => x.job_id);
                    table.ForeignKey(
                        name: "FK_processed_outputs_processing_jobs_job_id",
                        column: x => x.job_id,
                        principalTable: "processing_jobs",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_ingest_cleanup_entries_operation_id_ordinal",
                table: "ingest_cleanup_entries",
                columns: new[] { "operation_id", "ordinal" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_processed_outputs_active_inbox_item_id",
                table: "processed_outputs",
                column: "active_inbox_item_id",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_processing_jobs_active_inbox_item_id",
                table: "processing_jobs",
                column: "active_inbox_item_id",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_processing_jobs_inbox_item_id",
                table: "processing_jobs",
                column: "inbox_item_id");

            migrationBuilder.CreateIndex(
                name: "IX_processing_jobs_plan_id",
                table: "processing_jobs",
                column: "plan_id",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_processing_plan_inputs_plan_id_role",
                table: "processing_plan_inputs",
                columns: new[] { "plan_id", "role" },
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "ingest_cleanup_entries");

            migrationBuilder.DropTable(
                name: "processed_outputs");

            migrationBuilder.DropTable(
                name: "processing_plan_inputs");

            migrationBuilder.DropTable(
                name: "processing_jobs");

            migrationBuilder.DropTable(
                name: "processing_plans");
        }
    }
}
