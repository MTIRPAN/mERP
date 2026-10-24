using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Merp.Modules.HR.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddLeaveAccounts : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "leave_accounts",
                schema: "hr",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    employment_id = table.Column<Guid>(type: "uuid", nullable: false),
                    employee_id = table.Column<Guid>(type: "uuid", nullable: false),
                    company_id = table.Column<Guid>(type: "uuid", nullable: false),
                    leave_type_code = table.Column<string>(type: "character varying(30)", maxLength: 30, nullable: false),
                    closed_on = table.Column<DateOnly>(type: "date", nullable: true),
                    xmin = table.Column<uint>(type: "xid", rowVersion: true, nullable: false),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    created_by = table.Column<Guid>(type: "uuid", nullable: true),
                    modified_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    modified_by = table.Column<Guid>(type: "uuid", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_leave_accounts", x => x.id);
                });

            migrationBuilder.CreateTable(
                name: "leave_ledger_entries",
                schema: "hr",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    account_id = table.Column<Guid>(type: "uuid", nullable: false),
                    date = table.Column<DateOnly>(type: "date", nullable: false),
                    quantity = table.Column<decimal>(type: "numeric(7,2)", precision: 7, scale: 2, nullable: false),
                    source = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    source_id = table.Column<Guid>(type: "uuid", nullable: true),
                    period_key = table.Column<string>(type: "character varying(10)", maxLength: 10, nullable: true),
                    note = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: true),
                    recorded_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    recorded_by = table.Column<Guid>(type: "uuid", nullable: true),
                    xmin = table.Column<uint>(type: "xid", rowVersion: true, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_leave_ledger_entries", x => x.id);
                    table.ForeignKey(
                        name: "fk_leave_ledger_entries_leave_accounts_account_id",
                        column: x => x.account_id,
                        principalSchema: "hr",
                        principalTable: "leave_accounts",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "ix_leave_accounts_employee_id",
                schema: "hr",
                table: "leave_accounts",
                column: "employee_id");

            migrationBuilder.CreateIndex(
                name: "ix_leave_accounts_employment_id_leave_type_code",
                schema: "hr",
                table: "leave_accounts",
                columns: new[] { "employment_id", "leave_type_code" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_leave_ledger_entries_account_id_period_key",
                schema: "hr",
                table: "leave_ledger_entries",
                columns: new[] { "account_id", "period_key" },
                unique: true,
                filter: "source = 'Accrual'");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "leave_ledger_entries",
                schema: "hr");

            migrationBuilder.DropTable(
                name: "leave_accounts",
                schema: "hr");
        }
    }
}
