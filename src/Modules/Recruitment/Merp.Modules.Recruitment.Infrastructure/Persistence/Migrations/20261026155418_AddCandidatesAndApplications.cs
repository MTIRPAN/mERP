using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Merp.Modules.Recruitment.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddCandidatesAndApplications : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "candidates",
                schema: "recruitment",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    email = table.Column<string>(type: "character varying(254)", maxLength: 254, nullable: false),
                    first_name = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    last_name = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    phone = table.Column<string>(type: "character varying(30)", maxLength: 30, nullable: true),
                    culture = table.Column<string>(type: "character varying(10)", maxLength: 10, nullable: true),
                    source = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: false),
                    employee_id = table.Column<Guid>(type: "uuid", nullable: true),
                    cv_file_id = table.Column<Guid>(type: "uuid", nullable: true),
                    pool_consent_revision = table.Column<int>(type: "integer", nullable: true),
                    pool_consent_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    last_applied_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    xmin = table.Column<uint>(type: "xid", rowVersion: true, nullable: false),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    created_by = table.Column<Guid>(type: "uuid", nullable: true),
                    modified_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    modified_by = table.Column<Guid>(type: "uuid", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_candidates", x => x.id);
                });

            migrationBuilder.CreateTable(
                name: "job_applications",
                schema: "recruitment",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    candidate_id = table.Column<Guid>(type: "uuid", nullable: false),
                    opening_id = table.Column<Guid>(type: "uuid", nullable: false),
                    company_id = table.Column<Guid>(type: "uuid", nullable: false),
                    stage_id = table.Column<Guid>(type: "uuid", nullable: false),
                    status = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    cover_letter = table.Column<string>(type: "character varying(4000)", maxLength: 4000, nullable: true),
                    answers = table.Column<string>(type: "jsonb", nullable: false),
                    knocked_out = table.Column<bool>(type: "boolean", nullable: false),
                    cv_file_id = table.Column<Guid>(type: "uuid", nullable: true),
                    consent = table.Column<string>(type: "jsonb", nullable: false),
                    is_internal = table.Column<bool>(type: "boolean", nullable: false),
                    source = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: false),
                    applied_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    xmin = table.Column<uint>(type: "xid", rowVersion: true, nullable: false),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    created_by = table.Column<Guid>(type: "uuid", nullable: true),
                    modified_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    modified_by = table.Column<Guid>(type: "uuid", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_job_applications", x => x.id);
                });

            migrationBuilder.CreateIndex(
                name: "ix_candidates_email",
                schema: "recruitment",
                table: "candidates",
                column: "email",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_candidates_employee_id",
                schema: "recruitment",
                table: "candidates",
                column: "employee_id");

            migrationBuilder.CreateIndex(
                name: "ix_job_applications_candidate_id_opening_id",
                schema: "recruitment",
                table: "job_applications",
                columns: new[] { "candidate_id", "opening_id" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_job_applications_company_id_applied_at",
                schema: "recruitment",
                table: "job_applications",
                columns: new[] { "company_id", "applied_at" });

            migrationBuilder.CreateIndex(
                name: "ix_job_applications_opening_id_stage_id",
                schema: "recruitment",
                table: "job_applications",
                columns: new[] { "opening_id", "stage_id" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "candidates",
                schema: "recruitment");

            migrationBuilder.DropTable(
                name: "job_applications",
                schema: "recruitment");
        }
    }
}
