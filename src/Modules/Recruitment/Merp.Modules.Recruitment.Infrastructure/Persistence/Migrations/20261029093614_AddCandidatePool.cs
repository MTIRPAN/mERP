using System;
using Microsoft.EntityFrameworkCore.Migrations;
using NpgsqlTypes;

#nullable disable

namespace Merp.Modules.Recruitment.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddCandidatePool : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AlterColumn<string>(
                name: "consent",
                schema: "recruitment",
                table: "job_applications",
                type: "jsonb",
                nullable: true,
                oldClrType: typeof(string),
                oldType: "jsonb");

            migrationBuilder.AlterColumn<DateTimeOffset>(
                name: "last_applied_at",
                schema: "recruitment",
                table: "candidates",
                type: "timestamp with time zone",
                nullable: true,
                oldClrType: typeof(DateTimeOffset),
                oldType: "timestamp with time zone");

            migrationBuilder.AddColumn<string>(
                name: "current_company",
                schema: "recruitment",
                table: "candidates",
                type: "character varying(150)",
                maxLength: 150,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "current_title",
                schema: "recruitment",
                table: "candidates",
                type: "character varying(150)",
                maxLength: 150,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "cv_text",
                schema: "recruitment",
                table: "candidates",
                type: "text",
                nullable: true);

            migrationBuilder.AddColumn<Guid>(
                name: "cv_text_file_id",
                schema: "recruitment",
                table: "candidates",
                type: "uuid",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "education",
                schema: "recruitment",
                table: "candidates",
                type: "character varying(300)",
                maxLength: 300,
                nullable: true);

            migrationBuilder.AddColumn<decimal>(
                name: "experience_years",
                schema: "recruitment",
                table: "candidates",
                type: "numeric(4,1)",
                precision: 4,
                scale: 1,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "linked_in_url",
                schema: "recruitment",
                table: "candidates",
                type: "character varying(300)",
                maxLength: 300,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "location",
                schema: "recruitment",
                table: "candidates",
                type: "character varying(150)",
                maxLength: 150,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "normalized_phone",
                schema: "recruitment",
                table: "candidates",
                type: "character varying(30)",
                maxLength: 30,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "notes_text",
                schema: "recruitment",
                table: "candidates",
                type: "text",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "portfolio_url",
                schema: "recruitment",
                table: "candidates",
                type: "character varying(300)",
                maxLength: 300,
                nullable: true);

            migrationBuilder.AddColumn<Guid>(
                name: "referrer_employee_id",
                schema: "recruitment",
                table: "candidates",
                type: "uuid",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "tag_text",
                schema: "recruitment",
                table: "candidates",
                type: "character varying(2000)",
                maxLength: 2000,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "tags",
                schema: "recruitment",
                table: "candidates",
                type: "jsonb",
                nullable: false,
                defaultValue: "[]");

            migrationBuilder.AddColumn<NpgsqlTsVector>(
                name: "search_vector",
                schema: "recruitment",
                table: "candidates",
                type: "tsvector",
                nullable: true,
                computedColumnSql: "to_tsvector('turkish', coalesce(first_name, '') || ' ' || coalesce(last_name, '') || ' ' || coalesce(email, '') || ' ' || coalesce(current_title, '') || ' ' || coalesce(current_company, '') || ' ' || coalesce(location, '') || ' ' || coalesce(education, '') || ' ' || coalesce(tag_text, '') || ' ' || coalesce(notes_text, '') || ' ' || coalesce(cv_text, ''))",
                stored: true);

            migrationBuilder.CreateTable(
                name: "candidate_notes",
                schema: "recruitment",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    candidate_id = table.Column<Guid>(type: "uuid", nullable: false),
                    body = table.Column<string>(type: "character varying(2000)", maxLength: 2000, nullable: false),
                    mentioned_user_ids = table.Column<string>(type: "jsonb", nullable: false),
                    author_user_id = table.Column<Guid>(type: "uuid", nullable: true),
                    at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    xmin = table.Column<uint>(type: "xid", rowVersion: true, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_candidate_notes", x => x.id);
                    table.ForeignKey(
                        name: "fk_candidate_notes_candidates_candidate_id",
                        column: x => x.candidate_id,
                        principalSchema: "recruitment",
                        principalTable: "candidates",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "ix_candidates_last_name_first_name",
                schema: "recruitment",
                table: "candidates",
                columns: new[] { "last_name", "first_name" });

            migrationBuilder.CreateIndex(
                name: "ix_candidates_normalized_phone",
                schema: "recruitment",
                table: "candidates",
                column: "normalized_phone");

            migrationBuilder.CreateIndex(
                name: "ix_candidates_search_vector",
                schema: "recruitment",
                table: "candidates",
                column: "search_vector")
                .Annotation("Npgsql:IndexMethod", "gin");

            migrationBuilder.CreateIndex(
                name: "ix_candidate_notes_candidate_id_at",
                schema: "recruitment",
                table: "candidate_notes",
                columns: new[] { "candidate_id", "at" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "candidate_notes",
                schema: "recruitment");

            migrationBuilder.DropIndex(
                name: "ix_candidates_last_name_first_name",
                schema: "recruitment",
                table: "candidates");

            migrationBuilder.DropIndex(
                name: "ix_candidates_normalized_phone",
                schema: "recruitment",
                table: "candidates");

            migrationBuilder.DropIndex(
                name: "ix_candidates_search_vector",
                schema: "recruitment",
                table: "candidates");

            migrationBuilder.DropColumn(
                name: "search_vector",
                schema: "recruitment",
                table: "candidates");

            migrationBuilder.DropColumn(
                name: "current_company",
                schema: "recruitment",
                table: "candidates");

            migrationBuilder.DropColumn(
                name: "current_title",
                schema: "recruitment",
                table: "candidates");

            migrationBuilder.DropColumn(
                name: "cv_text",
                schema: "recruitment",
                table: "candidates");

            migrationBuilder.DropColumn(
                name: "cv_text_file_id",
                schema: "recruitment",
                table: "candidates");

            migrationBuilder.DropColumn(
                name: "education",
                schema: "recruitment",
                table: "candidates");

            migrationBuilder.DropColumn(
                name: "experience_years",
                schema: "recruitment",
                table: "candidates");

            migrationBuilder.DropColumn(
                name: "linked_in_url",
                schema: "recruitment",
                table: "candidates");

            migrationBuilder.DropColumn(
                name: "location",
                schema: "recruitment",
                table: "candidates");

            migrationBuilder.DropColumn(
                name: "normalized_phone",
                schema: "recruitment",
                table: "candidates");

            migrationBuilder.DropColumn(
                name: "notes_text",
                schema: "recruitment",
                table: "candidates");

            migrationBuilder.DropColumn(
                name: "portfolio_url",
                schema: "recruitment",
                table: "candidates");

            migrationBuilder.DropColumn(
                name: "referrer_employee_id",
                schema: "recruitment",
                table: "candidates");

            migrationBuilder.DropColumn(
                name: "tag_text",
                schema: "recruitment",
                table: "candidates");

            migrationBuilder.DropColumn(
                name: "tags",
                schema: "recruitment",
                table: "candidates");

            migrationBuilder.AlterColumn<string>(
                name: "consent",
                schema: "recruitment",
                table: "job_applications",
                type: "jsonb",
                nullable: false,
                defaultValue: "",
                oldClrType: typeof(string),
                oldType: "jsonb",
                oldNullable: true);

            migrationBuilder.AlterColumn<DateTimeOffset>(
                name: "last_applied_at",
                schema: "recruitment",
                table: "candidates",
                type: "timestamp with time zone",
                nullable: false,
                defaultValue: new DateTimeOffset(new DateTime(1, 1, 1, 0, 0, 0, 0, DateTimeKind.Unspecified), new TimeSpan(0, 0, 0, 0, 0)),
                oldClrType: typeof(DateTimeOffset),
                oldType: "timestamp with time zone",
                oldNullable: true);
        }
    }
}
