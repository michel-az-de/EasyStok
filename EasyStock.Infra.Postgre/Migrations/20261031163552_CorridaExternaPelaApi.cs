using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace EasyStock.Infra.Postgre.Migrations
{
    /// <inheritdoc />
    public partial class CorridaExternaPelaApi : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "CotacaoId",
                table: "corridas_externas",
                type: "character varying(64)",
                maxLength: 64,
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "CustoCotadoCentavos",
                table: "corridas_externas",
                type: "integer",
                nullable: true);

            migrationBuilder.AddColumn<DateTime>(
                name: "EncerradaEm",
                table: "corridas_externas",
                type: "timestamp with time zone",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "IdExterno",
                table: "corridas_externas",
                type: "character varying(64)",
                maxLength: 64,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "IdExternoAnterior",
                table: "corridas_externas",
                type: "character varying(64)",
                maxLength: 64,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "LinkRastreio",
                table: "corridas_externas",
                type: "character varying(500)",
                maxLength: 500,
                nullable: true);

            migrationBuilder.AddColumn<DateTime>(
                name: "MotoristaAtribuidoEm",
                table: "corridas_externas",
                type: "timestamp with time zone",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "MotoristaIdExterno",
                table: "corridas_externas",
                type: "character varying(64)",
                maxLength: 64,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "MotoristaPlaca",
                table: "corridas_externas",
                type: "character varying(20)",
                maxLength: 20,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "MotoristaTelefone",
                table: "corridas_externas",
                type: "character varying(30)",
                maxLength: 30,
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "Status",
                table: "corridas_externas",
                type: "integer",
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "Veiculo",
                table: "corridas_externas",
                type: "integer",
                nullable: true);

            migrationBuilder.CreateIndex(
                name: "ux_corridas_externas_provedor_id_externo",
                table: "corridas_externas",
                columns: new[] { "Provedor", "IdExterno" },
                unique: true,
                filter: "\"IdExterno\" IS NOT NULL");

            migrationBuilder.CreateIndex(
                name: "ux_corridas_externas_viagem_ativa",
                table: "corridas_externas",
                column: "ViagemId",
                unique: true,
                filter: "\"Status\" IN (1, 2, 3, 4)");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "ux_corridas_externas_provedor_id_externo",
                table: "corridas_externas");

            migrationBuilder.DropIndex(
                name: "ux_corridas_externas_viagem_ativa",
                table: "corridas_externas");

            migrationBuilder.DropColumn(
                name: "CotacaoId",
                table: "corridas_externas");

            migrationBuilder.DropColumn(
                name: "CustoCotadoCentavos",
                table: "corridas_externas");

            migrationBuilder.DropColumn(
                name: "EncerradaEm",
                table: "corridas_externas");

            migrationBuilder.DropColumn(
                name: "IdExterno",
                table: "corridas_externas");

            migrationBuilder.DropColumn(
                name: "IdExternoAnterior",
                table: "corridas_externas");

            migrationBuilder.DropColumn(
                name: "LinkRastreio",
                table: "corridas_externas");

            migrationBuilder.DropColumn(
                name: "MotoristaAtribuidoEm",
                table: "corridas_externas");

            migrationBuilder.DropColumn(
                name: "MotoristaIdExterno",
                table: "corridas_externas");

            migrationBuilder.DropColumn(
                name: "MotoristaPlaca",
                table: "corridas_externas");

            migrationBuilder.DropColumn(
                name: "MotoristaTelefone",
                table: "corridas_externas");

            migrationBuilder.DropColumn(
                name: "Status",
                table: "corridas_externas");

            migrationBuilder.DropColumn(
                name: "Veiculo",
                table: "corridas_externas");
        }
    }
}
