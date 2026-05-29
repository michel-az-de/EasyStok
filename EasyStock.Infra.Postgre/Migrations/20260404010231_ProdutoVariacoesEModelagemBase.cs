using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace EasyStock.Infra.Postgre.Migrations
{
    /// <inheritdoc />
    public partial class ProdutoVariacoesEModelagemBase : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "Ativo",
                table: "produtos");

            migrationBuilder.DropColumn(
                name: "EmbalagemJson",
                table: "produtos");

            migrationBuilder.RenameColumn(
                name: "Peso",
                table: "produtos",
                newName: "peso");

            migrationBuilder.RenameColumn(
                name: "Largura",
                table: "produtos",
                newName: "largura");

            migrationBuilder.RenameColumn(
                name: "Comprimento",
                table: "produtos",
                newName: "comprimento");

            migrationBuilder.RenameColumn(
                name: "Altura",
                table: "produtos",
                newName: "altura");

            migrationBuilder.RenameColumn(
                name: "PesoReal",
                table: "itens_estoque",
                newName: "peso_real");

            migrationBuilder.RenameColumn(
                name: "LarguraReal",
                table: "itens_estoque",
                newName: "largura_real");

            migrationBuilder.RenameColumn(
                name: "ComprimentoReal",
                table: "itens_estoque",
                newName: "comprimento_real");

            migrationBuilder.RenameColumn(
                name: "AlturaReal",
                table: "itens_estoque",
                newName: "altura_real");

            migrationBuilder.AddColumn<string>(
                name: "Status",
                table: "produtos",
                type: "character varying(50)",
                maxLength: 50,
                nullable: false,
                defaultValue: "");

            migrationBuilder.AddColumn<string>(
                name: "SugestaoDescricaoAnuncio",
                table: "produtos",
                type: "text",
                nullable: true);

            migrationBuilder.AddColumn<Guid>(
                name: "ProdutoVariacaoId",
                table: "movimentacoes_estoque",
                type: "uuid",
                nullable: true);

            migrationBuilder.AddColumn<Guid>(
                name: "ProdutoVariacaoId",
                table: "itens_venda",
                type: "uuid",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "VariacaoSnapshot",
                table: "itens_venda",
                type: "character varying(180)",
                maxLength: 180,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "ChavePesquisa",
                table: "itens_estoque",
                type: "character varying(300)",
                maxLength: 300,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "DescricaoAnuncio",
                table: "itens_estoque",
                type: "text",
                nullable: true);

            migrationBuilder.AddColumn<Guid>(
                name: "ProdutoVariacaoId",
                table: "itens_estoque",
                type: "uuid",
                nullable: true);

            migrationBuilder.CreateTable(
                name: "produto_caracteristicas",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    EmpresaId = table.Column<Guid>(type: "uuid", nullable: false),
                    ProdutoId = table.Column<Guid>(type: "uuid", nullable: false),
                    Nome = table.Column<string>(type: "character varying(120)", maxLength: 120, nullable: false),
                    Descricao = table.Column<string>(type: "text", nullable: true),
                    QuantidadeReferencia = table.Column<int>(type: "integer", nullable: true),
                    VariacaoPadrao = table.Column<string>(type: "character varying(120)", maxLength: 120, nullable: true),
                    OrdemExibicao = table.Column<int>(type: "integer", nullable: false),
                    CriadoEm = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    AlteradoEm = table.Column<DateTime>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_produto_caracteristicas", x => x.Id);
                    table.ForeignKey(
                        name: "FK_produto_caracteristicas_empresas_EmpresaId",
                        column: x => x.EmpresaId,
                        principalTable: "empresas",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_produto_caracteristicas_produtos_ProdutoId",
                        column: x => x.ProdutoId,
                        principalTable: "produtos",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "produto_embalagens",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    EmpresaId = table.Column<Guid>(type: "uuid", nullable: false),
                    ProdutoId = table.Column<Guid>(type: "uuid", nullable: false),
                    Nome = table.Column<string>(type: "character varying(120)", maxLength: 120, nullable: false),
                    Descricao = table.Column<string>(type: "text", nullable: true),
                    peso = table.Column<decimal>(type: "numeric(10,3)", nullable: true),
                    largura = table.Column<decimal>(type: "numeric(10,2)", nullable: true),
                    altura = table.Column<decimal>(type: "numeric(10,2)", nullable: true),
                    comprimento = table.Column<decimal>(type: "numeric(10,2)", nullable: true),
                    Padrao = table.Column<bool>(type: "boolean", nullable: false),
                    CriadoEm = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    AlteradoEm = table.Column<DateTime>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_produto_embalagens", x => x.Id);
                    table.ForeignKey(
                        name: "FK_produto_embalagens_empresas_EmpresaId",
                        column: x => x.EmpresaId,
                        principalTable: "empresas",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_produto_embalagens_produtos_ProdutoId",
                        column: x => x.ProdutoId,
                        principalTable: "produtos",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "produto_variacoes",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    EmpresaId = table.Column<Guid>(type: "uuid", nullable: false),
                    ProdutoId = table.Column<Guid>(type: "uuid", nullable: false),
                    Nome = table.Column<string>(type: "character varying(120)", maxLength: 120, nullable: false),
                    Cor = table.Column<string>(type: "character varying(60)", maxLength: 60, nullable: true),
                    Tamanho = table.Column<string>(type: "character varying(60)", maxLength: 60, nullable: true),
                    DescricaoComercial = table.Column<string>(type: "text", nullable: true),
                    Sku = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: true),
                    CodigoBarras = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: true),
                    AtributosJson = table.Column<string>(type: "jsonb", nullable: true),
                    peso = table.Column<decimal>(type: "numeric(10,3)", nullable: true),
                    largura = table.Column<decimal>(type: "numeric(10,2)", nullable: true),
                    altura = table.Column<decimal>(type: "numeric(10,2)", nullable: true),
                    comprimento = table.Column<decimal>(type: "numeric(10,2)", nullable: true),
                    Ativa = table.Column<bool>(type: "boolean", nullable: false),
                    CriadoEm = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    AlteradoEm = table.Column<DateTime>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_produto_variacoes", x => x.Id);
                    table.ForeignKey(
                        name: "FK_produto_variacoes_empresas_EmpresaId",
                        column: x => x.EmpresaId,
                        principalTable: "empresas",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_produto_variacoes_produtos_ProdutoId",
                        column: x => x.ProdutoId,
                        principalTable: "produtos",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_movimentacoes_estoque_ProdutoVariacaoId",
                table: "movimentacoes_estoque",
                column: "ProdutoVariacaoId");

            migrationBuilder.CreateIndex(
                name: "IX_itens_venda_ProdutoVariacaoId",
                table: "itens_venda",
                column: "ProdutoVariacaoId");

            migrationBuilder.CreateIndex(
                name: "IX_itens_estoque_ProdutoVariacaoId",
                table: "itens_estoque",
                column: "ProdutoVariacaoId");

            migrationBuilder.CreateIndex(
                name: "IX_produto_caracteristicas_EmpresaId",
                table: "produto_caracteristicas",
                column: "EmpresaId");

            migrationBuilder.CreateIndex(
                name: "IX_produto_caracteristicas_ProdutoId",
                table: "produto_caracteristicas",
                column: "ProdutoId");

            migrationBuilder.CreateIndex(
                name: "IX_produto_embalagens_EmpresaId",
                table: "produto_embalagens",
                column: "EmpresaId");

            migrationBuilder.CreateIndex(
                name: "IX_produto_embalagens_ProdutoId",
                table: "produto_embalagens",
                column: "ProdutoId");

            migrationBuilder.CreateIndex(
                name: "IX_produto_variacoes_EmpresaId",
                table: "produto_variacoes",
                column: "EmpresaId");

            migrationBuilder.CreateIndex(
                name: "IX_produto_variacoes_ProdutoId_Sku",
                table: "produto_variacoes",
                columns: new[] { "ProdutoId", "Sku" });

            migrationBuilder.AddForeignKey(
                name: "FK_itens_estoque_produto_variacoes_ProdutoVariacaoId",
                table: "itens_estoque",
                column: "ProdutoVariacaoId",
                principalTable: "produto_variacoes",
                principalColumn: "Id");

            migrationBuilder.AddForeignKey(
                name: "FK_itens_venda_produto_variacoes_ProdutoVariacaoId",
                table: "itens_venda",
                column: "ProdutoVariacaoId",
                principalTable: "produto_variacoes",
                principalColumn: "Id");

            migrationBuilder.AddForeignKey(
                name: "FK_movimentacoes_estoque_produto_variacoes_ProdutoVariacaoId",
                table: "movimentacoes_estoque",
                column: "ProdutoVariacaoId",
                principalTable: "produto_variacoes",
                principalColumn: "Id");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_itens_estoque_produto_variacoes_ProdutoVariacaoId",
                table: "itens_estoque");

            migrationBuilder.DropForeignKey(
                name: "FK_itens_venda_produto_variacoes_ProdutoVariacaoId",
                table: "itens_venda");

            migrationBuilder.DropForeignKey(
                name: "FK_movimentacoes_estoque_produto_variacoes_ProdutoVariacaoId",
                table: "movimentacoes_estoque");

            migrationBuilder.DropTable(
                name: "produto_caracteristicas");

            migrationBuilder.DropTable(
                name: "produto_embalagens");

            migrationBuilder.DropTable(
                name: "produto_variacoes");

            migrationBuilder.DropIndex(
                name: "IX_movimentacoes_estoque_ProdutoVariacaoId",
                table: "movimentacoes_estoque");

            migrationBuilder.DropIndex(
                name: "IX_itens_venda_ProdutoVariacaoId",
                table: "itens_venda");

            migrationBuilder.DropIndex(
                name: "IX_itens_estoque_ProdutoVariacaoId",
                table: "itens_estoque");

            migrationBuilder.DropColumn(
                name: "Status",
                table: "produtos");

            migrationBuilder.DropColumn(
                name: "SugestaoDescricaoAnuncio",
                table: "produtos");

            migrationBuilder.DropColumn(
                name: "ProdutoVariacaoId",
                table: "movimentacoes_estoque");

            migrationBuilder.DropColumn(
                name: "ProdutoVariacaoId",
                table: "itens_venda");

            migrationBuilder.DropColumn(
                name: "VariacaoSnapshot",
                table: "itens_venda");

            migrationBuilder.DropColumn(
                name: "ChavePesquisa",
                table: "itens_estoque");

            migrationBuilder.DropColumn(
                name: "DescricaoAnuncio",
                table: "itens_estoque");

            migrationBuilder.DropColumn(
                name: "ProdutoVariacaoId",
                table: "itens_estoque");

            migrationBuilder.RenameColumn(
                name: "peso",
                table: "produtos",
                newName: "Peso");

            migrationBuilder.RenameColumn(
                name: "largura",
                table: "produtos",
                newName: "Largura");

            migrationBuilder.RenameColumn(
                name: "comprimento",
                table: "produtos",
                newName: "Comprimento");

            migrationBuilder.RenameColumn(
                name: "altura",
                table: "produtos",
                newName: "Altura");

            migrationBuilder.RenameColumn(
                name: "peso_real",
                table: "itens_estoque",
                newName: "PesoReal");

            migrationBuilder.RenameColumn(
                name: "largura_real",
                table: "itens_estoque",
                newName: "LarguraReal");

            migrationBuilder.RenameColumn(
                name: "comprimento_real",
                table: "itens_estoque",
                newName: "ComprimentoReal");

            migrationBuilder.RenameColumn(
                name: "altura_real",
                table: "itens_estoque",
                newName: "AlturaReal");

            migrationBuilder.AddColumn<bool>(
                name: "Ativo",
                table: "produtos",
                type: "boolean",
                nullable: false,
                defaultValue: false);

            migrationBuilder.AddColumn<string>(
                name: "EmbalagemJson",
                table: "produtos",
                type: "jsonb",
                nullable: true);
        }
    }
}
