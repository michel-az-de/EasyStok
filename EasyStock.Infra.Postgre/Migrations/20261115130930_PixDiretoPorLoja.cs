using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace EasyStock.Infra.Postgre.Migrations
{
    /// <summary>
    /// #2233: chave Pix por loja (tipo, chave, nome e cidade do recebedor) e o copia e cola da cobrança Pix direto.
    /// Só aditiva e anulável: nulo é o comportamento de antes (loja sem chave não oferece Pix direto). As colunas de
    /// <c>lojas</c> herdam a RLS da tabela. O Down só remove as colunas novas e não toca as cobranças do Mercado Pago
    /// nem as da entrega.
    /// </summary>
    public partial class PixDiretoPorLoja : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "PixChave",
                table: "lojas",
                type: "character varying(77)",
                maxLength: 77,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "PixCidade",
                table: "lojas",
                type: "character varying(15)",
                maxLength: 15,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "PixNomeRecebedor",
                table: "lojas",
                type: "character varying(25)",
                maxLength: 25,
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "PixTipo",
                table: "lojas",
                type: "integer",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "PixCopiaCola",
                table: "cobrancas_pedido",
                type: "character varying(512)",
                maxLength: 512,
                nullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "PixChave",
                table: "lojas");

            migrationBuilder.DropColumn(
                name: "PixCidade",
                table: "lojas");

            migrationBuilder.DropColumn(
                name: "PixNomeRecebedor",
                table: "lojas");

            migrationBuilder.DropColumn(
                name: "PixTipo",
                table: "lojas");

            migrationBuilder.DropColumn(
                name: "PixCopiaCola",
                table: "cobrancas_pedido");
        }
    }
}
