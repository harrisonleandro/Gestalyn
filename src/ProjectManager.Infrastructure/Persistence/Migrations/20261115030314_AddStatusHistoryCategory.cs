using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace ProjectManager.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddStatusHistoryCategory : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "to_category",
                table: "task_status_history",
                type: "character varying(16)",
                maxLength: 16,
                nullable: true);

            // Migração de dados (ADR-014): os registros existentes recebem a categoria atual do estado de destino, a
            // melhor informação disponível. Fica nula só se o estado já foi excluído.
            migrationBuilder.Sql("""
                UPDATE task_status_history h
                SET to_category = s.category
                FROM workflow_states s
                WHERE s.id = h.to_state_id;
                """);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "to_category",
                table: "task_status_history");
        }
    }
}
