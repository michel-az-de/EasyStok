using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace EasyStock.Infra.Postgre.Migrations
{
    /// <summary>
    /// S68 (#2597): configuração do Assistente por loja (nome da dona, modelo, cota, roteiro e observações), tudo
    /// anulável: nulo é o comportamento de antes. Não troca dado: a escalada padrão cita a dona em código, e um rollback
    /// só de código segue lendo o banco sem surpresa.
    /// </summary>
    public partial class AssistentePorLoja : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<long>(
                name: "CotaTokensMes",
                table: "configuracoes_atendimento",
                type: "bigint",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "ModeloAgente",
                table: "configuracoes_atendimento",
                type: "character varying(200)",
                maxLength: 200,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "NomeDona",
                table: "configuracoes_atendimento",
                type: "character varying(60)",
                maxLength: 60,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "ObservacoesLoja",
                table: "configuracoes_atendimento",
                type: "character varying(500)",
                maxLength: 500,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "RoteiroJson",
                table: "configuracoes_atendimento",
                type: "jsonb",
                nullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "CotaTokensMes",
                table: "configuracoes_atendimento");

            migrationBuilder.DropColumn(
                name: "ModeloAgente",
                table: "configuracoes_atendimento");

            migrationBuilder.DropColumn(
                name: "NomeDona",
                table: "configuracoes_atendimento");

            migrationBuilder.DropColumn(
                name: "ObservacoesLoja",
                table: "configuracoes_atendimento");

            migrationBuilder.DropColumn(
                name: "RoteiroJson",
                table: "configuracoes_atendimento");

        }
    }
}
