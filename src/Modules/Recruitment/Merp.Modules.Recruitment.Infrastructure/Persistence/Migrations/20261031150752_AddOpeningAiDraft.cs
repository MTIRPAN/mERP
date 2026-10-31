using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Merp.Modules.Recruitment.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddOpeningAiDraft : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<DateTimeOffset>(
                name: "ai_draft_reviewed_at",
                schema: "recruitment",
                table: "job_openings",
                type: "timestamp with time zone",
                nullable: true);

            migrationBuilder.AddColumn<Guid>(
                name: "ai_draft_reviewed_by_user_id",
                schema: "recruitment",
                table: "job_openings",
                type: "uuid",
                nullable: true);

            migrationBuilder.AddColumn<DateTimeOffset>(
                name: "ai_drafted_at",
                schema: "recruitment",
                table: "job_openings",
                type: "timestamp with time zone",
                nullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "ai_draft_reviewed_at",
                schema: "recruitment",
                table: "job_openings");

            migrationBuilder.DropColumn(
                name: "ai_draft_reviewed_by_user_id",
                schema: "recruitment",
                table: "job_openings");

            migrationBuilder.DropColumn(
                name: "ai_drafted_at",
                schema: "recruitment",
                table: "job_openings");
        }
    }
}
