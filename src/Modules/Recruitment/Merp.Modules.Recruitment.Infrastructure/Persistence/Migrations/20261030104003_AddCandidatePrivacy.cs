using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Merp.Modules.Recruitment.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddCandidatePrivacy : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<DateTimeOffset>(
                name: "anonymized_at",
                schema: "recruitment",
                table: "candidates",
                type: "timestamp with time zone",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "consent_changes",
                schema: "recruitment",
                table: "candidates",
                type: "jsonb",
                nullable: false,
                defaultValue: "[]");

            migrationBuilder.AddColumn<DateTimeOffset>(
                name: "consent_renewal_requested_at",
                schema: "recruitment",
                table: "candidates",
                type: "timestamp with time zone",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "consent_token_hash",
                schema: "recruitment",
                table: "candidates",
                type: "character(64)",
                fixedLength: true,
                maxLength: 64,
                nullable: true);

            migrationBuilder.CreateIndex(
                name: "ix_candidates_consent_token_hash",
                schema: "recruitment",
                table: "candidates",
                column: "consent_token_hash",
                unique: true,
                filter: "consent_token_hash IS NOT NULL");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "ix_candidates_consent_token_hash",
                schema: "recruitment",
                table: "candidates");

            migrationBuilder.DropColumn(
                name: "anonymized_at",
                schema: "recruitment",
                table: "candidates");

            migrationBuilder.DropColumn(
                name: "consent_changes",
                schema: "recruitment",
                table: "candidates");

            migrationBuilder.DropColumn(
                name: "consent_renewal_requested_at",
                schema: "recruitment",
                table: "candidates");

            migrationBuilder.DropColumn(
                name: "consent_token_hash",
                schema: "recruitment",
                table: "candidates");
        }
    }
}
