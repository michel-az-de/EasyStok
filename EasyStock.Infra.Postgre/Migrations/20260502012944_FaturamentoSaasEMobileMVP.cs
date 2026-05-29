using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace EasyStock.Infra.Postgre.Migrations
{
    /// <inheritdoc />
    public partial class FaturamentoSaasEMobileMVP : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "push_token",
                table: "mobile_devices",
                type: "character varying(512)",
                maxLength: 512,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "BoletoCodigo",
                table: "CobrancasAssinatura",
                type: "text",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "BoletoUrl",
                table: "CobrancasAssinatura",
                type: "text",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "MetodoPagamento",
                table: "CobrancasAssinatura",
                type: "text",
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "TentativasLembrete",
                table: "CobrancasAssinatura",
                type: "integer",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.AddColumn<DateTime>(
                name: "UltimoLembreteEm",
                table: "CobrancasAssinatura",
                type: "timestamp with time zone",
                nullable: true);

            migrationBuilder.AddColumn<DateTime>(
                name: "SuspensaEm",
                table: "assinaturas_empresa",
                type: "timestamp with time zone",
                nullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "push_token",
                table: "mobile_devices");

            migrationBuilder.DropColumn(
                name: "BoletoCodigo",
                table: "CobrancasAssinatura");

            migrationBuilder.DropColumn(
                name: "BoletoUrl",
                table: "CobrancasAssinatura");

            migrationBuilder.DropColumn(
                name: "MetodoPagamento",
                table: "CobrancasAssinatura");

            migrationBuilder.DropColumn(
                name: "TentativasLembrete",
                table: "CobrancasAssinatura");

            migrationBuilder.DropColumn(
                name: "UltimoLembreteEm",
                table: "CobrancasAssinatura");

            migrationBuilder.DropColumn(
                name: "SuspensaEm",
                table: "assinaturas_empresa");
        }
    }
}
