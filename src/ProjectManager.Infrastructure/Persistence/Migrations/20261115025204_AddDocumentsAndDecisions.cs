using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace ProjectManager.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddDocumentsAndDecisions : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "decisions",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    organization_id = table.Column<Guid>(type: "uuid", nullable: false),
                    project_id = table.Column<Guid>(type: "uuid", nullable: false),
                    title = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    context = table.Column<string>(type: "character varying(20000)", maxLength: 20000, nullable: false),
                    outcome = table.Column<string>(type: "character varying(20000)", maxLength: 20000, nullable: false),
                    alternatives = table.Column<string>(type: "character varying(20000)", maxLength: 20000, nullable: true),
                    consequences = table.Column<string>(type: "character varying(20000)", maxLength: 20000, nullable: true),
                    task_id = table.Column<Guid>(type: "uuid", nullable: true),
                    status = table.Column<string>(type: "character varying(16)", maxLength: 16, nullable: false),
                    proposed_by_id = table.Column<Guid>(type: "uuid", nullable: false),
                    decided_by_id = table.Column<Guid>(type: "uuid", nullable: true),
                    decided_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    superseded_by_id = table.Column<Guid>(type: "uuid", nullable: true),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    updated_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    xmin = table.Column<uint>(type: "xid", rowVersion: true, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_decisions", x => x.id);
                    table.UniqueConstraint("ak_decisions_organization_id_project_id_id", x => new { x.organization_id, x.project_id, x.id });
                    table.CheckConstraint("ck_decisions_not_self_superseded", "superseded_by_id IS NULL OR superseded_by_id <> id");
                    table.ForeignKey(
                        name: "fk_decisions_decisions_organization_id_project_id_superseded_b",
                        columns: x => new { x.organization_id, x.project_id, x.superseded_by_id },
                        principalTable: "decisions",
                        principalColumns: new[] { "organization_id", "project_id", "id" },
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "fk_decisions_projects_organization_id_project_id",
                        columns: x => new { x.organization_id, x.project_id },
                        principalTable: "projects",
                        principalColumns: new[] { "organization_id", "id" },
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "fk_decisions_tasks_organization_id_project_id_task_id",
                        columns: x => new { x.organization_id, x.project_id, x.task_id },
                        principalTable: "tasks",
                        principalColumns: new[] { "organization_id", "project_id", "id" },
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "fk_decisions_users_proposed_by_id",
                        column: x => x.proposed_by_id,
                        principalTable: "users",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "project_documents",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    organization_id = table.Column<Guid>(type: "uuid", nullable: false),
                    project_id = table.Column<Guid>(type: "uuid", nullable: false),
                    title = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    body = table.Column<string>(type: "character varying(200000)", maxLength: 200000, nullable: false),
                    current_version = table.Column<int>(type: "integer", nullable: false),
                    created_by_user_id = table.Column<Guid>(type: "uuid", nullable: false),
                    archived_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    archived_by_user_id = table.Column<Guid>(type: "uuid", nullable: true),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    updated_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    xmin = table.Column<uint>(type: "xid", rowVersion: true, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_project_documents", x => x.id);
                    table.UniqueConstraint("ak_project_documents_organization_id_project_id_id", x => new { x.organization_id, x.project_id, x.id });
                    table.CheckConstraint("ck_project_documents_version_positive", "current_version > 0");
                    table.ForeignKey(
                        name: "fk_project_documents_projects_organization_id_project_id",
                        columns: x => new { x.organization_id, x.project_id },
                        principalTable: "projects",
                        principalColumns: new[] { "organization_id", "id" },
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "fk_project_documents_users_created_by_user_id",
                        column: x => x.created_by_user_id,
                        principalTable: "users",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "project_document_versions",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    organization_id = table.Column<Guid>(type: "uuid", nullable: false),
                    project_id = table.Column<Guid>(type: "uuid", nullable: false),
                    document_id = table.Column<Guid>(type: "uuid", nullable: false),
                    number = table.Column<int>(type: "integer", nullable: false),
                    title = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    body = table.Column<string>(type: "character varying(200000)", maxLength: 200000, nullable: false),
                    author_id = table.Column<Guid>(type: "uuid", nullable: false),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    xmin = table.Column<uint>(type: "xid", rowVersion: true, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_project_document_versions", x => x.id);
                    table.CheckConstraint("ck_project_document_versions_number_positive", "number > 0");
                    table.ForeignKey(
                        name: "fk_project_document_versions_project_documents_organization_id",
                        columns: x => new { x.organization_id, x.project_id, x.document_id },
                        principalTable: "project_documents",
                        principalColumns: new[] { "organization_id", "project_id", "id" },
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "fk_project_document_versions_users_author_id",
                        column: x => x.author_id,
                        principalTable: "users",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "ix_decisions_organization_id_project_id_superseded_by_id",
                table: "decisions",
                columns: new[] { "organization_id", "project_id", "superseded_by_id" });

            migrationBuilder.CreateIndex(
                name: "ix_decisions_organization_id_project_id_task_id",
                table: "decisions",
                columns: new[] { "organization_id", "project_id", "task_id" });

            migrationBuilder.CreateIndex(
                name: "ix_decisions_project_id_status",
                table: "decisions",
                columns: new[] { "project_id", "status" });

            migrationBuilder.CreateIndex(
                name: "ix_decisions_proposed_by_id",
                table: "decisions",
                column: "proposed_by_id");

            migrationBuilder.CreateIndex(
                name: "ix_project_document_versions_author_id",
                table: "project_document_versions",
                column: "author_id");

            migrationBuilder.CreateIndex(
                name: "ix_project_document_versions_document_id_number",
                table: "project_document_versions",
                columns: new[] { "document_id", "number" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_project_document_versions_organization_id_project_id_docume",
                table: "project_document_versions",
                columns: new[] { "organization_id", "project_id", "document_id" });

            migrationBuilder.CreateIndex(
                name: "ix_project_documents_created_by_user_id",
                table: "project_documents",
                column: "created_by_user_id");

            migrationBuilder.CreateIndex(
                name: "ix_project_documents_project_id_title",
                table: "project_documents",
                columns: new[] { "project_id", "title" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "decisions");

            migrationBuilder.DropTable(
                name: "project_document_versions");

            migrationBuilder.DropTable(
                name: "project_documents");
        }
    }
}
