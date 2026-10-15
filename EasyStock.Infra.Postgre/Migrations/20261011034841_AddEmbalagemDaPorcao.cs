using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace EasyStock.Infra.Postgre.Migrations
{
    /// <inheritdoc />
    public partial class AddEmbalagemDaPorcao : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<Guid>(
                name: "EmbalagemProdutoId",
                table: "cardapio_item_variacao",
                type: "uuid",
                nullable: true);

            migrationBuilder.CreateIndex(
                name: "IX_cardapio_item_variacao_EmbalagemProdutoId",
                table: "cardapio_item_variacao",
                column: "EmbalagemProdutoId");

            migrationBuilder.AddForeignKey(
                name: "FK_cardapio_item_variacao_produtos_EmbalagemProdutoId",
                table: "cardapio_item_variacao",
                column: "EmbalagemProdutoId",
                principalTable: "produtos",
                principalColumn: "Id",
                onDelete: ReferentialAction.SetNull);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_cardapio_item_variacao_produtos_EmbalagemProdutoId",
                table: "cardapio_item_variacao");

            migrationBuilder.DropIndex(
                name: "IX_cardapio_item_variacao_EmbalagemProdutoId",
                table: "cardapio_item_variacao");

            migrationBuilder.DropColumn(
                name: "EmbalagemProdutoId",
                table: "cardapio_item_variacao");
        }
    }
}
