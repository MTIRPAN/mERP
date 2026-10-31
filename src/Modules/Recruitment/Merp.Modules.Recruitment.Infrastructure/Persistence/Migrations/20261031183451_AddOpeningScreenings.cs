using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Merp.Modules.Recruitment.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddOpeningScreenings : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "requirements_hash",
                schema: "recruitment",
                table: "application_cv_analyses",
                type: "character(64)",
                fixedLength: true,
                maxLength: 64,
                nullable: true);

            migrationBuilder.CreateTable(
                name: "opening_screenings",
                schema: "recruitment",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    opening_id = table.Column<Guid>(type: "uuid", nullable: false),
                    company_id = table.Column<Guid>(type: "uuid", nullable: false),
                    status = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    requested_by_user_id = table.Column<Guid>(type: "uuid", nullable: false),
                    requested_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    batch_id = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: true),
                    submitted_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    items = table.Column<string>(type: "jsonb", nullable: false),
                    requirements_hash = table.Column<string>(type: "character(64)", fixedLength: true, maxLength: 64, nullable: true),
                    estimated_cost_usd = table.Column<decimal>(type: "numeric(12,4)", precision: 12, scale: 4, nullable: true),
                    hold_reason = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: true),
                    succeeded_count = table.Column<int>(type: "integer", nullable: false),
                    failed_count = table.Column<int>(type: "integer", nullable: false),
                    completed_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    cancelled_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    cancelled_by_user_id = table.Column<Guid>(type: "uuid", nullable: true),
                    xmin = table.Column<uint>(type: "xid", rowVersion: true, nullable: false),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    created_by = table.Column<Guid>(type: "uuid", nullable: true),
                    modified_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    modified_by = table.Column<Guid>(type: "uuid", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_opening_screenings", x => x.id);
                });

            migrationBuilder.CreateIndex(
                name: "ix_opening_screenings_opening_id_requested_at",
                schema: "recruitment",
                table: "opening_screenings",
                columns: new[] { "opening_id", "requested_at" });

            migrationBuilder.CreateIndex(
                name: "ux_opening_screenings_open",
                schema: "recruitment",
                table: "opening_screenings",
                column: "opening_id",
                unique: true,
                filter: "status IN ('Queued', 'Submitted', 'Held')");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "opening_screenings",
                schema: "recruitment");

            migrationBuilder.DropColumn(
                name: "requirements_hash",
                schema: "recruitment",
                table: "application_cv_analyses");
        }
    }
}
