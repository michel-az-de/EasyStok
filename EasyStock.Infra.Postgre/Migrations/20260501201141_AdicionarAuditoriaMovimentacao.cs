using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace EasyStock.Infra.Postgre.Migrations
{
    /// <inheritdoc />
    public partial class AdicionarAuditoriaMovimentacao : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "DispositivoId",
                table: "movimentacoes_estoque",
                type: "character varying(120)",
                maxLength: 120,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "Ip",
                table: "movimentacoes_estoque",
                type: "character varying(64)",
                maxLength: 64,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "MotivoEstorno",
                table: "movimentacoes_estoque",
                type: "character varying(300)",
                maxLength: 300,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "UserAgent",
                table: "movimentacoes_estoque",
                type: "character varying(500)",
                maxLength: 500,
                nullable: true);

            migrationBuilder.AddColumn<Guid>(
                name: "UsuarioId",
                table: "movimentacoes_estoque",
                type: "uuid",
                nullable: true);

            migrationBuilder.CreateIndex(
                name: "ix_movimentacoes_empresa_usuario_data",
                table: "movimentacoes_estoque",
                columns: new[] { "EmpresaId", "UsuarioId", "DataMovimentacao" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "ix_movimentacoes_empresa_usuario_data",
                table: "movimentacoes_estoque");

            migrationBuilder.DropColumn(
                name: "DispositivoId",
                table: "movimentacoes_estoque");

            migrationBuilder.DropColumn(
                name: "Ip",
                table: "movimentacoes_estoque");

            migrationBuilder.DropColumn(
                name: "MotivoEstorno",
                table: "movimentacoes_estoque");

            migrationBuilder.DropColumn(
                name: "UserAgent",
                table: "movimentacoes_estoque");

            migrationBuilder.DropColumn(
                name: "UsuarioId",
                table: "movimentacoes_estoque");
        }
    }
}
