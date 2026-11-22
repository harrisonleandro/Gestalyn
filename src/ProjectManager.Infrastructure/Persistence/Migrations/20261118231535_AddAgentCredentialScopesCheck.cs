using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace ProjectManager.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddAgentCredentialScopesCheck : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddCheckConstraint(
                name: "ck_agent_credentials_scopes_known",
                table: "agent_credentials",
                sql: "scopes <@ ARRAY['context:read', 'tasks:read', 'tasks:write', 'activities:write', 'reports:write']::text[] AND array_position(scopes, NULL) IS NULL");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropCheckConstraint(
                name: "ck_agent_credentials_scopes_known",
                table: "agent_credentials");
        }
    }
}
