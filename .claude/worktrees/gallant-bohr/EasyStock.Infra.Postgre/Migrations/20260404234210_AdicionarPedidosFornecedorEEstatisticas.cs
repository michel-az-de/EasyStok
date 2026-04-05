using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace EasyStock.Infra.Postgre.Migrations
{
    /// <inheritdoc />
    public partial class AdicionarPedidosFornecedorEEstatisticas : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "Categoria",
                table: "fornecedores",
                type: "character varying(120)",
                maxLength: 120,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "FretePadrao",
                table: "fornecedores",
                type: "character varying(120)",
                maxLength: 120,
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "LeadTimeEstimadoDias",
                table: "fornecedores",
                type: "integer",
                nullable: true);

            migrationBuilder.AddColumn<decimal>(
                name: "LeadTimeRealMedioDias",
                table: "fornecedores",
                type: "numeric(10,2)",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "Observacoes",
                table: "fornecedores",
                type: "text",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "PedidoMinimo",
                table: "fornecedores",
                type: "character varying(120)",
                maxLength: 120,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "SiteUrl",
                table: "fornecedores",
                type: "character varying(255)",
                maxLength: 255,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "Tipo",
                table: "fornecedores",
                type: "character varying(60)",
                maxLength: 60,
                nullable: true);

            migrationBuilder.CreateTable(
                name: "pedidos_fornecedor",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    EmpresaId = table.Column<Guid>(type: "uuid", nullable: false),
                    FornecedorId = table.Column<Guid>(type: "uuid", nullable: false),
                    DataPedido = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    PrevisaoEntrega = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    DataRecebimento = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    ValorEstimado = table.Column<decimal>(type: "numeric(18,2)", nullable: true),
                    Status = table.Column<string>(type: "character varying(40)", maxLength: 40, nullable: false),
                    Canal = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: true),
                    Tracking = table.Column<string>(type: "character varying(120)", maxLength: 120, nullable: true),
                    Observacoes = table.Column<string>(type: "text", nullable: true),
                    CriadoEm = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    AlteradoEm = table.Column<DateTime>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_pedidos_fornecedor", x => x.Id);
                    table.ForeignKey(
                        name: "FK_pedidos_fornecedor_fornecedores_FornecedorId",
                        column: x => x.FornecedorId,
                        principalTable: "fornecedores",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_pedidos_fornecedor_EmpresaId_DataPedido",
                table: "pedidos_fornecedor",
                columns: new[] { "EmpresaId", "DataPedido" });

            migrationBuilder.CreateIndex(
                name: "IX_pedidos_fornecedor_EmpresaId_FornecedorId_Status",
                table: "pedidos_fornecedor",
                columns: new[] { "EmpresaId", "FornecedorId", "Status" });

            migrationBuilder.CreateIndex(
                name: "IX_pedidos_fornecedor_FornecedorId",
                table: "pedidos_fornecedor",
                column: "FornecedorId");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "pedidos_fornecedor");

            migrationBuilder.DropColumn(
                name: "Categoria",
                table: "fornecedores");

            migrationBuilder.DropColumn(
                name: "FretePadrao",
                table: "fornecedores");

            migrationBuilder.DropColumn(
                name: "LeadTimeEstimadoDias",
                table: "fornecedores");

            migrationBuilder.DropColumn(
                name: "LeadTimeRealMedioDias",
                table: "fornecedores");

            migrationBuilder.DropColumn(
                name: "Observacoes",
                table: "fornecedores");

            migrationBuilder.DropColumn(
                name: "PedidoMinimo",
                table: "fornecedores");

            migrationBuilder.DropColumn(
                name: "SiteUrl",
                table: "fornecedores");

            migrationBuilder.DropColumn(
                name: "Tipo",
                table: "fornecedores");
        }
    }
}
