using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace EasyStock.Infra.Postgre.Migrations
{
    /// <inheritdoc />
    public partial class OrigemDoAnuncio : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "OrigemAnuncio_Campanha",
                table: "pedidos",
                type: "character varying(120)",
                maxLength: 120,
                nullable: true);

            migrationBuilder.AddColumn<DateTimeOffset>(
                name: "OrigemAnuncio_CapturadaEm",
                table: "pedidos",
                type: "timestamp with time zone",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "OrigemAnuncio_Conteudo",
                table: "pedidos",
                type: "character varying(120)",
                maxLength: 120,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "OrigemAnuncio_Fonte",
                table: "pedidos",
                type: "character varying(120)",
                maxLength: 120,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "OrigemAnuncio_IdAnuncio",
                table: "pedidos",
                type: "character varying(60)",
                maxLength: 60,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "OrigemAnuncio_IdClique",
                table: "pedidos",
                type: "character varying(200)",
                maxLength: 200,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "OrigemAnuncio_Meio",
                table: "pedidos",
                type: "character varying(120)",
                maxLength: 120,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "OrigemAnuncio_Plataforma",
                table: "pedidos",
                type: "character varying(20)",
                maxLength: 20,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "OrigemAnuncio_Termo",
                table: "pedidos",
                type: "character varying(120)",
                maxLength: 120,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "OrigemAnuncio_TipoIdClique",
                table: "pedidos",
                type: "character varying(10)",
                maxLength: 10,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "OrigemAnuncio_TituloAnuncio",
                table: "pedidos",
                type: "character varying(120)",
                maxLength: 120,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "OrigemAnuncio_Via",
                table: "pedidos",
                type: "character varying(20)",
                maxLength: 20,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "OrigemAnuncio_Campanha",
                table: "atendimento_conversas",
                type: "character varying(120)",
                maxLength: 120,
                nullable: true);

            migrationBuilder.AddColumn<DateTimeOffset>(
                name: "OrigemAnuncio_CapturadaEm",
                table: "atendimento_conversas",
                type: "timestamp with time zone",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "OrigemAnuncio_Conteudo",
                table: "atendimento_conversas",
                type: "character varying(120)",
                maxLength: 120,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "OrigemAnuncio_Fonte",
                table: "atendimento_conversas",
                type: "character varying(120)",
                maxLength: 120,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "OrigemAnuncio_IdAnuncio",
                table: "atendimento_conversas",
                type: "character varying(60)",
                maxLength: 60,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "OrigemAnuncio_IdClique",
                table: "atendimento_conversas",
                type: "character varying(200)",
                maxLength: 200,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "OrigemAnuncio_Meio",
                table: "atendimento_conversas",
                type: "character varying(120)",
                maxLength: 120,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "OrigemAnuncio_Plataforma",
                table: "atendimento_conversas",
                type: "character varying(20)",
                maxLength: 20,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "OrigemAnuncio_Termo",
                table: "atendimento_conversas",
                type: "character varying(120)",
                maxLength: 120,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "OrigemAnuncio_TipoIdClique",
                table: "atendimento_conversas",
                type: "character varying(10)",
                maxLength: 10,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "OrigemAnuncio_TituloAnuncio",
                table: "atendimento_conversas",
                type: "character varying(120)",
                maxLength: 120,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "OrigemAnuncio_Via",
                table: "atendimento_conversas",
                type: "character varying(20)",
                maxLength: 20,
                nullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "OrigemAnuncio_Campanha",
                table: "pedidos");

            migrationBuilder.DropColumn(
                name: "OrigemAnuncio_CapturadaEm",
                table: "pedidos");

            migrationBuilder.DropColumn(
                name: "OrigemAnuncio_Conteudo",
                table: "pedidos");

            migrationBuilder.DropColumn(
                name: "OrigemAnuncio_Fonte",
                table: "pedidos");

            migrationBuilder.DropColumn(
                name: "OrigemAnuncio_IdAnuncio",
                table: "pedidos");

            migrationBuilder.DropColumn(
                name: "OrigemAnuncio_IdClique",
                table: "pedidos");

            migrationBuilder.DropColumn(
                name: "OrigemAnuncio_Meio",
                table: "pedidos");

            migrationBuilder.DropColumn(
                name: "OrigemAnuncio_Plataforma",
                table: "pedidos");

            migrationBuilder.DropColumn(
                name: "OrigemAnuncio_Termo",
                table: "pedidos");

            migrationBuilder.DropColumn(
                name: "OrigemAnuncio_TipoIdClique",
                table: "pedidos");

            migrationBuilder.DropColumn(
                name: "OrigemAnuncio_TituloAnuncio",
                table: "pedidos");

            migrationBuilder.DropColumn(
                name: "OrigemAnuncio_Via",
                table: "pedidos");

            migrationBuilder.DropColumn(
                name: "OrigemAnuncio_Campanha",
                table: "atendimento_conversas");

            migrationBuilder.DropColumn(
                name: "OrigemAnuncio_CapturadaEm",
                table: "atendimento_conversas");

            migrationBuilder.DropColumn(
                name: "OrigemAnuncio_Conteudo",
                table: "atendimento_conversas");

            migrationBuilder.DropColumn(
                name: "OrigemAnuncio_Fonte",
                table: "atendimento_conversas");

            migrationBuilder.DropColumn(
                name: "OrigemAnuncio_IdAnuncio",
                table: "atendimento_conversas");

            migrationBuilder.DropColumn(
                name: "OrigemAnuncio_IdClique",
                table: "atendimento_conversas");

            migrationBuilder.DropColumn(
                name: "OrigemAnuncio_Meio",
                table: "atendimento_conversas");

            migrationBuilder.DropColumn(
                name: "OrigemAnuncio_Plataforma",
                table: "atendimento_conversas");

            migrationBuilder.DropColumn(
                name: "OrigemAnuncio_Termo",
                table: "atendimento_conversas");

            migrationBuilder.DropColumn(
                name: "OrigemAnuncio_TipoIdClique",
                table: "atendimento_conversas");

            migrationBuilder.DropColumn(
                name: "OrigemAnuncio_TituloAnuncio",
                table: "atendimento_conversas");

            migrationBuilder.DropColumn(
                name: "OrigemAnuncio_Via",
                table: "atendimento_conversas");
        }
    }
}
