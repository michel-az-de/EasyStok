using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace EasyStock.Infra.Postgre.Migrations
{
    /// <inheritdoc />
    public partial class AddRetomadaNaoEntreguesSms : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "ModeloRetomadaIdioma",
                table: "configuracoes_atendimento",
                type: "character varying(16)",
                maxLength: 16,
                nullable: false,
                defaultValue: "pt_BR");

            migrationBuilder.AddColumn<string>(
                name: "ModeloRetomadaNome",
                table: "configuracoes_atendimento",
                type: "character varying(512)",
                maxLength: 512,
                nullable: true);

            migrationBuilder.AddColumn<DateTime>(
                name: "AguardaClienteDesde",
                table: "atendimento_mensagens",
                type: "timestamp with time zone",
                nullable: true);

            migrationBuilder.AddColumn<DateTime>(
                name: "ReservaSmsEm",
                table: "atendimento_mensagens",
                type: "timestamp with time zone",
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "UltimaFalhaEnvio",
                table: "atendimento_mensagens",
                type: "integer",
                nullable: true);

            migrationBuilder.CreateIndex(
                name: "ix_atendimento_mensagens_aguarda_cliente",
                table: "atendimento_mensagens",
                column: "AguardaClienteDesde",
                filter: "\"AguardaClienteDesde\" IS NOT NULL");

            migrationBuilder.CreateIndex(
                name: "ix_atendimento_mensagens_nao_entregues",
                table: "atendimento_mensagens",
                columns: new[] { "EmpresaId", "EnviadaEm" },
                filter: "\"Status\" = 5 AND \"Direcao\" = 2");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "ix_atendimento_mensagens_aguarda_cliente",
                table: "atendimento_mensagens");

            migrationBuilder.DropIndex(
                name: "ix_atendimento_mensagens_nao_entregues",
                table: "atendimento_mensagens");

            migrationBuilder.DropColumn(
                name: "ModeloRetomadaIdioma",
                table: "configuracoes_atendimento");

            migrationBuilder.DropColumn(
                name: "ModeloRetomadaNome",
                table: "configuracoes_atendimento");

            migrationBuilder.DropColumn(
                name: "AguardaClienteDesde",
                table: "atendimento_mensagens");

            migrationBuilder.DropColumn(
                name: "ReservaSmsEm",
                table: "atendimento_mensagens");

            migrationBuilder.DropColumn(
                name: "UltimaFalhaEnvio",
                table: "atendimento_mensagens");
        }
    }
}
