using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace ProjectManager.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddAgents : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "agents",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    organization_id = table.Column<Guid>(type: "uuid", nullable: false),
                    name = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    normalized_name = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    description = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: true),
                    status = table.Column<string>(type: "character varying(16)", maxLength: 16, nullable: false),
                    created_by_user_id = table.Column<Guid>(type: "uuid", nullable: false),
                    disabled_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    disabled_by_user_id = table.Column<Guid>(type: "uuid", nullable: true),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    updated_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    xmin = table.Column<uint>(type: "xid", rowVersion: true, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_agents", x => x.id);
                    table.UniqueConstraint("ak_agents_organization_id_id", x => new { x.organization_id, x.id });
                    table.CheckConstraint("ck_agents_status", "status IN ('Active', 'Disabled')");
                    table.ForeignKey(
                        name: "fk_agents_asp_net_users_created_by_user_id",
                        column: x => x.created_by_user_id,
                        principalTable: "users",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "fk_agents_asp_net_users_disabled_by_user_id",
                        column: x => x.disabled_by_user_id,
                        principalTable: "users",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "fk_agents_organizations_organization_id",
                        column: x => x.organization_id,
                        principalTable: "organizations",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "agent_credentials",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    organization_id = table.Column<Guid>(type: "uuid", nullable: false),
                    agent_id = table.Column<Guid>(type: "uuid", nullable: false),
                    name = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    token_hash = table.Column<string>(type: "character(64)", fixedLength: true, maxLength: 64, nullable: false),
                    prefix = table.Column<string>(type: "character(8)", fixedLength: true, maxLength: 8, nullable: false),
                    expires_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    created_by_user_id = table.Column<Guid>(type: "uuid", nullable: false),
                    revoked_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    revoked_by_user_id = table.Column<Guid>(type: "uuid", nullable: true),
                    last_used_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    updated_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    xmin = table.Column<uint>(type: "xid", rowVersion: true, nullable: false),
                    scopes = table.Column<string[]>(type: "text[]", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_agent_credentials", x => x.id);
                    table.UniqueConstraint("ak_agent_credentials_organization_id_id", x => new { x.organization_id, x.id });
                    table.CheckConstraint("ck_agent_credentials_expires_after_creation", "expires_at > created_at");
                    table.CheckConstraint("ck_agent_credentials_scopes_not_empty", "cardinality(scopes) > 0");
                    table.ForeignKey(
                        name: "fk_agent_credentials_agents_organization_id_agent_id",
                        columns: x => new { x.organization_id, x.agent_id },
                        principalTable: "agents",
                        principalColumns: new[] { "organization_id", "id" },
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "fk_agent_credentials_asp_net_users_created_by_user_id",
                        column: x => x.created_by_user_id,
                        principalTable: "users",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "fk_agent_credentials_asp_net_users_revoked_by_user_id",
                        column: x => x.revoked_by_user_id,
                        principalTable: "users",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "agent_project_access",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    organization_id = table.Column<Guid>(type: "uuid", nullable: false),
                    agent_id = table.Column<Guid>(type: "uuid", nullable: false),
                    project_id = table.Column<Guid>(type: "uuid", nullable: false),
                    role = table.Column<string>(type: "character varying(16)", maxLength: 16, nullable: false),
                    granted_by_user_id = table.Column<Guid>(type: "uuid", nullable: false),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    updated_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    xmin = table.Column<uint>(type: "xid", rowVersion: true, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_agent_project_access", x => x.id);
                    table.CheckConstraint("ck_agent_project_access_role", "role IN ('Contributor', 'Viewer')");
                    table.ForeignKey(
                        name: "fk_agent_project_access_agents_organization_id_agent_id",
                        columns: x => new { x.organization_id, x.agent_id },
                        principalTable: "agents",
                        principalColumns: new[] { "organization_id", "id" },
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "fk_agent_project_access_asp_net_users_granted_by_user_id",
                        column: x => x.granted_by_user_id,
                        principalTable: "users",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "fk_agent_project_access_projects_organization_id_project_id",
                        columns: x => new { x.organization_id, x.project_id },
                        principalTable: "projects",
                        principalColumns: new[] { "organization_id", "id" },
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.InsertData(
                table: "permissions",
                columns: new[] { "code", "description" },
                values: new object[] { "agents.manage", "Administrar agentes de IA, credenciais e acessos a projetos" });

            migrationBuilder.CreateIndex(
                name: "ix_agent_credentials_created_by_user_id",
                table: "agent_credentials",
                column: "created_by_user_id");

            migrationBuilder.CreateIndex(
                name: "ix_agent_credentials_organization_id_agent_id",
                table: "agent_credentials",
                columns: new[] { "organization_id", "agent_id" });

            migrationBuilder.CreateIndex(
                name: "ix_agent_credentials_revoked_by_user_id",
                table: "agent_credentials",
                column: "revoked_by_user_id");

            migrationBuilder.CreateIndex(
                name: "ix_agent_credentials_token_hash",
                table: "agent_credentials",
                column: "token_hash",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_agent_project_access_agent_id_project_id",
                table: "agent_project_access",
                columns: new[] { "agent_id", "project_id" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_agent_project_access_granted_by_user_id",
                table: "agent_project_access",
                column: "granted_by_user_id");

            migrationBuilder.CreateIndex(
                name: "ix_agent_project_access_organization_id_agent_id",
                table: "agent_project_access",
                columns: new[] { "organization_id", "agent_id" });

            migrationBuilder.CreateIndex(
                name: "ix_agent_project_access_organization_id_project_id",
                table: "agent_project_access",
                columns: new[] { "organization_id", "project_id" });

            migrationBuilder.CreateIndex(
                name: "ix_agents_created_by_user_id",
                table: "agents",
                column: "created_by_user_id");

            migrationBuilder.CreateIndex(
                name: "ix_agents_disabled_by_user_id",
                table: "agents",
                column: "disabled_by_user_id");

            migrationBuilder.CreateIndex(
                name: "ix_agents_organization_id_normalized_name",
                table: "agents",
                columns: new[] { "organization_id", "normalized_name" },
                unique: true);

            // Migração de dados (ADR-016): os papéis de Proprietário e Administrador das organizações já
            // existentes recebem a permissão nova, como os criados a partir de agora.
            migrationBuilder.Sql("""
                INSERT INTO role_permissions (role_id, permission_code, organization_id)
                SELECT r.id, 'agents.manage', r.organization_id
                FROM roles r
                WHERE r.system_role IN ('Owner', 'Administrator')
                ON CONFLICT DO NOTHING;
                """);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql("DELETE FROM role_permissions WHERE permission_code = 'agents.manage';");

            migrationBuilder.DropTable(
                name: "agent_credentials");

            migrationBuilder.DropTable(
                name: "agent_project_access");

            migrationBuilder.DropTable(
                name: "agents");

            migrationBuilder.DeleteData(
                table: "permissions",
                keyColumn: "code",
                keyValue: "agents.manage");
        }
    }
}
