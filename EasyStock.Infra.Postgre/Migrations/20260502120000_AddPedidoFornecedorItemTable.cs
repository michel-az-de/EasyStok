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
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    pedido_fornecedor_id = table.Column<Guid>(type: "uuid", nullable: false),
                    produto_id = table.Column<Guid>(type: "uuid", nullable: true),
                    nome = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: false),
                    unidade = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: true),
                    quantidade = table.Column<decimal>(type: "decimal(18,4)", nullable: false),
                    quantidade_recebida = table.Column<decimal>(type: "decimal(18,4)", nullable: false, defaultValue: 0m),
                    custo_unitario = table.Column<decimal>(type: "decimal(18,6)", nullable: false),
                    observacao = table.Column<string>(type: "text", nullable: true),
                    criado_em = table.Column<DateTime>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_pedidos_fornecedor_itens", x => x.id);
                    table.ForeignKey(
                        name: "fk_pedidos_fornecedor_itens_pedidos_fornecedor_pedido_fornecedor_id",
                        column: x => x.pedido_fornecedor_id,
                        principalTable: "pedidos_fornecedor",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "fk_pedidos_fornecedor_itens_produtos_produto_id",
                        column: x => x.produto_id,
                        principalTable: "produtos",
                        principalColumn: "id",
                        onDelete: ReferentialAction.SetNull);
                });

            migrationBuilder.CreateIndex(
                name: "ix_pedidos_fornecedor_itens_pedido_fornecedor_id",
                table: "pedidos_fornecedor_itens",
                column: "pedido_fornecedor_id");

            migrationBuilder.CreateIndex(
                name: "ix_pedidos_fornecedor_itens_produto_id",
                table: "pedidos_fornecedor_itens",
                column: "produto_id");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "pedidos_fornecedor_itens");
        }
    }
}
