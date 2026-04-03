using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace EasyStock.Infra.Postgre.Migrations
{
    /// <inheritdoc />
    public partial class InitialCreate : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "empresas",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    Nome = table.Column<string>(type: "character varying(150)", maxLength: 150, nullable: false),
                    Documento = table.Column<string>(type: "character varying(30)", maxLength: 30, nullable: true),
                    CriadoEm = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    AlteradoEm = table.Column<DateTime>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_empresas", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "categorias",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    EmpresaId = table.Column<Guid>(type: "uuid", nullable: false),
                    CategoriaPaiId = table.Column<Guid>(type: "uuid", nullable: true),
                    Nome = table.Column<string>(type: "character varying(120)", maxLength: 120, nullable: false),
                    Descricao = table.Column<string>(type: "text", nullable: true),
                    CriadoEm = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    AlteradoEm = table.Column<DateTime>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_categorias", x => x.Id);
                    table.ForeignKey(
                        name: "FK_categorias_categorias_CategoriaPaiId",
                        column: x => x.CategoriaPaiId,
                        principalTable: "categorias",
                        principalColumn: "Id");
                    table.ForeignKey(
                        name: "FK_categorias_empresas_EmpresaId",
                        column: x => x.EmpresaId,
                        principalTable: "empresas",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "vendas",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    EmpresaId = table.Column<Guid>(type: "uuid", nullable: false),
                    Canal = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: false),
                    Natureza = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: false),
                    DataVenda = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    DataEnvio = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    NumeroNotaFiscal = table.Column<string>(type: "character varying(80)", maxLength: 80, nullable: true),
                    ValorTotal = table.Column<decimal>(type: "numeric(18,2)", nullable: false),
                    Observacoes = table.Column<string>(type: "text", nullable: true),
                    CriadoEm = table.Column<DateTime>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_vendas", x => x.Id);
                    table.ForeignKey(
                        name: "FK_vendas_empresas_EmpresaId",
                        column: x => x.EmpresaId,
                        principalTable: "empresas",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "produtos",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    EmpresaId = table.Column<Guid>(type: "uuid", nullable: false),
                    CategoriaId = table.Column<Guid>(type: "uuid", nullable: false),
                    Nome = table.Column<string>(type: "character varying(180)", maxLength: 180, nullable: false),
                    DescricaoBase = table.Column<string>(type: "text", nullable: true),
                    Marca = table.Column<string>(type: "character varying(120)", maxLength: 120, nullable: true),
                    Tipo = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: false),
                    SkuBase = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: true),
                    CodigoBarras = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: true),
                    ControlaValidade = table.Column<bool>(type: "boolean", nullable: false),
                    Peso = table.Column<decimal>(type: "numeric(10,3)", nullable: true),
                    Largura = table.Column<decimal>(type: "numeric(10,2)", nullable: true),
                    Altura = table.Column<decimal>(type: "numeric(10,2)", nullable: true),
                    Comprimento = table.Column<decimal>(type: "numeric(10,2)", nullable: true),
                    CustoReferencia = table.Column<decimal>(type: "numeric(18,2)", nullable: true),
                    PrecoReferencia = table.Column<decimal>(type: "numeric(18,2)", nullable: true),
                    MargemEstimada = table.Column<decimal>(type: "numeric(8,2)", nullable: true),
                    AtributosJson = table.Column<string>(type: "jsonb", nullable: true),
                    FotosJson = table.Column<string>(type: "jsonb", nullable: true),
                    EmbalagemJson = table.Column<string>(type: "jsonb", nullable: true),
                    Ativo = table.Column<bool>(type: "boolean", nullable: false),
                    CriadoEm = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    AlteradoEm = table.Column<DateTime>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_produtos", x => x.Id);
                    table.ForeignKey(
                        name: "FK_produtos_categorias_CategoriaId",
                        column: x => x.CategoriaId,
                        principalTable: "categorias",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_produtos_empresas_EmpresaId",
                        column: x => x.EmpresaId,
                        principalTable: "empresas",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "itens_estoque",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    EmpresaId = table.Column<Guid>(type: "uuid", nullable: false),
                    ProdutoId = table.Column<Guid>(type: "uuid", nullable: false),
                    CodigoInterno = table.Column<string>(type: "character varying(120)", maxLength: 120, nullable: true),
                    CodigoLote = table.Column<string>(type: "character varying(120)", maxLength: 120, nullable: true),
                    CodigoMarketplace = table.Column<string>(type: "character varying(120)", maxLength: 120, nullable: true),
                    VariacaoDescricao = table.Column<string>(type: "character varying(180)", maxLength: 180, nullable: true),
                    Cor = table.Column<string>(type: "character varying(60)", maxLength: 60, nullable: true),
                    Tamanho = table.Column<string>(type: "character varying(60)", maxLength: 60, nullable: true),
                    PesoReal = table.Column<decimal>(type: "numeric(10,3)", nullable: true),
                    LarguraReal = table.Column<decimal>(type: "numeric(10,2)", nullable: true),
                    AlturaReal = table.Column<decimal>(type: "numeric(10,2)", nullable: true),
                    ComprimentoReal = table.Column<decimal>(type: "numeric(10,2)", nullable: true),
                    FornecedorNome = table.Column<string>(type: "text", nullable: true),
                    QuantidadeInicial = table.Column<int>(type: "integer", nullable: false),
                    QuantidadeAtual = table.Column<int>(type: "integer", nullable: false),
                    CustoUnitario = table.Column<decimal>(type: "numeric(18,2)", nullable: false),
                    PrecoVendaSugerido = table.Column<decimal>(type: "numeric(18,2)", nullable: true),
                    EntradaEm = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    ValidadeEm = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    UltimaMovimentacaoEm = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    Status = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: false),
                    Observacoes = table.Column<string>(type: "text", nullable: true),
                    CriadoEm = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    AlteradoEm = table.Column<DateTime>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_itens_estoque", x => x.Id);
                    table.ForeignKey(
                        name: "FK_itens_estoque_empresas_EmpresaId",
                        column: x => x.EmpresaId,
                        principalTable: "empresas",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_itens_estoque_produtos_ProdutoId",
                        column: x => x.ProdutoId,
                        principalTable: "produtos",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "itens_venda",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    VendaId = table.Column<Guid>(type: "uuid", nullable: false),
                    ItemEstoqueId = table.Column<Guid>(type: "uuid", nullable: false),
                    ProdutoId = table.Column<Guid>(type: "uuid", nullable: false),
                    DescricaoSnapshot = table.Column<string>(type: "text", nullable: true),
                    Quantidade = table.Column<int>(type: "integer", nullable: false),
                    PrecoUnitario = table.Column<decimal>(type: "numeric(18,2)", nullable: false),
                    PrecoTotal = table.Column<decimal>(type: "numeric(18,2)", nullable: false),
                    CriadoEm = table.Column<DateTime>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_itens_venda", x => x.Id);
                    table.ForeignKey(
                        name: "FK_itens_venda_itens_estoque_ItemEstoqueId",
                        column: x => x.ItemEstoqueId,
                        principalTable: "itens_estoque",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_itens_venda_produtos_ProdutoId",
                        column: x => x.ProdutoId,
                        principalTable: "produtos",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_itens_venda_vendas_VendaId",
                        column: x => x.VendaId,
                        principalTable: "vendas",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "movimentacoes_estoque",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    EmpresaId = table.Column<Guid>(type: "uuid", nullable: false),
                    ItemEstoqueId = table.Column<Guid>(type: "uuid", nullable: false),
                    ProdutoId = table.Column<Guid>(type: "uuid", nullable: false),
                    VendaId = table.Column<Guid>(type: "uuid", nullable: true),
                    Tipo = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: false),
                    Natureza = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: false),
                    Quantidade = table.Column<int>(type: "integer", nullable: false),
                    ValorUnitario = table.Column<decimal>(type: "numeric(18,2)", nullable: true),
                    ValorTotal = table.Column<decimal>(type: "numeric(18,2)", nullable: true),
                    DataMovimentacao = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    Descricao = table.Column<string>(type: "text", nullable: true),
                    DocumentoReferencia = table.Column<string>(type: "text", nullable: true),
                    CriadoEm = table.Column<DateTime>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_movimentacoes_estoque", x => x.Id);
                    table.ForeignKey(
                        name: "FK_movimentacoes_estoque_empresas_EmpresaId",
                        column: x => x.EmpresaId,
                        principalTable: "empresas",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_movimentacoes_estoque_itens_estoque_ItemEstoqueId",
                        column: x => x.ItemEstoqueId,
                        principalTable: "itens_estoque",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_movimentacoes_estoque_produtos_ProdutoId",
                        column: x => x.ProdutoId,
                        principalTable: "produtos",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_movimentacoes_estoque_vendas_VendaId",
                        column: x => x.VendaId,
                        principalTable: "vendas",
                        principalColumn: "Id");
                });

            migrationBuilder.CreateIndex(
                name: "IX_categorias_CategoriaPaiId",
                table: "categorias",
                column: "CategoriaPaiId");

            migrationBuilder.CreateIndex(
                name: "IX_categorias_EmpresaId",
                table: "categorias",
                column: "EmpresaId");

            migrationBuilder.CreateIndex(
                name: "IX_empresas_Documento",
                table: "empresas",
                column: "Documento",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_itens_estoque_EmpresaId",
                table: "itens_estoque",
                column: "EmpresaId");

            migrationBuilder.CreateIndex(
                name: "IX_itens_estoque_ProdutoId",
                table: "itens_estoque",
                column: "ProdutoId");

            migrationBuilder.CreateIndex(
                name: "IX_itens_venda_ItemEstoqueId",
                table: "itens_venda",
                column: "ItemEstoqueId");

            migrationBuilder.CreateIndex(
                name: "IX_itens_venda_ProdutoId",
                table: "itens_venda",
                column: "ProdutoId");

            migrationBuilder.CreateIndex(
                name: "IX_itens_venda_VendaId",
                table: "itens_venda",
                column: "VendaId");

            migrationBuilder.CreateIndex(
                name: "IX_movimentacoes_estoque_EmpresaId",
                table: "movimentacoes_estoque",
                column: "EmpresaId");

            migrationBuilder.CreateIndex(
                name: "IX_movimentacoes_estoque_ItemEstoqueId",
                table: "movimentacoes_estoque",
                column: "ItemEstoqueId");

            migrationBuilder.CreateIndex(
                name: "IX_movimentacoes_estoque_ProdutoId",
                table: "movimentacoes_estoque",
                column: "ProdutoId");

            migrationBuilder.CreateIndex(
                name: "IX_movimentacoes_estoque_VendaId",
                table: "movimentacoes_estoque",
                column: "VendaId");

            migrationBuilder.CreateIndex(
                name: "IX_produtos_CategoriaId",
                table: "produtos",
                column: "CategoriaId");

            migrationBuilder.CreateIndex(
                name: "IX_produtos_EmpresaId",
                table: "produtos",
                column: "EmpresaId");

            migrationBuilder.CreateIndex(
                name: "IX_vendas_EmpresaId",
                table: "vendas",
                column: "EmpresaId");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "itens_venda");

            migrationBuilder.DropTable(
                name: "movimentacoes_estoque");

            migrationBuilder.DropTable(
                name: "itens_estoque");

            migrationBuilder.DropTable(
                name: "vendas");

            migrationBuilder.DropTable(
                name: "produtos");

            migrationBuilder.DropTable(
                name: "categorias");

            migrationBuilder.DropTable(
                name: "empresas");
        }
    }
}
