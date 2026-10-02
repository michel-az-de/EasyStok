using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace EasyStock.Infra.Postgre.Migrations
{
    /// <inheritdoc />
    public partial class AddSegredosDeAcessoN8 : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "Canal",
                table: "reset_tokens",
                type: "character varying(20)",
                maxLength: 20,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "Finalidade",
                table: "reset_tokens",
                type: "character varying(20)",
                maxLength: 20,
                nullable: false,
                defaultValue: "Reset");

            migrationBuilder.AddColumn<int>(
                name: "Tentativas",
                table: "reset_tokens",
                type: "integer",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.CreateIndex(
                name: "ix_reset_tokens_usuario_finalidade_aberto",
                table: "reset_tokens",
                columns: new[] { "UsuarioId", "Finalidade" },
                filter: "\"Usado\" = false");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "ix_reset_tokens_usuario_finalidade_aberto",
                table: "reset_tokens");

            migrationBuilder.DropColumn(
                name: "Canal",
                table: "reset_tokens");

            migrationBuilder.DropColumn(
                name: "Finalidade",
                table: "reset_tokens");

            migrationBuilder.DropColumn(
                name: "Tentativas",
                table: "reset_tokens");
        }
    }
}
