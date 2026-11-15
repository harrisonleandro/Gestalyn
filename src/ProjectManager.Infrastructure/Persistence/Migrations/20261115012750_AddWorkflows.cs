using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace ProjectManager.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddWorkflows : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            // Nasce opcional: as tarefas existentes recebem o estado inicial mais abaixo, e só então a coluna
            // passa a ser obrigatória (ADR-013, D3).
            migrationBuilder.AddColumn<Guid>(
                name: "state_id",
                table: "tasks",
                type: "uuid",
                nullable: true);

            migrationBuilder.CreateTable(
                name: "task_status_history",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    organization_id = table.Column<Guid>(type: "uuid", nullable: false),
                    project_id = table.Column<Guid>(type: "uuid", nullable: false),
                    task_id = table.Column<Guid>(type: "uuid", nullable: false),
                    from_state_id = table.Column<Guid>(type: "uuid", nullable: true),
                    from_state_name = table.Column<string>(type: "character varying(60)", maxLength: 60, nullable: true),
                    to_state_id = table.Column<Guid>(type: "uuid", nullable: false),
                    to_state_name = table.Column<string>(type: "character varying(60)", maxLength: 60, nullable: false),
                    changed_by_id = table.Column<Guid>(type: "uuid", nullable: false),
                    actor_kind = table.Column<string>(type: "character varying(16)", maxLength: 16, nullable: false),
                    comment = table.Column<string>(type: "character varying(1000)", maxLength: 1000, nullable: true),
                    changed_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    xmin = table.Column<uint>(type: "xid", rowVersion: true, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_task_status_history", x => x.id);
                    table.ForeignKey(
                        name: "fk_task_status_history_tasks_organization_id_project_id_task_id",
                        columns: x => new { x.organization_id, x.project_id, x.task_id },
                        principalTable: "tasks",
                        principalColumns: new[] { "organization_id", "project_id", "id" },
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "workflows",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    organization_id = table.Column<Guid>(type: "uuid", nullable: false),
                    project_id = table.Column<Guid>(type: "uuid", nullable: false),
                    require_independent_approval = table.Column<bool>(type: "boolean", nullable: false),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    updated_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    xmin = table.Column<uint>(type: "xid", rowVersion: true, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_workflows", x => x.id);
                    table.UniqueConstraint("ak_workflows_organization_id_project_id", x => new { x.organization_id, x.project_id });
                    table.ForeignKey(
                        name: "fk_workflows_projects_organization_id_project_id",
                        columns: x => new { x.organization_id, x.project_id },
                        principalTable: "projects",
                        principalColumns: new[] { "organization_id", "id" },
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "workflow_states",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    organization_id = table.Column<Guid>(type: "uuid", nullable: false),
                    project_id = table.Column<Guid>(type: "uuid", nullable: false),
                    name = table.Column<string>(type: "character varying(60)", maxLength: 60, nullable: false),
                    category = table.Column<string>(type: "character varying(16)", maxLength: 16, nullable: false),
                    position = table.Column<int>(type: "integer", nullable: false),
                    is_initial = table.Column<bool>(type: "boolean", nullable: false),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    updated_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    xmin = table.Column<uint>(type: "xid", rowVersion: true, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_workflow_states", x => x.id);
                    table.UniqueConstraint("ak_workflow_states_project_id_id", x => new { x.project_id, x.id });
                    table.CheckConstraint("ck_workflow_states_position_positive", "position > 0");
                    table.ForeignKey(
                        name: "fk_workflow_states_workflows_organization_id_project_id",
                        columns: x => new { x.organization_id, x.project_id },
                        principalTable: "workflows",
                        principalColumns: new[] { "organization_id", "project_id" },
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "workflow_transitions",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    organization_id = table.Column<Guid>(type: "uuid", nullable: false),
                    project_id = table.Column<Guid>(type: "uuid", nullable: false),
                    from_state_id = table.Column<Guid>(type: "uuid", nullable: false),
                    to_state_id = table.Column<Guid>(type: "uuid", nullable: false),
                    requires_approval = table.Column<bool>(type: "boolean", nullable: false),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    updated_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    xmin = table.Column<uint>(type: "xid", rowVersion: true, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_workflow_transitions", x => x.id);
                    table.CheckConstraint("ck_workflow_transitions_distinct_states", "from_state_id <> to_state_id");
                    table.ForeignKey(
                        name: "fk_workflow_transitions_workflow_states_project_id_from_state_",
                        columns: x => new { x.project_id, x.from_state_id },
                        principalTable: "workflow_states",
                        principalColumns: new[] { "project_id", "id" },
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "fk_workflow_transitions_workflow_states_project_id_to_state_id",
                        columns: x => new { x.project_id, x.to_state_id },
                        principalTable: "workflow_states",
                        principalColumns: new[] { "project_id", "id" },
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "fk_workflow_transitions_workflows_organization_id_project_id",
                        columns: x => new { x.organization_id, x.project_id },
                        principalTable: "workflows",
                        principalColumns: new[] { "organization_id", "project_id" },
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "ix_tasks_project_id_state_id",
                table: "tasks",
                columns: new[] { "project_id", "state_id" });

            migrationBuilder.CreateIndex(
                name: "ix_task_status_history_organization_id_project_id_task_id",
                table: "task_status_history",
                columns: new[] { "organization_id", "project_id", "task_id" });

            migrationBuilder.CreateIndex(
                name: "ix_task_status_history_task_id_changed_at",
                table: "task_status_history",
                columns: new[] { "task_id", "changed_at" });

            migrationBuilder.CreateIndex(
                name: "ix_workflow_states_organization_id_project_id",
                table: "workflow_states",
                columns: new[] { "organization_id", "project_id" });

            migrationBuilder.CreateIndex(
                name: "ix_workflow_states_project_id_position",
                table: "workflow_states",
                columns: new[] { "project_id", "position" });

            migrationBuilder.CreateIndex(
                name: "ix_workflow_transitions_organization_id_project_id",
                table: "workflow_transitions",
                columns: new[] { "organization_id", "project_id" });

            migrationBuilder.CreateIndex(
                name: "ix_workflow_transitions_project_id_from_state_id_to_state_id",
                table: "workflow_transitions",
                columns: new[] { "project_id", "from_state_id", "to_state_id" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_workflow_transitions_project_id_to_state_id",
                table: "workflow_transitions",
                columns: new[] { "project_id", "to_state_id" });

            migrationBuilder.CreateIndex(
                name: "ix_workflows_project_id",
                table: "workflows",
                column: "project_id",
                unique: true);

            // Migração de dados (ADR-013, D3): cada projeto existente recebe a configuração inicial, a mesma de
            // WorkflowDefinition.CreateDefault, e cada tarefa existente entra no estado inicial, com registro no
            // histórico. SQL sobre dados de organizações, justificado no ADR-013.
            migrationBuilder.Sql("""
                INSERT INTO workflows (id, organization_id, project_id, require_independent_approval, created_at, updated_at)
                SELECT gen_random_uuid(), p.organization_id, p.id, false, now(), now()
                FROM projects p;

                INSERT INTO workflow_states (id, organization_id, project_id, name, category, position, is_initial, created_at, updated_at)
                SELECT gen_random_uuid(), p.organization_id, p.id, s.name, s.category, s.position, s.is_initial, now(), now()
                FROM projects p
                CROSS JOIN (VALUES
                    ('Backlog', 'Todo', 1, true),
                    ('A fazer', 'Todo', 2, false),
                    ('Em andamento', 'InProgress', 3, false),
                    ('Em revisão', 'InProgress', 4, false),
                    ('Bloqueada', 'Blocked', 5, false),
                    ('Concluída', 'Done', 6, false)) AS s(name, category, position, is_initial);

                INSERT INTO workflow_transitions (id, organization_id, project_id, from_state_id, to_state_id, requires_approval, created_at, updated_at)
                SELECT gen_random_uuid(), origin.organization_id, origin.project_id, origin.id, target.id, d.requires_approval, now(), now()
                FROM (VALUES
                    ('Backlog', 'A fazer', false),
                    ('A fazer', 'Backlog', false),
                    ('A fazer', 'Em andamento', false),
                    ('Em andamento', 'A fazer', false),
                    ('Em andamento', 'Em revisão', false),
                    ('Em revisão', 'Em andamento', false),
                    ('Em revisão', 'Concluída', true),
                    ('Backlog', 'Bloqueada', false),
                    ('A fazer', 'Bloqueada', false),
                    ('Em andamento', 'Bloqueada', false),
                    ('Em revisão', 'Bloqueada', false),
                    ('Bloqueada', 'A fazer', false),
                    ('Bloqueada', 'Em andamento', false),
                    ('Concluída', 'Em andamento', true)) AS d(from_name, to_name, requires_approval)
                JOIN workflow_states origin ON origin.name = d.from_name
                JOIN workflow_states target ON target.project_id = origin.project_id AND target.name = d.to_name;

                UPDATE tasks t
                SET state_id = s.id
                FROM workflow_states s
                WHERE s.project_id = t.project_id AND s.is_initial;

                INSERT INTO task_status_history (id, organization_id, project_id, task_id, from_state_id, from_state_name, to_state_id, to_state_name, changed_by_id, actor_kind, comment, changed_at)
                SELECT gen_random_uuid(), t.organization_id, t.project_id, t.id, NULL, NULL, s.id, s.name, t.created_by_user_id, 'User',
                       'Estado inicial atribuído na migração para o workflow (Fase 07).', now()
                FROM tasks t
                JOIN workflow_states s ON s.id = t.state_id;
                """);

            migrationBuilder.AlterColumn<Guid>(
                name: "state_id",
                table: "tasks",
                type: "uuid",
                nullable: false,
                oldClrType: typeof(Guid),
                oldType: "uuid",
                oldNullable: true);

            migrationBuilder.AddForeignKey(
                name: "fk_tasks_workflow_states_project_id_state_id",
                table: "tasks",
                columns: new[] { "project_id", "state_id" },
                principalTable: "workflow_states",
                principalColumns: new[] { "project_id", "id" },
                onDelete: ReferentialAction.Restrict);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "fk_tasks_workflow_states_project_id_state_id",
                table: "tasks");

            migrationBuilder.DropTable(
                name: "task_status_history");

            migrationBuilder.DropTable(
                name: "workflow_transitions");

            migrationBuilder.DropTable(
                name: "workflow_states");

            migrationBuilder.DropTable(
                name: "workflows");

            migrationBuilder.DropIndex(
                name: "ix_tasks_project_id_state_id",
                table: "tasks");

            migrationBuilder.DropColumn(
                name: "state_id",
                table: "tasks");
        }
    }
}
