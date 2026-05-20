using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace EasyStock.Infra.Postgre.Migrations
{
    /// <inheritdoc />
    public partial class AddPedidoFornecedorItemTable : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "pedidos_fornecedor_itens",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    PedidoFornecedorId = table.Column<Guid>(type: "uuid", nullable: false),
                    ProdutoId = table.Column<Guid>(type: "uuid", nullable: true),
                    Nome = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: false),
                    Unidade = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: true),
                    Quantidade = table.Column<decimal>(type: "decimal(18,4)", nullable: false),
                    QuantidadeRecebida = table.Column<decimal>(type: "decimal(18,4)", nullable: false, defaultValue: 0m),
                    CustoUnitario = table.Column<decimal>(type: "decimal(18,6)", nullable: false),
                    Observacao = table.Column<string>(type: "text", nullable: true),
                    CriadoEm = table.Column<DateTime>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_pedidos_fornecedor_itens", x => x.Id);
                    table.ForeignKey(
                        name: "FK_pedidos_fornecedor_itens_pedidos_fornecedor_PedidoFornecedorId",
                        column: x => x.PedidoFornecedorId,
                        principalTable: "pedidos_fornecedor",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_pedidos_fornecedor_itens_produtos_ProdutoId",
                        column: x => x.ProdutoId,
                        principalTable: "produtos",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.SetNull);
                });

            migrationBuilder.CreateIndex(
                name: "IX_pedidos_fornecedor_itens_PedidoFornecedorId",
                table: "pedidos_fornecedor_itens",
                column: "PedidoFornecedorId");

            migrationBuilder.CreateIndex(
                name: "IX_pedidos_fornecedor_itens_ProdutoId",
                table: "pedidos_fornecedor_itens",
                column: "ProdutoId");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "pedidos_fornecedor_itens");
        }
    }
}
