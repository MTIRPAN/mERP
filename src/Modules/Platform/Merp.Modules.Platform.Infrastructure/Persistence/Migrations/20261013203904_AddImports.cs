using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Merp.Modules.Platform.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddImports : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "import_jobs",
                schema: "platform",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    import_key = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    user_id = table.Column<Guid>(type: "uuid", nullable: false),
                    culture = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    file_id = table.Column<Guid>(type: "uuid", nullable: false),
                    file_name = table.Column<string>(type: "character varying(255)", maxLength: 255, nullable: false),
                    status = table.Column<string>(type: "character varying(30)", maxLength: 30, nullable: false),
                    total_rows = table.Column<int>(type: "integer", nullable: false),
                    error_rows = table.Column<int>(type: "integer", nullable: false),
                    committed_rows = table.Column<int>(type: "integer", nullable: false),
                    errors = table.Column<string>(type: "jsonb", nullable: false),
                    completed_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    xmin = table.Column<uint>(type: "xid", rowVersion: true, nullable: false),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    created_by = table.Column<Guid>(type: "uuid", nullable: true),
                    modified_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    modified_by = table.Column<Guid>(type: "uuid", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_import_jobs", x => x.id);
                });

            migrationBuilder.CreateIndex(
                name: "ix_import_jobs_user_id_import_key_created_at",
                schema: "platform",
                table: "import_jobs",
                columns: new[] { "user_id", "import_key", "created_at" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "import_jobs",
                schema: "platform");
        }
    }
}
