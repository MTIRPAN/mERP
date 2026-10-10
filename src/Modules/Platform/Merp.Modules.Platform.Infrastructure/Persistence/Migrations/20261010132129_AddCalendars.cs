using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Merp.Modules.Platform.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddCalendars : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<Guid>(
                name: "work_schedule_id",
                schema: "platform",
                table: "branches",
                type: "uuid",
                nullable: true);

            migrationBuilder.CreateTable(
                name: "holiday_calendars",
                schema: "platform",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    code = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    name = table.Column<string>(type: "jsonb", nullable: false),
                    xmin = table.Column<uint>(type: "xid", rowVersion: true, nullable: false),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    created_by = table.Column<Guid>(type: "uuid", nullable: true),
                    modified_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    modified_by = table.Column<Guid>(type: "uuid", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_holiday_calendars", x => x.id);
                });

            migrationBuilder.CreateTable(
                name: "holidays",
                schema: "platform",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    calendar_id = table.Column<Guid>(type: "uuid", nullable: false),
                    date = table.Column<DateOnly>(type: "date", nullable: false),
                    name = table.Column<string>(type: "jsonb", nullable: false),
                    duration = table.Column<string>(type: "character varying(10)", maxLength: 10, nullable: false),
                    starts_at = table.Column<TimeOnly>(type: "time without time zone", nullable: true),
                    is_movable = table.Column<bool>(type: "boolean", nullable: false),
                    xmin = table.Column<uint>(type: "xid", rowVersion: true, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_holidays", x => x.id);
                    table.ForeignKey(
                        name: "fk_holidays_holiday_calendars_calendar_id",
                        column: x => x.calendar_id,
                        principalSchema: "platform",
                        principalTable: "holiday_calendars",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "work_schedules",
                schema: "platform",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    code = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    name = table.Column<string>(type: "jsonb", nullable: false),
                    company_id = table.Column<Guid>(type: "uuid", nullable: true),
                    holiday_calendar_id = table.Column<Guid>(type: "uuid", nullable: false),
                    days = table.Column<string>(type: "jsonb", nullable: false),
                    is_default = table.Column<bool>(type: "boolean", nullable: false),
                    is_active = table.Column<bool>(type: "boolean", nullable: false),
                    xmin = table.Column<uint>(type: "xid", rowVersion: true, nullable: false),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    created_by = table.Column<Guid>(type: "uuid", nullable: true),
                    modified_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    modified_by = table.Column<Guid>(type: "uuid", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_work_schedules", x => x.id);
                    table.ForeignKey(
                        name: "fk_work_schedules_companies_company_id",
                        column: x => x.company_id,
                        principalSchema: "platform",
                        principalTable: "companies",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "fk_work_schedules_holiday_calendars_holiday_calendar_id",
                        column: x => x.holiday_calendar_id,
                        principalSchema: "platform",
                        principalTable: "holiday_calendars",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "ix_branches_work_schedule_id",
                schema: "platform",
                table: "branches",
                column: "work_schedule_id");

            migrationBuilder.CreateIndex(
                name: "ix_holiday_calendars_code",
                schema: "platform",
                table: "holiday_calendars",
                column: "code",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_holidays_calendar_id_date",
                schema: "platform",
                table: "holidays",
                columns: new[] { "calendar_id", "date" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_work_schedules_code",
                schema: "platform",
                table: "work_schedules",
                column: "code",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_work_schedules_company_id_is_default",
                schema: "platform",
                table: "work_schedules",
                columns: new[] { "company_id", "is_default" });

            migrationBuilder.CreateIndex(
                name: "ix_work_schedules_holiday_calendar_id",
                schema: "platform",
                table: "work_schedules",
                column: "holiday_calendar_id");

            migrationBuilder.AddForeignKey(
                name: "fk_branches_work_schedules_work_schedule_id",
                schema: "platform",
                table: "branches",
                column: "work_schedule_id",
                principalSchema: "platform",
                principalTable: "work_schedules",
                principalColumn: "id",
                onDelete: ReferentialAction.Restrict);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "fk_branches_work_schedules_work_schedule_id",
                schema: "platform",
                table: "branches");

            migrationBuilder.DropTable(
                name: "holidays",
                schema: "platform");

            migrationBuilder.DropTable(
                name: "work_schedules",
                schema: "platform");

            migrationBuilder.DropTable(
                name: "holiday_calendars",
                schema: "platform");

            migrationBuilder.DropIndex(
                name: "ix_branches_work_schedule_id",
                schema: "platform",
                table: "branches");

            migrationBuilder.DropColumn(
                name: "work_schedule_id",
                schema: "platform",
                table: "branches");
        }
    }
}
