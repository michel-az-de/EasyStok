using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace EasyStock.Infra.Postgre.Migrations
{
    /// <inheritdoc />
    public partial class AuditoriaCompletaMovimentacaoEstoque : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "movimentacao_estoque_alteracoes",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    EmpresaId = table.Column<Guid>(type: "uuid", nullable: false),
                    MovimentacaoEstoqueId = table.Column<Guid>(type: "uuid", nullable: false),
                    UsuarioId = table.Column<Guid>(type: "uuid", nullable: false),
                    NomeUsuario = table.Column<string>(type: "character varying(256)", maxLength: 256, nullable: true),
                    EmailUsuario = table.Column<string>(type: "character varying(256)", maxLength: 256, nullable: true),
                    Acao = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: false),
                    Motivo = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: true),
                    Observacao = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: true),
                    AlteracoesJson = table.Column<string>(type: "text", nullable: true),
                    Ip = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: true),
                    UserAgent = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: true),
                    AlteradoEm = table.Column<DateTime>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_movimentacao_estoque_alteracoes", x => x.Id);
                    table.ForeignKey(
                        name: "FK_movimentacao_estoque_alteracoes_movimentacoes_estoque_Movim~",
                        column: x => x.MovimentacaoEstoqueId,
                        principalTable: "movimentacoes_estoque",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_movimentacao_estoque_alteracoes_usuarios_UsuarioId",
                        column: x => x.UsuarioId,
                        principalTable: "usuarios",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.SetNull);
                });

            migrationBuilder.CreateIndex(
                name: "IX_movimentacao_estoque_alteracoes_EmpresaId_AlteradoEm",
                table: "movimentacao_estoque_alteracoes",
                columns: new[] { "EmpresaId", "AlteradoEm" });

            migrationBuilder.CreateIndex(
                name: "IX_movimentacao_estoque_alteracoes_MovimentacaoEstoqueId_Alter~",
                table: "movimentacao_estoque_alteracoes",
                columns: new[] { "MovimentacaoEstoqueId", "AlteradoEm" });

            migrationBuilder.CreateIndex(
                name: "IX_movimentacao_estoque_alteracoes_UsuarioId",
                table: "movimentacao_estoque_alteracoes",
                column: "UsuarioId");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "movimentacao_estoque_alteracoes");
        }
    }
}
