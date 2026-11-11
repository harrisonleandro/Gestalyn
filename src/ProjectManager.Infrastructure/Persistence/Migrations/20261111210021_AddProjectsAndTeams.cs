using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

#pragma warning disable CA1814 // Prefer jagged arrays over multidimensional

namespace ProjectManager.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddProjectsAndTeams : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "ix_organization_members_organization_id_user_id",
                table: "organization_members");

            migrationBuilder.AddColumn<DateTimeOffset>(
                name: "archived_at",
                table: "projects",
                type: "timestamp with time zone",
                nullable: true);

            migrationBuilder.AddColumn<Guid>(
                name: "archived_by_user_id",
                table: "projects",
                type: "uuid",
                nullable: true);

            migrationBuilder.AddColumn<DateOnly>(
                name: "due_date",
                table: "projects",
                type: "date",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "priority",
                table: "projects",
                type: "character varying(16)",
                maxLength: 16,
                nullable: false,
                defaultValue: "Medium");

            migrationBuilder.AddColumn<DateOnly>(
                name: "start_date",
                table: "projects",
                type: "date",
                nullable: true);

            migrationBuilder.AddUniqueConstraint(
                name: "ak_organization_members_organization_id_user_id",
                table: "organization_members",
                columns: new[] { "organization_id", "user_id" });

            migrationBuilder.CreateTable(
                name: "project_members",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    organization_id = table.Column<Guid>(type: "uuid", nullable: false),
                    project_id = table.Column<Guid>(type: "uuid", nullable: false),
                    user_id = table.Column<Guid>(type: "uuid", nullable: false),
                    role = table.Column<string>(type: "character varying(16)", maxLength: 16, nullable: false),
                    is_responsible = table.Column<bool>(type: "boolean", nullable: false),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    updated_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    xmin = table.Column<uint>(type: "xid", rowVersion: true, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_project_members", x => x.id);
                    table.ForeignKey(
                        name: "fk_project_members_organization_members_organization_id_user_id",
                        columns: x => new { x.organization_id, x.user_id },
                        principalTable: "organization_members",
                        principalColumns: new[] { "organization_id", "user_id" },
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "fk_project_members_projects_organization_id_project_id",
                        columns: x => new { x.organization_id, x.project_id },
                        principalTable: "projects",
                        principalColumns: new[] { "organization_id", "id" },
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "project_repositories",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    organization_id = table.Column<Guid>(type: "uuid", nullable: false),
                    project_id = table.Column<Guid>(type: "uuid", nullable: false),
                    url = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: false),
                    name = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: true),
                    provider = table.Column<string>(type: "character varying(16)", maxLength: 16, nullable: false),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    updated_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    xmin = table.Column<uint>(type: "xid", rowVersion: true, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_project_repositories", x => x.id);
                    table.ForeignKey(
                        name: "fk_project_repositories_projects_organization_id_project_id",
                        columns: x => new { x.organization_id, x.project_id },
                        principalTable: "projects",
                        principalColumns: new[] { "organization_id", "id" },
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "project_tags",
                columns: table => new
                {
                    project_id = table.Column<Guid>(type: "uuid", nullable: false),
                    normalized_name = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: false),
                    organization_id = table.Column<Guid>(type: "uuid", nullable: false),
                    name = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_project_tags", x => new { x.project_id, x.normalized_name });
                    table.ForeignKey(
                        name: "fk_project_tags_projects_organization_id_project_id",
                        columns: x => new { x.organization_id, x.project_id },
                        principalTable: "projects",
                        principalColumns: new[] { "organization_id", "id" },
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.InsertData(
                table: "permissions",
                columns: new[] { "code", "description" },
                values: new object[,]
                {
                    { "projects.create", "Criar projetos" },
                    { "projects.manage_all", "Administrar todos os projetos da organização" },
                    { "projects.read_all", "Ver todos os projetos da organização" }
                });

            // Migração de dados (ADR-010): os papéis de sistema das organizações já existentes recebem as
            // permissões novas, como os papéis criados a partir de agora (Role.CreateSystemRoles).
            migrationBuilder.Sql("""
                INSERT INTO role_permissions (role_id, permission_code, organization_id)
                SELECT r.id, p.code, r.organization_id
                FROM roles r
                CROSS JOIN (VALUES ('projects.create'), ('projects.read_all'), ('projects.manage_all')) AS p(code)
                WHERE r.system_role IN ('Owner', 'Administrator')
                   OR (r.system_role = 'Member' AND p.code = 'projects.create')
                ON CONFLICT DO NOTHING;
                """);

            migrationBuilder.CreateIndex(
                name: "ix_projects_organization_id_name",
                table: "projects",
                columns: new[] { "organization_id", "name" });

            migrationBuilder.AddCheckConstraint(
                name: "ck_projects_due_date_after_start",
                table: "projects",
                sql: "due_date IS NULL OR start_date IS NULL OR due_date >= start_date");

            migrationBuilder.CreateIndex(
                name: "ix_project_members_organization_id_project_id",
                table: "project_members",
                columns: new[] { "organization_id", "project_id" });

            migrationBuilder.CreateIndex(
                name: "ix_project_members_organization_id_user_id",
                table: "project_members",
                columns: new[] { "organization_id", "user_id" });

            migrationBuilder.CreateIndex(
                name: "ix_project_members_project_id_user_id",
                table: "project_members",
                columns: new[] { "project_id", "user_id" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_project_members_responsible",
                table: "project_members",
                column: "project_id",
                unique: true,
                filter: "is_responsible");

            migrationBuilder.CreateIndex(
                name: "ix_project_repositories_organization_id_project_id",
                table: "project_repositories",
                columns: new[] { "organization_id", "project_id" });

            migrationBuilder.CreateIndex(
                name: "ix_project_repositories_project_id_url",
                table: "project_repositories",
                columns: new[] { "project_id", "url" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_project_tags_organization_id_normalized_name",
                table: "project_tags",
                columns: new[] { "organization_id", "normalized_name" });

            migrationBuilder.CreateIndex(
                name: "ix_project_tags_organization_id_project_id",
                table: "project_tags",
                columns: new[] { "organization_id", "project_id" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "project_members");

            migrationBuilder.DropTable(
                name: "project_repositories");

            migrationBuilder.DropTable(
                name: "project_tags");

            migrationBuilder.DropIndex(
                name: "ix_projects_organization_id_name",
                table: "projects");

            migrationBuilder.DropCheckConstraint(
                name: "ck_projects_due_date_after_start",
                table: "projects");

            migrationBuilder.DropUniqueConstraint(
                name: "ak_organization_members_organization_id_user_id",
                table: "organization_members");

            migrationBuilder.Sql("""
                DELETE FROM role_permissions
                WHERE permission_code IN ('projects.create', 'projects.read_all', 'projects.manage_all');
                """);

            migrationBuilder.DeleteData(
                table: "permissions",
                keyColumn: "code",
                keyValue: "projects.create");

            migrationBuilder.DeleteData(
                table: "permissions",
                keyColumn: "code",
                keyValue: "projects.manage_all");

            migrationBuilder.DeleteData(
                table: "permissions",
                keyColumn: "code",
                keyValue: "projects.read_all");

            migrationBuilder.DropColumn(
                name: "archived_at",
                table: "projects");

            migrationBuilder.DropColumn(
                name: "archived_by_user_id",
                table: "projects");

            migrationBuilder.DropColumn(
                name: "due_date",
                table: "projects");

            migrationBuilder.DropColumn(
                name: "priority",
                table: "projects");

            migrationBuilder.DropColumn(
                name: "start_date",
                table: "projects");

            migrationBuilder.CreateIndex(
                name: "ix_organization_members_organization_id_user_id",
                table: "organization_members",
                columns: new[] { "organization_id", "user_id" },
                unique: true);
        }
    }
}
