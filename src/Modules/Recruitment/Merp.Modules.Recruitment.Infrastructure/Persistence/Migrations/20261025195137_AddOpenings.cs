using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Merp.Modules.Recruitment.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddOpenings : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "job_openings",
                schema: "recruitment",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    company_id = table.Column<Guid>(type: "uuid", nullable: false),
                    requisition_id = table.Column<Guid>(type: "uuid", nullable: true),
                    title = table.Column<string>(type: "character varying(150)", maxLength: 150, nullable: false),
                    description = table.Column<string>(type: "character varying(20000)", maxLength: 20000, nullable: true),
                    qualifications = table.Column<string>(type: "character varying(20000)", maxLength: 20000, nullable: true),
                    branch_id = table.Column<Guid>(type: "uuid", nullable: true),
                    work_model = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    employment_type = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    headcount = table.Column<int>(type: "integer", nullable: false),
                    visibility = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    publish_from = table.Column<DateOnly>(type: "date", nullable: true),
                    publish_until = table.Column<DateOnly>(type: "date", nullable: true),
                    hiring_manager_user_id = table.Column<Guid>(type: "uuid", nullable: false),
                    team = table.Column<string>(type: "jsonb", nullable: false),
                    form = table.Column<string>(type: "jsonb", nullable: false),
                    stage_template_id = table.Column<Guid>(type: "uuid", nullable: false),
                    stages = table.Column<string>(type: "jsonb", nullable: false),
                    status = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    published_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    closed_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    close_reason = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: true),
                    xmin = table.Column<uint>(type: "xid", rowVersion: true, nullable: false),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    created_by = table.Column<Guid>(type: "uuid", nullable: true),
                    modified_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    modified_by = table.Column<Guid>(type: "uuid", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_job_openings", x => x.id);
                });

            migrationBuilder.CreateIndex(
                name: "ix_job_openings_company_id_created_at",
                schema: "recruitment",
                table: "job_openings",
                columns: new[] { "company_id", "created_at" });

            migrationBuilder.CreateIndex(
                name: "ix_job_openings_requisition_id",
                schema: "recruitment",
                table: "job_openings",
                column: "requisition_id");

            migrationBuilder.CreateIndex(
                name: "ix_job_openings_status_visibility",
                schema: "recruitment",
                table: "job_openings",
                columns: new[] { "status", "visibility" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "job_openings",
                schema: "recruitment");
        }
    }
}
