using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace EasyStock.Infra.Postgre.Migrations
{
    /// <inheritdoc />
    public partial class BackfillVelocidadeSaidaDiaria : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            // Recalcula VelocidadeSaidaDiaria para todos os ItemEstoque com base
            // nas MovimentacoesEstoque (Saida + Venda) dos últimos 30 dias.
            // Resolve o gap histórico antes da integração Pedido→Estoque atualizar a velocidade.
            migrationBuilder.Sql(@"
UPDATE itens_estoque i
SET ""VelocidadeSaidaDiaria"" = sub.vel
FROM (
    SELECT
        m.""ItemEstoqueId"",
        COALESCE(
            SUM(CAST(m.""Quantidade"" AS numeric))
            FILTER (WHERE m.""DataMovimentacao"" >= NOW() AT TIME ZONE 'UTC' - INTERVAL '30 days'),
            0
        ) / 30.0 AS vel
    FROM movimentacoes_estoque m
    WHERE m.""Tipo""    = 'Saida'
      AND m.""Natureza"" = 'Venda'
    GROUP BY m.""ItemEstoqueId""
) sub
WHERE i.""Id"" = sub.""ItemEstoqueId""
  AND sub.vel > 0;
");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql(@"UPDATE itens_estoque SET ""VelocidadeSaidaDiaria"" = 0;");
        }
    }
}
