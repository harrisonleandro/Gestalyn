using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace ProjectManager.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddActivity : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "activity_events",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    organization_id = table.Column<Guid>(type: "uuid", nullable: false),
                    project_id = table.Column<Guid>(type: "uuid", nullable: true),
                    task_id = table.Column<Guid>(type: "uuid", nullable: true),
                    actor_id = table.Column<Guid>(type: "uuid", nullable: true),
                    actor_kind = table.Column<string>(type: "character varying(16)", maxLength: 16, nullable: false),
                    origin = table.Column<string>(type: "character varying(16)", maxLength: 16, nullable: false),
                    request_id = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: true),
                    action = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    entity_type = table.Column<string>(type: "character varying(40)", maxLength: 40, nullable: false),
                    entity_id = table.Column<Guid>(type: "uuid", nullable: false),
                    summary = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: false),
                    changes = table.Column<string>(type: "jsonb", nullable: true),
                    occurred_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    xmin = table.Column<uint>(type: "xid", rowVersion: true, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_activity_events", x => x.id);
                    table.ForeignKey(
                        name: "fk_activity_events_organizations_organization_id",
                        column: x => x.organization_id,
                        principalTable: "organizations",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "fk_activity_events_projects_organization_id_project_id",
                        columns: x => new { x.organization_id, x.project_id },
                        principalTable: "projects",
                        principalColumns: new[] { "organization_id", "id" },
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "fk_activity_events_tasks_organization_id_project_id_task_id",
                        columns: x => new { x.organization_id, x.project_id, x.task_id },
                        principalTable: "tasks",
                        principalColumns: new[] { "organization_id", "project_id", "id" },
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "activity_logs",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    organization_id = table.Column<Guid>(type: "uuid", nullable: false),
                    project_id = table.Column<Guid>(type: "uuid", nullable: false),
                    task_id = table.Column<Guid>(type: "uuid", nullable: true),
                    author_id = table.Column<Guid>(type: "uuid", nullable: true),
                    actor_kind = table.Column<string>(type: "character varying(16)", maxLength: 16, nullable: false),
                    origin = table.Column<string>(type: "character varying(16)", maxLength: 16, nullable: false),
                    request_id = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: true),
                    kind = table.Column<string>(type: "character varying(16)", maxLength: 16, nullable: false),
                    summary = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: false),
                    details = table.Column<string>(type: "character varying(10000)", maxLength: 10000, nullable: true),
                    occurred_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    recorded_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    xmin = table.Column<uint>(type: "xid", rowVersion: true, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_activity_logs", x => x.id);
                    table.ForeignKey(
                        name: "fk_activity_logs_projects_organization_id_project_id",
                        columns: x => new { x.organization_id, x.project_id },
                        principalTable: "projects",
                        principalColumns: new[] { "organization_id", "id" },
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "fk_activity_logs_tasks_organization_id_project_id_task_id",
                        columns: x => new { x.organization_id, x.project_id, x.task_id },
                        principalTable: "tasks",
                        principalColumns: new[] { "organization_id", "project_id", "id" },
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.InsertData(
                table: "permissions",
                columns: new[] { "code", "description" },
                values: new object[] { "activity.read_all", "Consultar as atividades de toda a organização" });

            migrationBuilder.CreateIndex(
                name: "ix_activity_events_organization_id_occurred_at",
                table: "activity_events",
                columns: new[] { "organization_id", "occurred_at" });

            migrationBuilder.CreateIndex(
                name: "ix_activity_events_organization_id_project_id_task_id",
                table: "activity_events",
                columns: new[] { "organization_id", "project_id", "task_id" });

            migrationBuilder.CreateIndex(
                name: "ix_activity_events_project_id_occurred_at",
                table: "activity_events",
                columns: new[] { "project_id", "occurred_at" });

            migrationBuilder.CreateIndex(
                name: "ix_activity_events_task_id_occurred_at",
                table: "activity_events",
                columns: new[] { "task_id", "occurred_at" });

            migrationBuilder.CreateIndex(
                name: "ix_activity_logs_organization_id_project_id_task_id",
                table: "activity_logs",
                columns: new[] { "organization_id", "project_id", "task_id" });

            migrationBuilder.CreateIndex(
                name: "ix_activity_logs_project_id_occurred_at",
                table: "activity_logs",
                columns: new[] { "project_id", "occurred_at" });

            migrationBuilder.CreateIndex(
                name: "ix_activity_logs_task_id_occurred_at",
                table: "activity_logs",
                columns: new[] { "task_id", "occurred_at" });

            // Imutabilidade no banco (ADR-014, D4): UPDATE, DELETE e TRUNCATE são recusados nas duas tabelas,
            // inclusive por SQL direto. Correções viram novos registros.
            migrationBuilder.Sql("""
                CREATE FUNCTION reject_activity_change() RETURNS trigger LANGUAGE plpgsql AS $$
                BEGIN
                    RAISE EXCEPTION 'Os registros de atividade são imutáveis (tabela %).', TG_TABLE_NAME
                        USING ERRCODE = 'integrity_constraint_violation';
                END;
                $$;

                CREATE TRIGGER activity_events_immutable BEFORE UPDATE OR DELETE ON activity_events
                    FOR EACH ROW EXECUTE FUNCTION reject_activity_change();
                CREATE TRIGGER activity_events_no_truncate BEFORE TRUNCATE ON activity_events
                    FOR EACH STATEMENT EXECUTE FUNCTION reject_activity_change();
                CREATE TRIGGER activity_logs_immutable BEFORE UPDATE OR DELETE ON activity_logs
                    FOR EACH ROW EXECUTE FUNCTION reject_activity_change();
                CREATE TRIGGER activity_logs_no_truncate BEFORE TRUNCATE ON activity_logs
                    FOR EACH STATEMENT EXECUTE FUNCTION reject_activity_change();
                """);

            // Migração de dados (ADR-014): os papéis de Proprietário e Administrador das organizações já
            // existentes recebem a permissão nova, como os criados a partir de agora.
            migrationBuilder.Sql("""
                INSERT INTO role_permissions (role_id, permission_code, organization_id)
                SELECT r.id, 'activity.read_all', r.organization_id
                FROM roles r
                WHERE r.system_role IN ('Owner', 'Administrator')
                ON CONFLICT DO NOTHING;
                """);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql("""
                DELETE FROM role_permissions WHERE permission_code = 'activity.read_all';
                DROP TRIGGER activity_events_immutable ON activity_events;
                DROP TRIGGER activity_events_no_truncate ON activity_events;
                DROP TRIGGER activity_logs_immutable ON activity_logs;
                DROP TRIGGER activity_logs_no_truncate ON activity_logs;
                DROP FUNCTION reject_activity_change();
                """);

            migrationBuilder.DropTable(
                name: "activity_events");

            migrationBuilder.DropTable(
                name: "activity_logs");

            migrationBuilder.DeleteData(
                table: "permissions",
                keyColumn: "code",
                keyValue: "activity.read_all");
        }
    }
}
