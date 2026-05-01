using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace EasyStock.Infra.Postgre.Migrations
{
    /// <inheritdoc />
    public partial class AdicionarLimiaresConfiguraveis : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<int>(
                name: "QuantidadeCritica",
                table: "produtos",
                type: "integer",
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "QuantidadeMinima",
                table: "produtos",
                type: "integer",
                nullable: true);

            migrationBuilder.AddColumn<uint>(
                name: "xmin",
                table: "produtos",
                type: "xid",
                rowVersion: true,
                nullable: false,
                defaultValue: 0u);

            migrationBuilder.AddColumn<Guid>(
                name: "LojaId",
                table: "pedidos_fornecedor",
                type: "uuid",
                nullable: true);

            migrationBuilder.AddColumn<uint>(
                name: "xmin",
                table: "pedidos",
                type: "xid",
                rowVersion: true,
                nullable: false,
                defaultValue: 0u);

            migrationBuilder.AddColumn<int>(
                name: "QuantidadeCritica",
                table: "itens_estoque",
                type: "integer",
                nullable: false,
                defaultValue: 2);

            migrationBuilder.AddColumn<int>(
                name: "QuantidadeCriticaPadrao",
                table: "configuracoes_loja",
                type: "integer",
                nullable: false,
                defaultValue: 2);

            migrationBuilder.AddColumn<int>(
                name: "QuantidadeCritica",
                table: "categorias",
                type: "integer",
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "QuantidadeMinima",
                table: "categorias",
                type: "integer",
                nullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "QuantidadeCritica",
                table: "produtos");

            migrationBuilder.DropColumn(
                name: "QuantidadeMinima",
                table: "produtos");

            migrationBuilder.DropColumn(
                name: "xmin",
                table: "produtos");

            migrationBuilder.DropColumn(
                name: "LojaId",
                table: "pedidos_fornecedor");

            migrationBuilder.DropColumn(
                name: "xmin",
                table: "pedidos");

            migrationBuilder.DropColumn(
                name: "QuantidadeCritica",
                table: "itens_estoque");

            migrationBuilder.DropColumn(
                name: "QuantidadeCriticaPadrao",
                table: "configuracoes_loja");

            migrationBuilder.DropColumn(
                name: "QuantidadeCritica",
                table: "categorias");

            migrationBuilder.DropColumn(
                name: "QuantidadeMinima",
                table: "categorias");
        }
    }
}
