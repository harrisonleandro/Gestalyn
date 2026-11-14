using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace ProjectManager.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddRefreshTokenSuccessor : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<Guid>(
                name: "replaced_by_token_id",
                table: "user_refresh_tokens",
                type: "uuid",
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "replay_count",
                table: "user_refresh_tokens",
                type: "integer",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.CreateIndex(
                name: "ix_user_refresh_tokens_replaced_by_token_id",
                table: "user_refresh_tokens",
                column: "replaced_by_token_id",
                unique: true);

            migrationBuilder.AddForeignKey(
                name: "fk_user_refresh_tokens_user_refresh_tokens_replaced_by_token_id",
                table: "user_refresh_tokens",
                column: "replaced_by_token_id",
                principalTable: "user_refresh_tokens",
                principalColumn: "id");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "fk_user_refresh_tokens_user_refresh_tokens_replaced_by_token_id",
                table: "user_refresh_tokens");

            migrationBuilder.DropIndex(
                name: "ix_user_refresh_tokens_replaced_by_token_id",
                table: "user_refresh_tokens");

            migrationBuilder.DropColumn(
                name: "replaced_by_token_id",
                table: "user_refresh_tokens");

            migrationBuilder.DropColumn(
                name: "replay_count",
                table: "user_refresh_tokens");
        }
    }
}
