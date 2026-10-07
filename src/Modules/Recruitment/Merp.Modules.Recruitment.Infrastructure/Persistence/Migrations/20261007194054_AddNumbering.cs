using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Merp.Modules.Recruitment.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddNumbering : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "number_counters",
                schema: "recruitment",
                columns: table => new
                {
                    document_type = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    company_id = table.Column<Guid>(type: "uuid", nullable: false),
                    period = table.Column<string>(type: "character varying(10)", maxLength: 10, nullable: false),
                    value = table.Column<long>(type: "bigint", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_number_counters", x => new { x.document_type, x.company_id, x.period });
                });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "number_counters",
                schema: "recruitment");
        }
    }
}
