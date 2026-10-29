using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Merp.Modules.Recruitment.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddInterviews : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "evaluation_templates",
                schema: "recruitment",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    name = table.Column<string>(type: "character varying(150)", maxLength: 150, nullable: false),
                    form = table.Column<string>(type: "jsonb", nullable: false),
                    is_active = table.Column<bool>(type: "boolean", nullable: false),
                    xmin = table.Column<uint>(type: "xid", rowVersion: true, nullable: false),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    created_by = table.Column<Guid>(type: "uuid", nullable: true),
                    modified_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    modified_by = table.Column<Guid>(type: "uuid", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_evaluation_templates", x => x.id);
                });

            migrationBuilder.CreateTable(
                name: "interview_feedback",
                schema: "recruitment",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    interview_id = table.Column<Guid>(type: "uuid", nullable: false),
                    application_id = table.Column<Guid>(type: "uuid", nullable: false),
                    interviewer_user_id = table.Column<Guid>(type: "uuid", nullable: false),
                    scores = table.Column<string>(type: "jsonb", nullable: false),
                    answers = table.Column<string>(type: "jsonb", nullable: false),
                    recommendation = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    summary = table.Column<string>(type: "character varying(4000)", maxLength: 4000, nullable: true),
                    weighted_score = table.Column<decimal>(type: "numeric(4,2)", precision: 4, scale: 2, nullable: false),
                    submitted_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    xmin = table.Column<uint>(type: "xid", rowVersion: true, nullable: false),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    created_by = table.Column<Guid>(type: "uuid", nullable: true),
                    modified_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    modified_by = table.Column<Guid>(type: "uuid", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_interview_feedback", x => x.id);
                });

            migrationBuilder.CreateTable(
                name: "interviews",
                schema: "recruitment",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    application_id = table.Column<Guid>(type: "uuid", nullable: false),
                    candidate_id = table.Column<Guid>(type: "uuid", nullable: false),
                    opening_id = table.Column<Guid>(type: "uuid", nullable: false),
                    company_id = table.Column<Guid>(type: "uuid", nullable: false),
                    type = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    starts_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    ends_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    location = table.Column<string>(type: "character varying(300)", maxLength: 300, nullable: true),
                    online_link = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: true),
                    interviewer_user_ids = table.Column<Guid[]>(type: "uuid[]", nullable: false),
                    organizer_user_id = table.Column<Guid>(type: "uuid", nullable: false),
                    notes = table.Column<string>(type: "character varying(2000)", maxLength: 2000, nullable: true),
                    status = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    cancel_reason = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: true),
                    evaluation = table.Column<string>(type: "jsonb", nullable: true),
                    sequence = table.Column<int>(type: "integer", nullable: false),
                    day_reminder_sent_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    hour_reminder_sent_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    xmin = table.Column<uint>(type: "xid", rowVersion: true, nullable: false),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    created_by = table.Column<Guid>(type: "uuid", nullable: true),
                    modified_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    modified_by = table.Column<Guid>(type: "uuid", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_interviews", x => x.id);
                });

            migrationBuilder.CreateIndex(
                name: "ix_interview_feedback_application_id",
                schema: "recruitment",
                table: "interview_feedback",
                column: "application_id");

            migrationBuilder.CreateIndex(
                name: "ix_interview_feedback_interview_id_interviewer_user_id",
                schema: "recruitment",
                table: "interview_feedback",
                columns: new[] { "interview_id", "interviewer_user_id" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_interviews_application_id",
                schema: "recruitment",
                table: "interviews",
                column: "application_id");

            migrationBuilder.CreateIndex(
                name: "ix_interviews_company_id_starts_at",
                schema: "recruitment",
                table: "interviews",
                columns: new[] { "company_id", "starts_at" });

            migrationBuilder.CreateIndex(
                name: "ix_interviews_interviewer_user_ids",
                schema: "recruitment",
                table: "interviews",
                column: "interviewer_user_ids")
                .Annotation("Npgsql:IndexMethod", "gin");

            migrationBuilder.CreateIndex(
                name: "ix_interviews_scheduled_starts_at",
                schema: "recruitment",
                table: "interviews",
                column: "starts_at",
                filter: "status = 'Scheduled'");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "evaluation_templates",
                schema: "recruitment");

            migrationBuilder.DropTable(
                name: "interview_feedback",
                schema: "recruitment");

            migrationBuilder.DropTable(
                name: "interviews",
                schema: "recruitment");
        }
    }
}
