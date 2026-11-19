using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace ProjectManager.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddIdempotencyRecords : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "idempotency_records",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    organization_id = table.Column<Guid>(type: "uuid", nullable: false),
                    agent_id = table.Column<Guid>(type: "uuid", nullable: false),
                    credential_id = table.Column<Guid>(type: "uuid", nullable: false),
                    key = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    method = table.Column<string>(type: "character varying(10)", maxLength: 10, nullable: false),
                    route_template = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    request_hash = table.Column<string>(type: "character(64)", fixedLength: true, maxLength: 64, nullable: false),
                    response_status = table.Column<int>(type: "integer", nullable: false),
                    response_body = table.Column<string>(type: "character varying(262144)", maxLength: 262144, nullable: false),
                    location = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: true),
                    project_id = table.Column<Guid>(type: "uuid", nullable: true),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    expires_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    xmin = table.Column<uint>(type: "xid", rowVersion: true, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_idempotency_records", x => x.id);
                    table.ForeignKey(
                        name: "fk_idempotency_records_agent_credentials_organization_id_crede",
                        columns: x => new { x.organization_id, x.credential_id },
                        principalTable: "agent_credentials",
                        principalColumns: new[] { "organization_id", "id" },
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "fk_idempotency_records_agents_organization_id_agent_id",
                        columns: x => new { x.organization_id, x.agent_id },
                        principalTable: "agents",
                        principalColumns: new[] { "organization_id", "id" },
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "ix_idempotency_records_created_at",
                table: "idempotency_records",
                column: "created_at");

            migrationBuilder.CreateIndex(
                name: "ix_idempotency_records_credential_id_key",
                table: "idempotency_records",
                columns: new[] { "credential_id", "key" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_idempotency_records_organization_id_agent_id",
                table: "idempotency_records",
                columns: new[] { "organization_id", "agent_id" });

            migrationBuilder.CreateIndex(
                name: "ix_idempotency_records_organization_id_credential_id",
                table: "idempotency_records",
                columns: new[] { "organization_id", "credential_id" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "idempotency_records");
        }
    }
}
