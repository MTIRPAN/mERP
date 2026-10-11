using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Merp.Modules.Platform.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddApprovals : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "approval_definitions",
                schema: "platform",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    document_type = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    company_id = table.Column<Guid>(type: "uuid", nullable: true),
                    name = table.Column<string>(type: "jsonb", nullable: false),
                    priority = table.Column<int>(type: "integer", nullable: false),
                    entry_condition = table.Column<string>(type: "jsonb", nullable: true),
                    steps = table.Column<string>(type: "jsonb", nullable: false),
                    fallback = table.Column<string>(type: "jsonb", nullable: true),
                    is_active = table.Column<bool>(type: "boolean", nullable: false),
                    revision = table.Column<int>(type: "integer", nullable: false),
                    xmin = table.Column<uint>(type: "xid", rowVersion: true, nullable: false),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    created_by = table.Column<Guid>(type: "uuid", nullable: true),
                    modified_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    modified_by = table.Column<Guid>(type: "uuid", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_approval_definitions", x => x.id);
                });

            migrationBuilder.CreateTable(
                name: "approval_requests",
                schema: "platform",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    document_type = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    document_id = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    company_id = table.Column<Guid>(type: "uuid", nullable: true),
                    requester_user_id = table.Column<Guid>(type: "uuid", nullable: false),
                    requester_employee_id = table.Column<Guid>(type: "uuid", nullable: true),
                    facts = table.Column<string>(type: "jsonb", nullable: false),
                    title_key = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    title_args = table.Column<string>(type: "jsonb", nullable: false),
                    summary = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: true),
                    link = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: true),
                    definition_id = table.Column<Guid>(type: "uuid", nullable: true),
                    definition_revision = table.Column<int>(type: "integer", nullable: true),
                    steps = table.Column<string>(type: "jsonb", nullable: false),
                    fallback = table.Column<string>(type: "jsonb", nullable: true),
                    status = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    round = table.Column<int>(type: "integer", nullable: false),
                    current_step = table.Column<int>(type: "integer", nullable: true),
                    submitted_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    completed_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    last_activity_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    xmin = table.Column<uint>(type: "xid", rowVersion: true, nullable: false),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    created_by = table.Column<Guid>(type: "uuid", nullable: true),
                    modified_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    modified_by = table.Column<Guid>(type: "uuid", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_approval_requests", x => x.id);
                });

            migrationBuilder.CreateTable(
                name: "delegations",
                schema: "platform",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    principal_user_id = table.Column<Guid>(type: "uuid", nullable: false),
                    delegate_user_id = table.Column<Guid>(type: "uuid", nullable: false),
                    from = table.Column<DateOnly>(type: "date", nullable: false),
                    to = table.Column<DateOnly>(type: "date", nullable: false),
                    document_types = table.Column<string>(type: "jsonb", nullable: false),
                    is_revoked = table.Column<bool>(type: "boolean", nullable: false),
                    xmin = table.Column<uint>(type: "xid", rowVersion: true, nullable: false),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    created_by = table.Column<Guid>(type: "uuid", nullable: true),
                    modified_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    modified_by = table.Column<Guid>(type: "uuid", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_delegations", x => x.id);
                });

            migrationBuilder.CreateTable(
                name: "approval_history",
                schema: "platform",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    request_id = table.Column<Guid>(type: "uuid", nullable: false),
                    sequence = table.Column<int>(type: "integer", nullable: false),
                    kind = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    round = table.Column<int>(type: "integer", nullable: false),
                    step = table.Column<int>(type: "integer", nullable: true),
                    user_id = table.Column<Guid>(type: "uuid", nullable: true),
                    on_behalf_of_user_id = table.Column<Guid>(type: "uuid", nullable: true),
                    detail = table.Column<string>(type: "character varying(2000)", maxLength: 2000, nullable: true),
                    at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    xmin = table.Column<uint>(type: "xid", rowVersion: true, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_approval_history", x => x.id);
                    table.ForeignKey(
                        name: "fk_approval_history_approval_requests_request_id",
                        column: x => x.request_id,
                        principalSchema: "platform",
                        principalTable: "approval_requests",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "approval_tasks",
                schema: "platform",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    request_id = table.Column<Guid>(type: "uuid", nullable: false),
                    round = table.Column<int>(type: "integer", nullable: false),
                    step = table.Column<int>(type: "integer", nullable: false),
                    assignee_user_id = table.Column<Guid>(type: "uuid", nullable: false),
                    status = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    decided_by_user_id = table.Column<Guid>(type: "uuid", nullable: true),
                    on_behalf_of_user_id = table.Column<Guid>(type: "uuid", nullable: true),
                    comment = table.Column<string>(type: "character varying(2000)", maxLength: 2000, nullable: true),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    due_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    decided_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    escalated_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    xmin = table.Column<uint>(type: "xid", rowVersion: true, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_approval_tasks", x => x.id);
                    table.ForeignKey(
                        name: "fk_approval_tasks_approval_requests_request_id",
                        column: x => x.request_id,
                        principalSchema: "platform",
                        principalTable: "approval_requests",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "ix_approval_definitions_document_type_company_id_priority",
                schema: "platform",
                table: "approval_definitions",
                columns: new[] { "document_type", "company_id", "priority" });

            migrationBuilder.CreateIndex(
                name: "ix_approval_history_request_id_sequence",
                schema: "platform",
                table: "approval_history",
                columns: new[] { "request_id", "sequence" });

            migrationBuilder.CreateIndex(
                name: "ix_approval_requests_document_type_document_id",
                schema: "platform",
                table: "approval_requests",
                columns: new[] { "document_type", "document_id" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_approval_requests_requester_user_id",
                schema: "platform",
                table: "approval_requests",
                column: "requester_user_id");

            migrationBuilder.CreateIndex(
                name: "ix_approval_requests_status_submitted_at",
                schema: "platform",
                table: "approval_requests",
                columns: new[] { "status", "submitted_at" });

            migrationBuilder.CreateIndex(
                name: "ix_approval_tasks_due",
                schema: "platform",
                table: "approval_tasks",
                column: "due_at",
                filter: "status = 'Pending' AND escalated_at IS NULL");

            migrationBuilder.CreateIndex(
                name: "ix_approval_tasks_open",
                schema: "platform",
                table: "approval_tasks",
                column: "assignee_user_id",
                filter: "status = 'Pending'");

            migrationBuilder.CreateIndex(
                name: "ix_approval_tasks_request_id",
                schema: "platform",
                table: "approval_tasks",
                column: "request_id");

            migrationBuilder.CreateIndex(
                name: "ix_delegations_delegate_user_id",
                schema: "platform",
                table: "delegations",
                column: "delegate_user_id");

            migrationBuilder.CreateIndex(
                name: "ix_delegations_principal_user_id_from_to",
                schema: "platform",
                table: "delegations",
                columns: new[] { "principal_user_id", "from", "to" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "approval_definitions",
                schema: "platform");

            migrationBuilder.DropTable(
                name: "approval_history",
                schema: "platform");

            migrationBuilder.DropTable(
                name: "approval_tasks",
                schema: "platform");

            migrationBuilder.DropTable(
                name: "delegations",
                schema: "platform");

            migrationBuilder.DropTable(
                name: "approval_requests",
                schema: "platform");
        }
    }
}
