using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace EasyStock.Infra.Postgre.Migrations
{
    /// <inheritdoc />
    public partial class EnriquecerProdutoSubcategoriaECaracteristica : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<Guid>(
                name: "SubcategoriaId",
                table: "produtos",
                type: "uuid",
                nullable: true);

            migrationBuilder.AddColumn<Guid>(
                name: "VariacaoId",
                table: "produto_caracteristicas",
                type: "uuid",
                nullable: true);

            migrationBuilder.CreateIndex(
                name: "IX_produtos_SubcategoriaId",
                table: "produtos",
                column: "SubcategoriaId");

            migrationBuilder.CreateIndex(
                name: "IX_produto_caracteristicas_VariacaoId",
                table: "produto_caracteristicas",
                column: "VariacaoId");

            migrationBuilder.AddForeignKey(
                name: "FK_produto_caracteristicas_produto_variacoes_VariacaoId",
                table: "produto_caracteristicas",
                column: "VariacaoId",
                principalTable: "produto_variacoes",
                principalColumn: "Id");

            migrationBuilder.AddForeignKey(
                name: "FK_produtos_categorias_SubcategoriaId",
                table: "produtos",
                column: "SubcategoriaId",
                principalTable: "categorias",
                principalColumn: "Id");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_produto_caracteristicas_produto_variacoes_VariacaoId",
                table: "produto_caracteristicas");

            migrationBuilder.DropForeignKey(
                name: "FK_produtos_categorias_SubcategoriaId",
                table: "produtos");

            migrationBuilder.DropIndex(
                name: "IX_produtos_SubcategoriaId",
                table: "produtos");

            migrationBuilder.DropIndex(
                name: "IX_produto_caracteristicas_VariacaoId",
                table: "produto_caracteristicas");

            migrationBuilder.DropColumn(
                name: "SubcategoriaId",
                table: "produtos");

            migrationBuilder.DropColumn(
                name: "VariacaoId",
                table: "produto_caracteristicas");
        }
    }
}
