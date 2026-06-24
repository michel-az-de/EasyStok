using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace EasyStock.Infra.Postgre.Migrations
{
    /// <inheritdoc />
    public partial class AddStorefrontFreteRaioConfig : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<double>(
                name: "CozinhaLat",
                table: "storefront",
                type: "double precision",
                nullable: true);

            migrationBuilder.AddColumn<double>(
                name: "CozinhaLng",
                table: "storefront",
                type: "double precision",
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "FreteFaixaGratisMetros",
                table: "storefront",
                type: "integer",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "FreteFaixasJson",
                table: "storefront",
                type: "text",
                nullable: true);

            migrationBuilder.AddColumn<double>(
                name: "FreteFatorRota",
                table: "storefront",
                type: "double precision",
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "FreteRaioMaxMetros",
                table: "storefront",
                type: "integer",
                nullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "CozinhaLat",
                table: "storefront");

            migrationBuilder.DropColumn(
                name: "CozinhaLng",
                table: "storefront");

            migrationBuilder.DropColumn(
                name: "FreteFaixaGratisMetros",
                table: "storefront");

            migrationBuilder.DropColumn(
                name: "FreteFaixasJson",
                table: "storefront");

            migrationBuilder.DropColumn(
                name: "FreteFatorRota",
                table: "storefront");

            migrationBuilder.DropColumn(
                name: "FreteRaioMaxMetros",
                table: "storefront");
        }
    }
}
