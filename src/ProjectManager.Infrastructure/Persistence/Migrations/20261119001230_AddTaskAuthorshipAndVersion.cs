using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace ProjectManager.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddTaskAuthorshipAndVersion : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AlterColumn<Guid>(
                name: "created_by_user_id",
                table: "tasks",
                type: "uuid",
                nullable: true,
                oldClrType: typeof(Guid),
                oldType: "uuid");

            migrationBuilder.AddColumn<Guid>(
                name: "created_by_agent_id",
                table: "tasks",
                type: "uuid",
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "version",
                table: "tasks",
                type: "integer",
                nullable: false,
                defaultValue: 1);

            migrationBuilder.CreateIndex(
                name: "ix_tasks_organization_id_created_by_agent_id",
                table: "tasks",
                columns: new[] { "organization_id", "created_by_agent_id" });

            migrationBuilder.AddCheckConstraint(
                name: "ck_tasks_single_author",
                table: "tasks",
                sql: "num_nonnulls(created_by_user_id, created_by_agent_id) = 1");

            migrationBuilder.AddForeignKey(
                name: "fk_tasks_agents_organization_id_created_by_agent_id",
                table: "tasks",
                columns: new[] { "organization_id", "created_by_agent_id" },
                principalTable: "agents",
                principalColumns: new[] { "organization_id", "id" },
                onDelete: ReferentialAction.Restrict);

            // Autoria imutável (ADR-016, D6): depois da criação, nenhum UPDATE troca o autor da tarefa. Escrito à mão,
            // como os gatilhos da Fase 08.
            migrationBuilder.Sql("""
                CREATE FUNCTION reject_task_author_change() RETURNS trigger LANGUAGE plpgsql AS $$
                BEGIN
                    IF NEW.created_by_user_id IS DISTINCT FROM OLD.created_by_user_id
                        OR NEW.created_by_agent_id IS DISTINCT FROM OLD.created_by_agent_id THEN
                        RAISE EXCEPTION 'A autoria de uma tarefa não pode ser alterada (tarefa %).', OLD.id
                            USING ERRCODE = 'check_violation';
                    END IF;
                    RETURN NEW;
                END;
                $$;

                CREATE TRIGGER tasks_author_immutable BEFORE UPDATE OF created_by_user_id, created_by_agent_id ON tasks
                    FOR EACH ROW EXECUTE FUNCTION reject_task_author_change();
                """);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql("""
                DROP TRIGGER tasks_author_immutable ON tasks;
                DROP FUNCTION reject_task_author_change();
                """);

            migrationBuilder.DropForeignKey(
                name: "fk_tasks_agents_organization_id_created_by_agent_id",
                table: "tasks");

            migrationBuilder.DropIndex(
                name: "ix_tasks_organization_id_created_by_agent_id",
                table: "tasks");

            migrationBuilder.DropCheckConstraint(
                name: "ck_tasks_single_author",
                table: "tasks");

            migrationBuilder.DropColumn(
                name: "created_by_agent_id",
                table: "tasks");

            migrationBuilder.DropColumn(
                name: "version",
                table: "tasks");

            migrationBuilder.AlterColumn<Guid>(
                name: "created_by_user_id",
                table: "tasks",
                type: "uuid",
                nullable: false,
                defaultValue: new Guid("00000000-0000-0000-0000-000000000000"),
                oldClrType: typeof(Guid),
                oldType: "uuid",
                oldNullable: true);
        }
    }
}
