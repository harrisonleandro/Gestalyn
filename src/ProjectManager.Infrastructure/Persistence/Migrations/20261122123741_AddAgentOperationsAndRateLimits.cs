using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace ProjectManager.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddAgentOperationsAndRateLimits : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "agent_operations",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    organization_id = table.Column<Guid>(type: "uuid", nullable: false),
                    agent_id = table.Column<Guid>(type: "uuid", nullable: false),
                    credential_id = table.Column<Guid>(type: "uuid", nullable: false),
                    occurred_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    duration_ms = table.Column<int>(type: "integer", nullable: false),
                    http_method = table.Column<string>(type: "character varying(10)", maxLength: 10, nullable: false),
                    route_template = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    project_id = table.Column<Guid>(type: "uuid", nullable: true),
                    task_id = table.Column<Guid>(type: "uuid", nullable: true),
                    status_code = table.Column<int>(type: "integer", nullable: false),
                    authorization_result = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false),
                    denial_code = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: true),
                    correlation_id = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: true),
                    idempotent_replay = table.Column<bool>(type: "boolean", nullable: false),
                    client_origin = table.Column<string>(type: "character varying(16)", maxLength: 16, nullable: false),
                    xmin = table.Column<uint>(type: "xid", rowVersion: true, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_agent_operations", x => x.id);
                    table.CheckConstraint("ck_agent_operations_duration", "duration_ms >= 0");
                    table.ForeignKey(
                        name: "fk_agent_operations_agent_credentials_organization_id_credenti",
                        columns: x => new { x.organization_id, x.credential_id },
                        principalTable: "agent_credentials",
                        principalColumns: new[] { "organization_id", "id" },
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "fk_agent_operations_agents_organization_id_agent_id",
                        columns: x => new { x.organization_id, x.agent_id },
                        principalTable: "agents",
                        principalColumns: new[] { "organization_id", "id" },
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "agent_rate_limit_counters",
                columns: table => new
                {
                    credential_id = table.Column<Guid>(type: "uuid", nullable: false),
                    window_start = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    organization_id = table.Column<Guid>(type: "uuid", nullable: false),
                    request_count = table.Column<int>(type: "integer", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_agent_rate_limit_counters", x => new { x.credential_id, x.window_start });
                    table.CheckConstraint("ck_agent_rate_limit_counters_count", "request_count > 0");
                    table.ForeignKey(
                        name: "fk_agent_rate_limit_counters_agent_credentials_organization_id",
                        columns: x => new { x.organization_id, x.credential_id },
                        principalTable: "agent_credentials",
                        principalColumns: new[] { "organization_id", "id" },
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "ix_agent_operations_agent_id_occurred_at",
                table: "agent_operations",
                columns: new[] { "agent_id", "occurred_at" });

            migrationBuilder.CreateIndex(
                name: "ix_agent_operations_occurred_at",
                table: "agent_operations",
                column: "occurred_at");

            migrationBuilder.CreateIndex(
                name: "ix_agent_operations_organization_id_agent_id",
                table: "agent_operations",
                columns: new[] { "organization_id", "agent_id" });

            migrationBuilder.CreateIndex(
                name: "ix_agent_operations_organization_id_credential_id",
                table: "agent_operations",
                columns: new[] { "organization_id", "credential_id" });

            migrationBuilder.CreateIndex(
                name: "ix_agent_operations_organization_id_occurred_at",
                table: "agent_operations",
                columns: new[] { "organization_id", "occurred_at" });

            migrationBuilder.CreateIndex(
                name: "ix_agent_rate_limit_counters_organization_id_credential_id",
                table: "agent_rate_limit_counters",
                columns: new[] { "organization_id", "credential_id" });

            migrationBuilder.CreateIndex(
                name: "ix_agent_rate_limit_counters_window_start",
                table: "agent_rate_limit_counters",
                column: "window_start");

            // Imutabilidade da auditoria (ADR-016, D10 e D12): UPDATE e TRUNCATE são sempre recusados; DELETE só de
            // linhas com mais de 180 dias, o prazo da retenção. Mudar o prazo exige uma nova migração.
            migrationBuilder.Sql("""
                CREATE FUNCTION guard_agent_operation_change() RETURNS trigger LANGUAGE plpgsql AS $$
                BEGIN
                    -- IFs separados: no gatilho de TRUNCATE (por instrução) não existe OLD.
                    IF TG_OP = 'DELETE' THEN
                        IF OLD.occurred_at < now() - interval '180 days' THEN
                            RETURN OLD;
                        END IF;
                    END IF;
                    RAISE EXCEPTION 'As operações de agentes são imutáveis; só a retenção apaga as vencidas (tabela %).', TG_TABLE_NAME
                        USING ERRCODE = 'integrity_constraint_violation';
                END;
                $$;

                CREATE TRIGGER agent_operations_guarded BEFORE UPDATE OR DELETE ON agent_operations
                    FOR EACH ROW EXECUTE FUNCTION guard_agent_operation_change();
                CREATE TRIGGER agent_operations_no_truncate BEFORE TRUNCATE ON agent_operations
                    FOR EACH STATEMENT EXECUTE FUNCTION guard_agent_operation_change();
                """);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql("""
                DROP TRIGGER agent_operations_guarded ON agent_operations;
                DROP TRIGGER agent_operations_no_truncate ON agent_operations;
                DROP FUNCTION guard_agent_operation_change();
                """);

            migrationBuilder.DropTable(
                name: "agent_operations");

            migrationBuilder.DropTable(
                name: "agent_rate_limit_counters");
        }
    }
}
