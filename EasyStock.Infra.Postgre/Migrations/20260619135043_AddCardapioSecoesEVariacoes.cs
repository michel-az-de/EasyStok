using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace EasyStock.Infra.Postgre.Migrations
{
    /// <inheritdoc />
    public partial class AddCardapioSecoesEVariacoes : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<Guid>(
                name: "CardapioItemId",
                table: "pedido_itens",
                type: "uuid",
                nullable: true);

            migrationBuilder.AddColumn<Guid>(
                name: "CardapioItemVariacaoId",
                table: "pedido_itens",
                type: "uuid",
                nullable: true);

            migrationBuilder.AddColumn<Guid>(
                name: "ProdutoVariacaoId",
                table: "pedido_itens",
                type: "uuid",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "SkuSnapshot",
                table: "pedido_itens",
                type: "character varying(100)",
                maxLength: 100,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "VariacaoRotuloSnapshot",
                table: "pedido_itens",
                type: "character varying(60)",
                maxLength: 60,
                nullable: true);

            migrationBuilder.AddColumn<Guid>(
                name: "SecaoId",
                table: "cardapio_item",
                type: "uuid",
                nullable: true);

            migrationBuilder.CreateTable(
                name: "cardapio_item_variacao",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    CardapioItemId = table.Column<Guid>(type: "uuid", nullable: false),
                    Rotulo = table.Column<string>(type: "character varying(60)", maxLength: 60, nullable: false),
                    PrecoStorefront = table.Column<decimal>(type: "numeric(10,2)", nullable: false),
                    Sku = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: true),
                    ProdutoVariacaoId = table.Column<Guid>(type: "uuid", nullable: true),
                    PesoExibicao = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: true),
                    Disponivel = table.Column<bool>(type: "boolean", nullable: false, defaultValue: true),
                    OrdemExibicao = table.Column<double>(type: "double precision", nullable: false, defaultValue: 0.0),
                    EhPadrao = table.Column<bool>(type: "boolean", nullable: false, defaultValue: false),
                    CriadoEm = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    AlteradoEm = table.Column<DateTime>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_cardapio_item_variacao", x => x.Id);
                    table.ForeignKey(
                        name: "FK_cardapio_item_variacao_cardapio_item_CardapioItemId",
                        column: x => x.CardapioItemId,
                        principalTable: "cardapio_item",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_cardapio_item_variacao_produto_variacoes_ProdutoVariacaoId",
                        column: x => x.ProdutoVariacaoId,
                        principalTable: "produto_variacoes",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "cardapio_secao",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    StorefrontId = table.Column<Guid>(type: "uuid", nullable: false),
                    SecaoPaiId = table.Column<Guid>(type: "uuid", nullable: true),
                    Nome = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    OrdemExibicao = table.Column<double>(type: "double precision", nullable: false, defaultValue: 0.0),
                    Visivel = table.Column<bool>(type: "boolean", nullable: false, defaultValue: true),
                    Nivel = table.Column<short>(type: "smallint", nullable: false),
                    CriadoEm = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    AlteradoEm = table.Column<DateTime>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_cardapio_secao", x => x.Id);
                    table.CheckConstraint("ck_cardapio_secao_nivel", "\"Nivel\" BETWEEN 0 AND 2");
                    table.ForeignKey(
                        name: "FK_cardapio_secao_cardapio_secao_SecaoPaiId",
                        column: x => x.SecaoPaiId,
                        principalTable: "cardapio_secao",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_cardapio_secao_storefront_StorefrontId",
                        column: x => x.StorefrontId,
                        principalTable: "storefront",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_cardapio_item_SecaoId",
                table: "cardapio_item",
                column: "SecaoId");

            migrationBuilder.CreateIndex(
                name: "ix_cardapio_item_variacao_item_ordem",
                table: "cardapio_item_variacao",
                columns: new[] { "CardapioItemId", "OrdemExibicao" });

            migrationBuilder.CreateIndex(
                name: "IX_cardapio_item_variacao_ProdutoVariacaoId",
                table: "cardapio_item_variacao",
                column: "ProdutoVariacaoId");

            migrationBuilder.CreateIndex(
                name: "IX_cardapio_secao_SecaoPaiId",
                table: "cardapio_secao",
                column: "SecaoPaiId");

            migrationBuilder.CreateIndex(
                name: "ix_cardapio_secao_storefront_pai_ordem",
                table: "cardapio_secao",
                columns: new[] { "StorefrontId", "SecaoPaiId", "OrdemExibicao" });

            migrationBuilder.AddForeignKey(
                name: "FK_cardapio_item_cardapio_secao_SecaoId",
                table: "cardapio_item",
                column: "SecaoId",
                principalTable: "cardapio_secao",
                principalColumn: "Id",
                onDelete: ReferentialAction.SetNull);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_cardapio_item_cardapio_secao_SecaoId",
                table: "cardapio_item");

            migrationBuilder.DropTable(
                name: "cardapio_item_variacao");

            migrationBuilder.DropTable(
                name: "cardapio_secao");

            migrationBuilder.DropIndex(
                name: "IX_cardapio_item_SecaoId",
                table: "cardapio_item");

            migrationBuilder.DropColumn(
                name: "CardapioItemId",
                table: "pedido_itens");

            migrationBuilder.DropColumn(
                name: "CardapioItemVariacaoId",
                table: "pedido_itens");

            migrationBuilder.DropColumn(
                name: "ProdutoVariacaoId",
                table: "pedido_itens");

            migrationBuilder.DropColumn(
                name: "SkuSnapshot",
                table: "pedido_itens");

            migrationBuilder.DropColumn(
                name: "VariacaoRotuloSnapshot",
                table: "pedido_itens");

            migrationBuilder.DropColumn(
                name: "SecaoId",
                table: "cardapio_item");
        }
    }
}
