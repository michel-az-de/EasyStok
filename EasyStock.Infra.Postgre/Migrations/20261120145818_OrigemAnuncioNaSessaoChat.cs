using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace EasyStock.Infra.Postgre.Migrations
{
    /// <inheritdoc />
    public partial class OrigemAnuncioNaSessaoChat : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "OrigemAnuncio_Campanha",
                table: "sessoes_chat_site",
                type: "character varying(120)",
                maxLength: 120,
                nullable: true);

            migrationBuilder.AddColumn<DateTimeOffset>(
                name: "OrigemAnuncio_CapturadaEm",
                table: "sessoes_chat_site",
                type: "timestamp with time zone",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "OrigemAnuncio_Conteudo",
                table: "sessoes_chat_site",
                type: "character varying(120)",
                maxLength: 120,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "OrigemAnuncio_Fonte",
                table: "sessoes_chat_site",
                type: "character varying(120)",
                maxLength: 120,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "OrigemAnuncio_IdAnuncio",
                table: "sessoes_chat_site",
                type: "character varying(60)",
                maxLength: 60,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "OrigemAnuncio_IdClique",
                table: "sessoes_chat_site",
                type: "character varying(200)",
                maxLength: 200,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "OrigemAnuncio_Meio",
                table: "sessoes_chat_site",
                type: "character varying(120)",
                maxLength: 120,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "OrigemAnuncio_Plataforma",
                table: "sessoes_chat_site",
                type: "character varying(20)",
                maxLength: 20,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "OrigemAnuncio_Termo",
                table: "sessoes_chat_site",
                type: "character varying(120)",
                maxLength: 120,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "OrigemAnuncio_TipoIdClique",
                table: "sessoes_chat_site",
                type: "character varying(10)",
                maxLength: 10,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "OrigemAnuncio_TituloAnuncio",
                table: "sessoes_chat_site",
                type: "character varying(120)",
                maxLength: 120,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "OrigemAnuncio_Via",
                table: "sessoes_chat_site",
                type: "character varying(20)",
                maxLength: 20,
                nullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "OrigemAnuncio_Campanha",
                table: "sessoes_chat_site");

            migrationBuilder.DropColumn(
                name: "OrigemAnuncio_CapturadaEm",
                table: "sessoes_chat_site");

            migrationBuilder.DropColumn(
                name: "OrigemAnuncio_Conteudo",
                table: "sessoes_chat_site");

            migrationBuilder.DropColumn(
                name: "OrigemAnuncio_Fonte",
                table: "sessoes_chat_site");

            migrationBuilder.DropColumn(
                name: "OrigemAnuncio_IdAnuncio",
                table: "sessoes_chat_site");

            migrationBuilder.DropColumn(
                name: "OrigemAnuncio_IdClique",
                table: "sessoes_chat_site");

            migrationBuilder.DropColumn(
                name: "OrigemAnuncio_Meio",
                table: "sessoes_chat_site");

            migrationBuilder.DropColumn(
                name: "OrigemAnuncio_Plataforma",
                table: "sessoes_chat_site");

            migrationBuilder.DropColumn(
                name: "OrigemAnuncio_Termo",
                table: "sessoes_chat_site");

            migrationBuilder.DropColumn(
                name: "OrigemAnuncio_TipoIdClique",
                table: "sessoes_chat_site");

            migrationBuilder.DropColumn(
                name: "OrigemAnuncio_TituloAnuncio",
                table: "sessoes_chat_site");

            migrationBuilder.DropColumn(
                name: "OrigemAnuncio_Via",
                table: "sessoes_chat_site");
        }
    }
}
