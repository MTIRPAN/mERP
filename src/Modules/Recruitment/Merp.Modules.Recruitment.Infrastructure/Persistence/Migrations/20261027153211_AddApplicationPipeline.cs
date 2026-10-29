using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Merp.Modules.Recruitment.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddApplicationPipeline : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<Guid>(
                name: "previous_stage_id",
                schema: "recruitment",
                table: "job_applications",
                type: "uuid",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "reason_code",
                schema: "recruitment",
                table: "job_applications",
                type: "character varying(30)",
                maxLength: 30,
                nullable: true);

            migrationBuilder.AddColumn<DateTimeOffset>(
                name: "rejection_email_due_at",
                schema: "recruitment",
                table: "job_applications",
                type: "timestamp with time zone",
                nullable: true);

            migrationBuilder.AddColumn<DateTimeOffset>(
                name: "rejection_email_sent_at",
                schema: "recruitment",
                table: "job_applications",
                type: "timestamp with time zone",
                nullable: true);

            migrationBuilder.AddColumn<DateTimeOffset>(
                name: "stage_entered_at",
                schema: "recruitment",
                table: "job_applications",
                type: "timestamp with time zone",
                nullable: false,
                defaultValue: new DateTimeOffset(new DateTime(1, 1, 1, 0, 0, 0, 0, DateTimeKind.Unspecified), new TimeSpan(0, 0, 0, 0, 0)));

            migrationBuilder.CreateTable(
                name: "application_stage_changes",
                schema: "recruitment",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    application_id = table.Column<Guid>(type: "uuid", nullable: false),
                    sequence = table.Column<int>(type: "integer", nullable: false),
                    from_stage_id = table.Column<Guid>(type: "uuid", nullable: true),
                    to_stage_id = table.Column<Guid>(type: "uuid", nullable: false),
                    status = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    reason_code = table.Column<string>(type: "character varying(30)", maxLength: 30, nullable: true),
                    note = table.Column<string>(type: "character varying(1000)", maxLength: 1000, nullable: true),
                    user_id = table.Column<Guid>(type: "uuid", nullable: true),
                    at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    xmin = table.Column<uint>(type: "xid", rowVersion: true, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_application_stage_changes", x => x.id);
                    table.ForeignKey(
                        name: "fk_application_stage_changes_job_applications_application_id",
                        column: x => x.application_id,
                        principalSchema: "recruitment",
                        principalTable: "job_applications",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            // Applications of R3b came in at the Applied stage and have not moved since
            migrationBuilder.Sql("""
                UPDATE recruitment.job_applications SET stage_entered_at = applied_at;
                INSERT INTO recruitment.application_stage_changes (id, application_id, sequence, from_stage_id, to_stage_id, status, at)
                SELECT gen_random_uuid(), id, 1, NULL, stage_id, status, applied_at FROM recruitment.job_applications;
                """);

            migrationBuilder.CreateIndex(
                name: "ix_job_applications_rejection_email_due",
                schema: "recruitment",
                table: "job_applications",
                column: "rejection_email_due_at",
                filter: "rejection_email_due_at IS NOT NULL");

            migrationBuilder.CreateIndex(
                name: "ix_application_stage_changes_application_id_sequence",
                schema: "recruitment",
                table: "application_stage_changes",
                columns: new[] { "application_id", "sequence" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "application_stage_changes",
                schema: "recruitment");

            migrationBuilder.DropIndex(
                name: "ix_job_applications_rejection_email_due",
                schema: "recruitment",
                table: "job_applications");

            migrationBuilder.DropColumn(
                name: "previous_stage_id",
                schema: "recruitment",
                table: "job_applications");

            migrationBuilder.DropColumn(
                name: "reason_code",
                schema: "recruitment",
                table: "job_applications");

            migrationBuilder.DropColumn(
                name: "rejection_email_due_at",
                schema: "recruitment",
                table: "job_applications");

            migrationBuilder.DropColumn(
                name: "rejection_email_sent_at",
                schema: "recruitment",
                table: "job_applications");

            migrationBuilder.DropColumn(
                name: "stage_entered_at",
                schema: "recruitment",
                table: "job_applications");
        }
    }
}
