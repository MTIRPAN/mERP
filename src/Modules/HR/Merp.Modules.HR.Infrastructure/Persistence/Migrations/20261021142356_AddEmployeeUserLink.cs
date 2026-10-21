using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Merp.Modules.HR.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddEmployeeUserLink : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<DateTimeOffset>(
                name: "user_deactivation_requested_at",
                schema: "hr",
                table: "employees",
                type: "timestamp with time zone",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "user_link_error",
                schema: "hr",
                table: "employees",
                type: "character varying(100)",
                maxLength: 100,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "user_link_state",
                schema: "hr",
                table: "employees",
                type: "character varying(20)",
                maxLength: 20,
                nullable: false,
                defaultValue: "None");

            // People linked before D39 keep their link
            migrationBuilder.Sql("UPDATE hr.employees SET user_link_state = 'Linked' WHERE user_id IS NOT NULL;");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "user_deactivation_requested_at",
                schema: "hr",
                table: "employees");

            migrationBuilder.DropColumn(
                name: "user_link_error",
                schema: "hr",
                table: "employees");

            migrationBuilder.DropColumn(
                name: "user_link_state",
                schema: "hr",
                table: "employees");
        }
    }
}
