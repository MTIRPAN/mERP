using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Merp.Modules.HR.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddLeaveRequests : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "leave_requests",
                schema: "hr",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    number = table.Column<string>(type: "character varying(30)", maxLength: 30, nullable: false),
                    employee_id = table.Column<Guid>(type: "uuid", nullable: false),
                    employment_id = table.Column<Guid>(type: "uuid", nullable: false),
                    company_id = table.Column<Guid>(type: "uuid", nullable: false),
                    requester_user_id = table.Column<Guid>(type: "uuid", nullable: false),
                    origin = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    status = table.Column<string>(type: "character varying(30)", maxLength: 30, nullable: false),
                    leave_type_id = table.Column<Guid>(type: "uuid", nullable: false),
                    leave_type_code = table.Column<string>(type: "character varying(30)", maxLength: 30, nullable: false),
                    deducts_from = table.Column<string>(type: "character varying(30)", maxLength: 30, nullable: true),
                    start_date = table.Column<DateOnly>(type: "date", nullable: false),
                    end_date = table.Column<DateOnly>(type: "date", nullable: false),
                    start_part = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    end_part = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    start_time = table.Column<TimeOnly>(type: "time without time zone", nullable: true),
                    end_time = table.Column<TimeOnly>(type: "time without time zone", nullable: true),
                    event_date = table.Column<DateOnly>(type: "date", nullable: true),
                    travel_days = table.Column<int>(type: "integer", nullable: false),
                    basis = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    quantity = table.Column<decimal>(type: "numeric(7,2)", precision: 7, scale: 2, nullable: false),
                    days = table.Column<decimal>(type: "numeric(7,2)", precision: 7, scale: 2, nullable: false),
                    note = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: true),
                    address = table.Column<string>(type: "character varying(300)", maxLength: 300, nullable: true),
                    phone = table.Column<string>(type: "character varying(30)", maxLength: 30, nullable: true),
                    substitute_employee_id = table.Column<Guid>(type: "uuid", nullable: true),
                    return_comment = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: true),
                    cancellation_reason = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: true),
                    cancellation_requested_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    original_end_date = table.Column<DateOnly>(type: "date", nullable: true),
                    submitted_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    decided_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    reminded_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    xmin = table.Column<uint>(type: "xid", rowVersion: true, nullable: false),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    created_by = table.Column<Guid>(type: "uuid", nullable: true),
                    modified_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    modified_by = table.Column<Guid>(type: "uuid", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_leave_requests", x => x.id);
                });

            migrationBuilder.CreateIndex(
                name: "ix_leave_requests_company_id_number",
                schema: "hr",
                table: "leave_requests",
                columns: new[] { "company_id", "number" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_leave_requests_employee_id_start_date",
                schema: "hr",
                table: "leave_requests",
                columns: new[] { "employee_id", "start_date" });

            migrationBuilder.CreateIndex(
                name: "ix_leave_requests_employment_id_status",
                schema: "hr",
                table: "leave_requests",
                columns: new[] { "employment_id", "status" });

            migrationBuilder.CreateIndex(
                name: "ix_leave_requests_status_start_date",
                schema: "hr",
                table: "leave_requests",
                columns: new[] { "status", "start_date" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "leave_requests",
                schema: "hr");
        }
    }
}
