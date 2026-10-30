using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Merp.Modules.Recruitment.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddJobOffers : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "job_offers",
                schema: "recruitment",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    number = table.Column<string>(type: "character varying(30)", maxLength: 30, nullable: false),
                    application_id = table.Column<Guid>(type: "uuid", nullable: false),
                    opening_id = table.Column<Guid>(type: "uuid", nullable: false),
                    candidate_id = table.Column<Guid>(type: "uuid", nullable: false),
                    company_id = table.Column<Guid>(type: "uuid", nullable: false),
                    org_unit_id = table.Column<Guid>(type: "uuid", nullable: true),
                    hiring_manager_user_id = table.Column<Guid>(type: "uuid", nullable: false),
                    requester_user_id = table.Column<Guid>(type: "uuid", nullable: false),
                    position_title = table.Column<string>(type: "character varying(150)", maxLength: 150, nullable: false),
                    grade = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: true),
                    start_date = table.Column<DateOnly>(type: "date", nullable: false),
                    salary = table.Column<decimal>(type: "numeric(18,2)", precision: 18, scale: 2, nullable: false),
                    currency = table.Column<string>(type: "character varying(3)", maxLength: 3, nullable: false),
                    period = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    benefits = table.Column<string>(type: "character varying(2000)", maxLength: 2000, nullable: true),
                    expires_on = table.Column<DateOnly>(type: "date", nullable: false),
                    is_above_budget = table.Column<bool>(type: "boolean", nullable: false),
                    status = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    return_comment = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: true),
                    withdrawal_reason = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: true),
                    submitted_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    decided_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    sent_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    letter_file_id = table.Column<Guid>(type: "uuid", nullable: true),
                    response_token_hash = table.Column<string>(type: "character(64)", fixedLength: true, maxLength: 64, nullable: true),
                    responded_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    responded_via = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: true),
                    responded_by_user_id = table.Column<Guid>(type: "uuid", nullable: true),
                    response_note = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: true),
                    xmin = table.Column<uint>(type: "xid", rowVersion: true, nullable: false),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    created_by = table.Column<Guid>(type: "uuid", nullable: true),
                    modified_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    modified_by = table.Column<Guid>(type: "uuid", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_job_offers", x => x.id);
                });

            migrationBuilder.CreateIndex(
                name: "ix_job_offers_application_id",
                schema: "recruitment",
                table: "job_offers",
                column: "application_id");

            migrationBuilder.CreateIndex(
                name: "ix_job_offers_company_id_number",
                schema: "recruitment",
                table: "job_offers",
                columns: new[] { "company_id", "number" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_job_offers_response_token_hash",
                schema: "recruitment",
                table: "job_offers",
                column: "response_token_hash",
                unique: true,
                filter: "response_token_hash IS NOT NULL");

            migrationBuilder.CreateIndex(
                name: "ix_job_offers_sent_expires_on",
                schema: "recruitment",
                table: "job_offers",
                column: "expires_on",
                filter: "status = 'Sent'");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "job_offers",
                schema: "recruitment");
        }
    }
}
