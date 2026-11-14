using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace EasyStock.Infra.Postgre.Migrations
{
    /// <summary>
    /// #2544 (S71, D1): a saudação do primeiro contato e a de retorno saem sem o link do cardápio, porque o Assistente conduz
    /// e oferece a comanda só a quem quer escolher sozinho. Só dados: troca o texto apenas onde ele é exatamente o padrão
    /// antigo; o texto que a loja escreveu fica como está.
    /// </summary>
    public partial class SaudacaoSemLinkDoCardapio : Migration
    {
        private const string PrimeiroAntigo = "Oi! Seja bem-vindo(a) à Casa da Baba 🍝 Dá uma olhada no nosso cardápio: {link}";
        private const string PrimeiroNovo = "Oi! Seja bem-vindo(a) à Casa da Baba 🍝";
        private const string RetornoAntigo = "Oi, {nome}! Que bom te ver de novo por aqui. Segue nosso cardápio: {link}";
        private const string RetornoNovo = "Oi, {nome}! Que bom te ver de novo por aqui.";

        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            Trocar(migrationBuilder, "SaudacaoPrimeiroContato", PrimeiroAntigo, PrimeiroNovo);
            Trocar(migrationBuilder, "SaudacaoRetorno", RetornoAntigo, RetornoNovo);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            Trocar(migrationBuilder, "SaudacaoPrimeiroContato", PrimeiroNovo, PrimeiroAntigo);
            Trocar(migrationBuilder, "SaudacaoRetorno", RetornoNovo, RetornoAntigo);
        }

        private static void Trocar(MigrationBuilder migrationBuilder, string coluna, string de, string para) =>
            migrationBuilder.Sql(
                $"UPDATE configuracoes_atendimento SET \"{coluna}\" = '{para}' WHERE \"{coluna}\" = '{de}';");
    }
}
