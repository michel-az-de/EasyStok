using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace EasyStock.Infra.Postgre.Migrations
{
    /// <inheritdoc />
    public partial class AdicionarProdutoAlteracoes : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_produtos_EmpresaId",
                table: "produtos");

            migrationBuilder.DropIndex(
                name: "IX_movimentacoes_estoque_EmpresaId",
                table: "movimentacoes_estoque");

            migrationBuilder.DropIndex(
                name: "IX_itens_estoque_EmpresaId",
                table: "itens_estoque");

            migrationBuilder.RenameIndex(
                name: "IX_itens_venda_VendaId",
                table: "itens_venda",
                newName: "ix_itens_venda_venda_id");

            migrationBuilder.CreateTable(
                name: "produto_alteracoes",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    EmpresaId = table.Column<Guid>(type: "uuid", nullable: false),
                    ProdutoId = table.Column<Guid>(type: "uuid", nullable: false),
                    UsuarioId = table.Column<Guid>(type: "uuid", nullable: false),
                    Acao = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: false),
                    AlteracoesJson = table.Column<string>(type: "text", nullable: true),
                    AlteradoEm = table.Column<DateTime>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_produto_alteracoes", x => x.Id);
                    table.ForeignKey(
                        name: "FK_produto_alteracoes_produtos_ProdutoId",
                        column: x => x.ProdutoId,
                        principalTable: "produtos",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_produto_alteracoes_usuarios_UsuarioId",
                        column: x => x.UsuarioId,
                        principalTable: "usuarios",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.SetNull);
                });

            migrationBuilder.CreateIndex(
                name: "ix_produtos_empresa_alteradoem",
                table: "produtos",
                columns: new[] { "EmpresaId", "AlteradoEm" });

            migrationBuilder.CreateIndex(
                name: "ix_produtos_empresa_sku_unique",
                table: "produtos",
                columns: new[] { "EmpresaId", "SkuBase" },
                unique: true,
                filter: "\"SkuBase\" IS NOT NULL");

            migrationBuilder.CreateIndex(
                name: "ix_produtos_empresa_status",
                table: "produtos",
                columns: new[] { "EmpresaId", "Status" });

            migrationBuilder.CreateIndex(
                name: "ix_movimentacoes_empresa_natureza",
                table: "movimentacoes_estoque",
                columns: new[] { "EmpresaId", "Natureza" });

            migrationBuilder.CreateIndex(
                name: "ix_movimentacoes_empresa_tipo_data",
                table: "movimentacoes_estoque",
                columns: new[] { "EmpresaId", "Tipo", "DataMovimentacao" });

            migrationBuilder.CreateIndex(
                name: "ix_itens_estoque_empresa_quantidade",
                table: "itens_estoque",
                columns: new[] { "EmpresaId", "QuantidadeAtual" });

            migrationBuilder.CreateIndex(
                name: "ix_itens_estoque_empresa_ultima_mov",
                table: "itens_estoque",
                columns: new[] { "EmpresaId", "UltimaMovimentacaoEm" });

            migrationBuilder.CreateIndex(
                name: "IX_produto_alteracoes_EmpresaId_ProdutoId",
                table: "produto_alteracoes",
                columns: new[] { "EmpresaId", "ProdutoId" });

            migrationBuilder.CreateIndex(
                name: "IX_produto_alteracoes_ProdutoId_AlteradoEm",
                table: "produto_alteracoes",
                columns: new[] { "ProdutoId", "AlteradoEm" });

            migrationBuilder.CreateIndex(
                name: "IX_produto_alteracoes_UsuarioId",
                table: "produto_alteracoes",
                column: "UsuarioId");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "produto_alteracoes");

            migrationBuilder.DropIndex(
                name: "ix_produtos_empresa_alteradoem",
                table: "produtos");

            migrationBuilder.DropIndex(
                name: "ix_produtos_empresa_sku_unique",
                table: "produtos");

            migrationBuilder.DropIndex(
                name: "ix_produtos_empresa_status",
                table: "produtos");

            migrationBuilder.DropIndex(
                name: "ix_movimentacoes_empresa_natureza",
                table: "movimentacoes_estoque");

            migrationBuilder.DropIndex(
                name: "ix_movimentacoes_empresa_tipo_data",
                table: "movimentacoes_estoque");

            migrationBuilder.DropIndex(
                name: "ix_itens_estoque_empresa_quantidade",
                table: "itens_estoque");

            migrationBuilder.DropIndex(
                name: "ix_itens_estoque_empresa_ultima_mov",
                table: "itens_estoque");

            migrationBuilder.RenameIndex(
                name: "ix_itens_venda_venda_id",
                table: "itens_venda",
                newName: "IX_itens_venda_VendaId");

            migrationBuilder.CreateIndex(
                name: "IX_produtos_EmpresaId",
                table: "produtos",
                column: "EmpresaId");

            migrationBuilder.CreateIndex(
                name: "IX_movimentacoes_estoque_EmpresaId",
                table: "movimentacoes_estoque",
                column: "EmpresaId");

            migrationBuilder.CreateIndex(
                name: "IX_itens_estoque_EmpresaId",
                table: "itens_estoque",
                column: "EmpresaId");
        }
    }
}
