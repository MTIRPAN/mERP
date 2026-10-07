using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Merp.Modules.Platform.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddNumbering : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "number_counters",
                schema: "platform",
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

            migrationBuilder.CreateTable(
                name: "number_sequences",
                schema: "platform",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    document_type = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    company_id = table.Column<Guid>(type: "uuid", nullable: true),
                    pattern = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: false),
                    reset_period = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    xmin = table.Column<uint>(type: "xid", rowVersion: true, nullable: false),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    created_by = table.Column<Guid>(type: "uuid", nullable: true),
                    modified_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    modified_by = table.Column<Guid>(type: "uuid", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_number_sequences", x => x.id);
                });

            migrationBuilder.CreateIndex(
                name: "ix_number_sequences_document_type",
                schema: "platform",
                table: "number_sequences",
                column: "document_type",
                unique: true,
                filter: "company_id IS NULL");

            migrationBuilder.CreateIndex(
                name: "ix_number_sequences_document_type_company_id",
                schema: "platform",
                table: "number_sequences",
                columns: new[] { "document_type", "company_id" },
                unique: true,
                filter: "company_id IS NOT NULL");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "number_counters",
                schema: "platform");

            migrationBuilder.DropTable(
                name: "number_sequences",
                schema: "platform");
        }
    }
}
