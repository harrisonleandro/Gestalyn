using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace ProjectManager.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddPlanningAndTasks : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "ix_project_members_organization_id_project_id",
                table: "project_members");

            migrationBuilder.AddColumn<int>(
                name: "next_task_number",
                table: "projects",
                type: "integer",
                nullable: false,
                defaultValue: 1);

            migrationBuilder.AddColumn<string>(
                name: "description",
                table: "project_phases",
                type: "character varying(4000)",
                maxLength: 4000,
                nullable: true);

            migrationBuilder.AddColumn<DateOnly>(
                name: "end_date",
                table: "project_phases",
                type: "date",
                nullable: true);

            migrationBuilder.AddColumn<DateOnly>(
                name: "start_date",
                table: "project_phases",
                type: "date",
                nullable: true);

            migrationBuilder.AddUniqueConstraint(
                name: "ak_project_phases_project_id_id",
                table: "project_phases",
                columns: new[] { "project_id", "id" });

            migrationBuilder.AddUniqueConstraint(
                name: "ak_project_members_organization_id_project_id_user_id",
                table: "project_members",
                columns: new[] { "organization_id", "project_id", "user_id" });

            migrationBuilder.CreateTable(
                name: "milestones",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    organization_id = table.Column<Guid>(type: "uuid", nullable: false),
                    project_id = table.Column<Guid>(type: "uuid", nullable: false),
                    name = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    description = table.Column<string>(type: "character varying(4000)", maxLength: 4000, nullable: true),
                    due_date = table.Column<DateOnly>(type: "date", nullable: true),
                    phase_id = table.Column<Guid>(type: "uuid", nullable: true),
                    reached_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    updated_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    xmin = table.Column<uint>(type: "xid", rowVersion: true, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_milestones", x => x.id);
                    table.UniqueConstraint("ak_milestones_project_id_id", x => new { x.project_id, x.id });
                    table.ForeignKey(
                        name: "fk_milestones_project_phases_project_id_phase_id",
                        columns: x => new { x.project_id, x.phase_id },
                        principalTable: "project_phases",
                        principalColumns: new[] { "project_id", "id" },
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "fk_milestones_projects_organization_id_project_id",
                        columns: x => new { x.organization_id, x.project_id },
                        principalTable: "projects",
                        principalColumns: new[] { "organization_id", "id" },
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "tasks",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    organization_id = table.Column<Guid>(type: "uuid", nullable: false),
                    project_id = table.Column<Guid>(type: "uuid", nullable: false),
                    number = table.Column<int>(type: "integer", nullable: false),
                    title = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    description = table.Column<string>(type: "character varying(20000)", maxLength: 20000, nullable: true),
                    priority = table.Column<string>(type: "character varying(16)", maxLength: 16, nullable: false),
                    due_date = table.Column<DateOnly>(type: "date", nullable: true),
                    phase_id = table.Column<Guid>(type: "uuid", nullable: true),
                    milestone_id = table.Column<Guid>(type: "uuid", nullable: true),
                    created_by_user_id = table.Column<Guid>(type: "uuid", nullable: false),
                    archived_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    archived_by_user_id = table.Column<Guid>(type: "uuid", nullable: true),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    updated_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    xmin = table.Column<uint>(type: "xid", rowVersion: true, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_tasks", x => x.id);
                    table.UniqueConstraint("ak_tasks_organization_id_project_id_id", x => new { x.organization_id, x.project_id, x.id });
                    table.CheckConstraint("ck_tasks_number_positive", "number > 0");
                    table.ForeignKey(
                        name: "fk_tasks_milestones_project_id_milestone_id",
                        columns: x => new { x.project_id, x.milestone_id },
                        principalTable: "milestones",
                        principalColumns: new[] { "project_id", "id" },
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "fk_tasks_project_phases_project_id_phase_id",
                        columns: x => new { x.project_id, x.phase_id },
                        principalTable: "project_phases",
                        principalColumns: new[] { "project_id", "id" },
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "fk_tasks_projects_organization_id_project_id",
                        columns: x => new { x.organization_id, x.project_id },
                        principalTable: "projects",
                        principalColumns: new[] { "organization_id", "id" },
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "fk_tasks_users_created_by_user_id",
                        column: x => x.created_by_user_id,
                        principalTable: "users",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "task_assignments",
                columns: table => new
                {
                    task_id = table.Column<Guid>(type: "uuid", nullable: false),
                    user_id = table.Column<Guid>(type: "uuid", nullable: false),
                    organization_id = table.Column<Guid>(type: "uuid", nullable: false),
                    project_id = table.Column<Guid>(type: "uuid", nullable: false),
                    assigned_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_task_assignments", x => new { x.task_id, x.user_id });
                    table.ForeignKey(
                        name: "fk_task_assignments_project_members_organization_id_project_id",
                        columns: x => new { x.organization_id, x.project_id, x.user_id },
                        principalTable: "project_members",
                        principalColumns: new[] { "organization_id", "project_id", "user_id" },
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "fk_task_assignments_tasks_organization_id_project_id_task_id",
                        columns: x => new { x.organization_id, x.project_id, x.task_id },
                        principalTable: "tasks",
                        principalColumns: new[] { "organization_id", "project_id", "id" },
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "task_comments",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    organization_id = table.Column<Guid>(type: "uuid", nullable: false),
                    project_id = table.Column<Guid>(type: "uuid", nullable: false),
                    task_id = table.Column<Guid>(type: "uuid", nullable: false),
                    author_user_id = table.Column<Guid>(type: "uuid", nullable: false),
                    body = table.Column<string>(type: "character varying(10000)", maxLength: 10000, nullable: false),
                    edited_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    updated_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    xmin = table.Column<uint>(type: "xid", rowVersion: true, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_task_comments", x => x.id);
                    table.ForeignKey(
                        name: "fk_task_comments_tasks_organization_id_project_id_task_id",
                        columns: x => new { x.organization_id, x.project_id, x.task_id },
                        principalTable: "tasks",
                        principalColumns: new[] { "organization_id", "project_id", "id" },
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "fk_task_comments_users_author_user_id",
                        column: x => x.author_user_id,
                        principalTable: "users",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "task_dependencies",
                columns: table => new
                {
                    task_id = table.Column<Guid>(type: "uuid", nullable: false),
                    depends_on_task_id = table.Column<Guid>(type: "uuid", nullable: false),
                    organization_id = table.Column<Guid>(type: "uuid", nullable: false),
                    project_id = table.Column<Guid>(type: "uuid", nullable: false),
                    created_by_user_id = table.Column<Guid>(type: "uuid", nullable: false),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_task_dependencies", x => new { x.task_id, x.depends_on_task_id });
                    table.CheckConstraint("ck_task_dependencies_not_self", "task_id <> depends_on_task_id");
                    table.ForeignKey(
                        name: "fk_task_dependencies_tasks_organization_id_project_id_depends_",
                        columns: x => new { x.organization_id, x.project_id, x.depends_on_task_id },
                        principalTable: "tasks",
                        principalColumns: new[] { "organization_id", "project_id", "id" },
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "fk_task_dependencies_tasks_organization_id_project_id_task_id",
                        columns: x => new { x.organization_id, x.project_id, x.task_id },
                        principalTable: "tasks",
                        principalColumns: new[] { "organization_id", "project_id", "id" },
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "fk_task_dependencies_users_created_by_user_id",
                        column: x => x.created_by_user_id,
                        principalTable: "users",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "task_labels",
                columns: table => new
                {
                    task_id = table.Column<Guid>(type: "uuid", nullable: false),
                    normalized_name = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: false),
                    organization_id = table.Column<Guid>(type: "uuid", nullable: false),
                    project_id = table.Column<Guid>(type: "uuid", nullable: false),
                    name = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_task_labels", x => new { x.task_id, x.normalized_name });
                    table.ForeignKey(
                        name: "fk_task_labels_tasks_organization_id_project_id_task_id",
                        columns: x => new { x.organization_id, x.project_id, x.task_id },
                        principalTable: "tasks",
                        principalColumns: new[] { "organization_id", "project_id", "id" },
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.AddCheckConstraint(
                name: "ck_project_phases_end_date_after_start",
                table: "project_phases",
                sql: "end_date IS NULL OR start_date IS NULL OR end_date >= start_date");

            migrationBuilder.CreateIndex(
                name: "ix_milestones_organization_id_project_id",
                table: "milestones",
                columns: new[] { "organization_id", "project_id" });

            migrationBuilder.CreateIndex(
                name: "ix_milestones_project_id_phase_id",
                table: "milestones",
                columns: new[] { "project_id", "phase_id" });

            migrationBuilder.CreateIndex(
                name: "ix_task_assignments_organization_id_project_id_task_id",
                table: "task_assignments",
                columns: new[] { "organization_id", "project_id", "task_id" });

            migrationBuilder.CreateIndex(
                name: "ix_task_assignments_organization_id_project_id_user_id",
                table: "task_assignments",
                columns: new[] { "organization_id", "project_id", "user_id" });

            migrationBuilder.CreateIndex(
                name: "ix_task_assignments_project_id_user_id",
                table: "task_assignments",
                columns: new[] { "project_id", "user_id" });

            migrationBuilder.CreateIndex(
                name: "ix_task_comments_author_user_id",
                table: "task_comments",
                column: "author_user_id");

            migrationBuilder.CreateIndex(
                name: "ix_task_comments_organization_id_project_id_task_id",
                table: "task_comments",
                columns: new[] { "organization_id", "project_id", "task_id" });

            migrationBuilder.CreateIndex(
                name: "ix_task_comments_task_id_created_at",
                table: "task_comments",
                columns: new[] { "task_id", "created_at" });

            migrationBuilder.CreateIndex(
                name: "ix_task_dependencies_created_by_user_id",
                table: "task_dependencies",
                column: "created_by_user_id");

            migrationBuilder.CreateIndex(
                name: "ix_task_dependencies_depends_on_task_id",
                table: "task_dependencies",
                column: "depends_on_task_id");

            migrationBuilder.CreateIndex(
                name: "ix_task_dependencies_organization_id_project_id_depends_on_tas",
                table: "task_dependencies",
                columns: new[] { "organization_id", "project_id", "depends_on_task_id" });

            migrationBuilder.CreateIndex(
                name: "ix_task_dependencies_organization_id_project_id_task_id",
                table: "task_dependencies",
                columns: new[] { "organization_id", "project_id", "task_id" });

            migrationBuilder.CreateIndex(
                name: "ix_task_labels_organization_id_project_id_task_id",
                table: "task_labels",
                columns: new[] { "organization_id", "project_id", "task_id" });

            migrationBuilder.CreateIndex(
                name: "ix_task_labels_project_id_normalized_name",
                table: "task_labels",
                columns: new[] { "project_id", "normalized_name" });

            migrationBuilder.CreateIndex(
                name: "ix_tasks_created_by_user_id",
                table: "tasks",
                column: "created_by_user_id");

            migrationBuilder.CreateIndex(
                name: "ix_tasks_project_id_due_date",
                table: "tasks",
                columns: new[] { "project_id", "due_date" });

            migrationBuilder.CreateIndex(
                name: "ix_tasks_project_id_milestone_id",
                table: "tasks",
                columns: new[] { "project_id", "milestone_id" });

            migrationBuilder.CreateIndex(
                name: "ix_tasks_project_id_number",
                table: "tasks",
                columns: new[] { "project_id", "number" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_tasks_project_id_phase_id",
                table: "tasks",
                columns: new[] { "project_id", "phase_id" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "task_assignments");

            migrationBuilder.DropTable(
                name: "task_comments");

            migrationBuilder.DropTable(
                name: "task_dependencies");

            migrationBuilder.DropTable(
                name: "task_labels");

            migrationBuilder.DropTable(
                name: "tasks");

            migrationBuilder.DropTable(
                name: "milestones");

            migrationBuilder.DropUniqueConstraint(
                name: "ak_project_phases_project_id_id",
                table: "project_phases");

            migrationBuilder.DropCheckConstraint(
                name: "ck_project_phases_end_date_after_start",
                table: "project_phases");

            migrationBuilder.DropUniqueConstraint(
                name: "ak_project_members_organization_id_project_id_user_id",
                table: "project_members");

            migrationBuilder.DropColumn(
                name: "next_task_number",
                table: "projects");

            migrationBuilder.DropColumn(
                name: "description",
                table: "project_phases");

            migrationBuilder.DropColumn(
                name: "end_date",
                table: "project_phases");

            migrationBuilder.DropColumn(
                name: "start_date",
                table: "project_phases");

            migrationBuilder.CreateIndex(
                name: "ix_project_members_organization_id_project_id",
                table: "project_members",
                columns: new[] { "organization_id", "project_id" });
        }
    }
}
