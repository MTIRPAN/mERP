using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Merp.Modules.HR.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddEmployees : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "employees",
                schema: "hr",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    tckn = table.Column<string>(type: "character varying(11)", maxLength: 11, nullable: false),
                    first_name = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    last_name = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    birth_date = table.Column<DateOnly>(type: "date", nullable: true),
                    birth_place = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: true),
                    gender = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: true),
                    nationality = table.Column<string>(type: "character varying(2)", maxLength: 2, nullable: true),
                    marital_status = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: true),
                    military_status = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: true),
                    iban = table.Column<string>(type: "character varying(34)", maxLength: 34, nullable: true),
                    user_id = table.Column<Guid>(type: "uuid", nullable: true),
                    emails = table.Column<string>(type: "jsonb", nullable: false),
                    phones = table.Column<string>(type: "jsonb", nullable: false),
                    addresses = table.Column<string>(type: "jsonb", nullable: false),
                    emergency_contacts = table.Column<string>(type: "jsonb", nullable: false),
                    family_members = table.Column<string>(type: "jsonb", nullable: false),
                    educations = table.Column<string>(type: "jsonb", nullable: false),
                    certificates = table.Column<string>(type: "jsonb", nullable: false),
                    languages = table.Column<string>(type: "jsonb", nullable: false),
                    custom_fields = table.Column<string>(type: "jsonb", nullable: false),
                    xmin = table.Column<uint>(type: "xid", rowVersion: true, nullable: false),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    created_by = table.Column<Guid>(type: "uuid", nullable: true),
                    modified_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    modified_by = table.Column<Guid>(type: "uuid", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_employees", x => x.id);
                });

            migrationBuilder.CreateTable(
                name: "sgk_exit_codes",
                schema: "hr",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    code = table.Column<string>(type: "character varying(10)", maxLength: 10, nullable: false),
                    name = table.Column<string>(type: "jsonb", nullable: false),
                    is_active = table.Column<bool>(type: "boolean", nullable: false),
                    xmin = table.Column<uint>(type: "xid", rowVersion: true, nullable: false),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    created_by = table.Column<Guid>(type: "uuid", nullable: true),
                    modified_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    modified_by = table.Column<Guid>(type: "uuid", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_sgk_exit_codes", x => x.id);
                });

            migrationBuilder.CreateTable(
                name: "employee_sensitive_data",
                schema: "hr",
                columns: table => new
                {
                    employee_id = table.Column<Guid>(type: "uuid", nullable: false),
                    disability_degree = table.Column<string>(type: "text", nullable: true),
                    blood_type = table.Column<string>(type: "text", nullable: true),
                    health_notes = table.Column<string>(type: "text", nullable: true),
                    criminal_record_date = table.Column<string>(type: "text", nullable: true),
                    xmin = table.Column<uint>(type: "xid", rowVersion: true, nullable: false),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    created_by = table.Column<Guid>(type: "uuid", nullable: true),
                    modified_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    modified_by = table.Column<Guid>(type: "uuid", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_employee_sensitive_data", x => x.employee_id);
                    table.ForeignKey(
                        name: "fk_employee_sensitive_data_employees_employee_id",
                        column: x => x.employee_id,
                        principalSchema: "hr",
                        principalTable: "employees",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "employments",
                schema: "hr",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    employee_id = table.Column<Guid>(type: "uuid", nullable: false),
                    company_id = table.Column<Guid>(type: "uuid", nullable: false),
                    registry_number = table.Column<string>(type: "character varying(30)", maxLength: 30, nullable: false),
                    hire_date = table.Column<DateOnly>(type: "date", nullable: false),
                    seniority_date = table.Column<DateOnly>(type: "date", nullable: false),
                    seniority_reason = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: true),
                    type = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    contract_end_date = table.Column<DateOnly>(type: "date", nullable: true),
                    weekly_hours = table.Column<decimal>(type: "numeric(5,2)", precision: 5, scale: 2, nullable: true),
                    probation_end_date = table.Column<DateOnly>(type: "date", nullable: true),
                    work_schedule_id = table.Column<Guid>(type: "uuid", nullable: true),
                    leave_policy_id = table.Column<Guid>(type: "uuid", nullable: true),
                    is_primary = table.Column<bool>(type: "boolean", nullable: false),
                    termination_date = table.Column<DateOnly>(type: "date", nullable: true),
                    sgk_exit_code = table.Column<string>(type: "character varying(10)", maxLength: 10, nullable: true),
                    termination_reason = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: true),
                    rehireable = table.Column<bool>(type: "boolean", nullable: true),
                    termination_note = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: true),
                    xmin = table.Column<uint>(type: "xid", rowVersion: true, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_employments", x => x.id);
                    table.ForeignKey(
                        name: "fk_employments_employees_employee_id",
                        column: x => x.employee_id,
                        principalSchema: "hr",
                        principalTable: "employees",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "job_assignments",
                schema: "hr",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    employment_id = table.Column<Guid>(type: "uuid", nullable: false),
                    position_id = table.Column<Guid>(type: "uuid", nullable: false),
                    branch_id = table.Column<Guid>(type: "uuid", nullable: true),
                    manager_position_id = table.Column<Guid>(type: "uuid", nullable: true),
                    job_id = table.Column<Guid>(type: "uuid", nullable: false),
                    level = table.Column<string>(type: "character varying(10)", maxLength: 10, nullable: true),
                    cost_center_code = table.Column<string>(type: "character varying(30)", maxLength: 30, nullable: true),
                    reason = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    valid_from = table.Column<DateOnly>(type: "date", nullable: false),
                    valid_to = table.Column<DateOnly>(type: "date", nullable: true),
                    announced = table.Column<bool>(type: "boolean", nullable: false),
                    xmin = table.Column<uint>(type: "xid", rowVersion: true, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_job_assignments", x => x.id);
                    table.ForeignKey(
                        name: "fk_job_assignments_employments_employment_id",
                        column: x => x.employment_id,
                        principalSchema: "hr",
                        principalTable: "employments",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "fk_job_assignments_jobs_job_id",
                        column: x => x.job_id,
                        principalSchema: "hr",
                        principalTable: "jobs",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "fk_job_assignments_positions_manager_position_id",
                        column: x => x.manager_position_id,
                        principalSchema: "hr",
                        principalTable: "positions",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "fk_job_assignments_positions_position_id",
                        column: x => x.position_id,
                        principalSchema: "hr",
                        principalTable: "positions",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "ix_employees_custom_fields",
                schema: "hr",
                table: "employees",
                column: "custom_fields")
                .Annotation("Npgsql:IndexMethod", "gin")
                .Annotation("Npgsql:IndexOperators", new[] { "jsonb_path_ops" });

            migrationBuilder.CreateIndex(
                name: "ix_employees_last_name_first_name",
                schema: "hr",
                table: "employees",
                columns: new[] { "last_name", "first_name" });

            migrationBuilder.CreateIndex(
                name: "ix_employees_tckn",
                schema: "hr",
                table: "employees",
                column: "tckn",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_employees_user_id",
                schema: "hr",
                table: "employees",
                column: "user_id",
                unique: true,
                filter: "user_id IS NOT NULL");

            migrationBuilder.CreateIndex(
                name: "ix_employments_company_id_registry_number",
                schema: "hr",
                table: "employments",
                columns: new[] { "company_id", "registry_number" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_employments_employee_id_company_id_open",
                schema: "hr",
                table: "employments",
                columns: new[] { "employee_id", "company_id" },
                unique: true,
                filter: "termination_date IS NULL");

            migrationBuilder.CreateIndex(
                name: "ix_job_assignments_employment_id",
                schema: "hr",
                table: "job_assignments",
                column: "employment_id");

            migrationBuilder.CreateIndex(
                name: "ix_job_assignments_job_id",
                schema: "hr",
                table: "job_assignments",
                column: "job_id");

            migrationBuilder.CreateIndex(
                name: "ix_job_assignments_manager_position_id",
                schema: "hr",
                table: "job_assignments",
                column: "manager_position_id");

            migrationBuilder.CreateIndex(
                name: "ix_job_assignments_position_id_valid_from",
                schema: "hr",
                table: "job_assignments",
                columns: new[] { "position_id", "valid_from" });

            migrationBuilder.CreateIndex(
                name: "ix_job_assignments_unannounced",
                schema: "hr",
                table: "job_assignments",
                column: "valid_from",
                filter: "NOT announced");

            migrationBuilder.CreateIndex(
                name: "ix_sgk_exit_codes_code",
                schema: "hr",
                table: "sgk_exit_codes",
                column: "code",
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "employee_sensitive_data",
                schema: "hr");

            migrationBuilder.DropTable(
                name: "job_assignments",
                schema: "hr");

            migrationBuilder.DropTable(
                name: "sgk_exit_codes",
                schema: "hr");

            migrationBuilder.DropTable(
                name: "employments",
                schema: "hr");

            migrationBuilder.DropTable(
                name: "employees",
                schema: "hr");
        }
    }
}
