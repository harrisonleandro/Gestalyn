using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace ProjectManager.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddWorkReports : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "work_reports",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    organization_id = table.Column<Guid>(type: "uuid", nullable: false),
                    project_id = table.Column<Guid>(type: "uuid", nullable: false),
                    task_id = table.Column<Guid>(type: "uuid", nullable: true),
                    agent_id = table.Column<Guid>(type: "uuid", nullable: false),
                    credential_id = table.Column<Guid>(type: "uuid", nullable: false),
                    summary = table.Column<string>(type: "character varying(2000)", maxLength: 2000, nullable: false),
                    changes = table.Column<string>(type: "character varying(20000)", maxLength: 20000, nullable: true),
                    tests_command = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: true),
                    tests_passed = table.Column<int>(type: "integer", nullable: true),
                    tests_failed = table.Column<int>(type: "integer", nullable: true),
                    tests_skipped = table.Column<int>(type: "integer", nullable: true),
                    commit_sha = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: true),
                    commit_repository = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: true),
                    task_state_id = table.Column<Guid>(type: "uuid", nullable: true),
                    task_state_name = table.Column<string>(type: "character varying(60)", maxLength: 60, nullable: true),
                    suggested_state_id = table.Column<Guid>(type: "uuid", nullable: true),
                    suggested_state_name = table.Column<string>(type: "character varying(60)", maxLength: 60, nullable: true),
                    request_id = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: true),
                    received_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    xmin = table.Column<uint>(type: "xid", rowVersion: true, nullable: false),
                    files = table.Column<string[]>(type: "text[]", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_work_reports", x => x.id);
                    table.CheckConstraint("ck_work_reports_commit_sha", "commit_sha IS NULL OR commit_sha ~ '^[0-9a-f]{40}([0-9a-f]{24})?$'");
                    table.CheckConstraint("ck_work_reports_files", "cardinality(files) <= 200 AND array_position(files, NULL) IS NULL");
                    table.CheckConstraint("ck_work_reports_suggested_state", "(suggested_state_id IS NULL) = (suggested_state_name IS NULL)");
                    table.CheckConstraint("ck_work_reports_task_state", "(task_id IS NULL) = (task_state_id IS NULL) AND (task_state_id IS NULL) = (task_state_name IS NULL)");
                    table.CheckConstraint("ck_work_reports_tests_non_negative", "coalesce(tests_passed, 0) >= 0 AND coalesce(tests_failed, 0) >= 0 AND coalesce(tests_skipped, 0) >= 0");
                    table.ForeignKey(
                        name: "fk_work_reports_agent_credentials_organization_id_credential_id",
                        columns: x => new { x.organization_id, x.credential_id },
                        principalTable: "agent_credentials",
                        principalColumns: new[] { "organization_id", "id" },
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "fk_work_reports_agents_organization_id_agent_id",
                        columns: x => new { x.organization_id, x.agent_id },
                        principalTable: "agents",
                        principalColumns: new[] { "organization_id", "id" },
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "fk_work_reports_projects_organization_id_project_id",
                        columns: x => new { x.organization_id, x.project_id },
                        principalTable: "projects",
                        principalColumns: new[] { "organization_id", "id" },
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "fk_work_reports_tasks_organization_id_project_id_task_id",
                        columns: x => new { x.organization_id, x.project_id, x.task_id },
                        principalTable: "tasks",
                        principalColumns: new[] { "organization_id", "project_id", "id" },
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "ix_work_reports_organization_id_agent_id",
                table: "work_reports",
                columns: new[] { "organization_id", "agent_id" });

            migrationBuilder.CreateIndex(
                name: "ix_work_reports_organization_id_credential_id",
                table: "work_reports",
                columns: new[] { "organization_id", "credential_id" });

            migrationBuilder.CreateIndex(
                name: "ix_work_reports_organization_id_project_id_task_id",
                table: "work_reports",
                columns: new[] { "organization_id", "project_id", "task_id" });

            migrationBuilder.CreateIndex(
                name: "ix_work_reports_project_id_received_at",
                table: "work_reports",
                columns: new[] { "project_id", "received_at" });

            migrationBuilder.CreateIndex(
                name: "ix_work_reports_task_id_received_at",
                table: "work_reports",
                columns: new[] { "task_id", "received_at" });

            // Imutabilidade no banco (ADR-016, D8): o relatório é permanente. UPDATE, DELETE e TRUNCATE são recusados,
            // inclusive por SQL direto, e não há exceção de retenção. As verificações posteriores ficam em outra tabela.
            migrationBuilder.Sql("""
                CREATE FUNCTION reject_work_report_change() RETURNS trigger LANGUAGE plpgsql AS $$
                BEGIN
                    RAISE EXCEPTION 'Os relatórios de trabalho são imutáveis (tabela %).', TG_TABLE_NAME
                        USING ERRCODE = 'integrity_constraint_violation';
                END;
                $$;

                CREATE TRIGGER work_reports_immutable BEFORE UPDATE OR DELETE ON work_reports
                    FOR EACH ROW EXECUTE FUNCTION reject_work_report_change();
                CREATE TRIGGER work_reports_no_truncate BEFORE TRUNCATE ON work_reports
                    FOR EACH STATEMENT EXECUTE FUNCTION reject_work_report_change();
                """);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql("""
                DROP TRIGGER work_reports_immutable ON work_reports;
                DROP TRIGGER work_reports_no_truncate ON work_reports;
                DROP FUNCTION reject_work_report_change();
                """);

            migrationBuilder.DropTable(
                name: "work_reports");
        }
    }
}
