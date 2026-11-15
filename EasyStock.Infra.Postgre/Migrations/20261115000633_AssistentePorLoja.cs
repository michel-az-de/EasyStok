using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace EasyStock.Infra.Postgre.Migrations
{
    /// <summary>
    /// S68 (#2597): configuração do Assistente por loja (nome da dona, modelo, cota, roteiro e observações), tudo
    /// anulável: nulo é o comportamento de antes. A escalada padrão passa a citar {dona}; a linha que ainda tem
    /// exatamente o texto padrão antigo ganha o novo, que sem nome configurado sai igual ("a equipe"). O texto que a
    /// loja escreveu fica como está.
    /// </summary>
    public partial class AssistentePorLoja : Migration
    {
        private const string EscaladaAntiga = "Vou encaminhar sua mensagem para a equipe conferir e responder por aqui.";
        private const string EscaladaNova = "Vou encaminhar sua mensagem para {dona} conferir e responder por aqui.";

        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AlterColumn<string>(
                name: "MensagemEscalada",
                table: "configuracoes_atendimento",
                type: "character varying(500)",
                maxLength: 500,
                nullable: false,
                defaultValue: "Vou encaminhar sua mensagem para {dona} conferir e responder por aqui.",
                oldClrType: typeof(string),
                oldType: "character varying(500)",
                oldMaxLength: 500,
                oldDefaultValue: "Vou encaminhar sua mensagem para a equipe conferir e responder por aqui.");

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

            TrocarEscalada(migrationBuilder, EscaladaAntiga, EscaladaNova);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            TrocarEscalada(migrationBuilder, EscaladaNova, EscaladaAntiga);

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

            migrationBuilder.AlterColumn<string>(
                name: "MensagemEscalada",
                table: "configuracoes_atendimento",
                type: "character varying(500)",
                maxLength: 500,
                nullable: false,
                defaultValue: "Vou encaminhar sua mensagem para a equipe conferir e responder por aqui.",
                oldClrType: typeof(string),
                oldType: "character varying(500)",
                oldMaxLength: 500,
                oldDefaultValue: "Vou encaminhar sua mensagem para {dona} conferir e responder por aqui.");
        }

        private static void TrocarEscalada(MigrationBuilder migrationBuilder, string de, string para) =>
            migrationBuilder.Sql(
                $"UPDATE configuracoes_atendimento SET \"MensagemEscalada\" = '{para}' WHERE \"MensagemEscalada\" = '{de}';");
    }
}
