using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace EasyStock.Infra.Postgre.Migrations
{
    /// <inheritdoc />
    public partial class RefactorQuantidadeToDecimal : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            // PR-A: Quantidade VO int → decimal(18,3).
            // Conversão USING col::numeric é segura (integer ⊂ numeric) e não perde dados.
            // suppressTransaction: true obrigatório — ALTER TYPE não pode rodar dentro de tx.

            migrationBuilder.Sql(
                @"ALTER TABLE itens_estoque ALTER COLUMN ""QuantidadeInicial"" TYPE numeric(18,3) USING ""QuantidadeInicial""::numeric;",
                suppressTransaction: true);

            migrationBuilder.Sql(
                @"ALTER TABLE itens_estoque ALTER COLUMN ""QuantidadeAtual"" TYPE numeric(18,3) USING ""QuantidadeAtual""::numeric;",
                suppressTransaction: true);

            migrationBuilder.Sql(
                @"ALTER TABLE movimentacoes_estoque ALTER COLUMN ""Quantidade"" TYPE numeric(18,3) USING ""Quantidade""::numeric;",
                suppressTransaction: true);

            migrationBuilder.Sql(
                @"ALTER TABLE itens_venda ALTER COLUMN ""Quantidade"" TYPE numeric(18,3) USING ""Quantidade""::numeric;",
                suppressTransaction: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            // Rollback seguro apenas se não houver valores fracionários gravados.
            // Se houver, a conversão trunca — emitir aviso no runbook antes de reverter.

            migrationBuilder.Sql(
                @"ALTER TABLE itens_venda ALTER COLUMN ""Quantidade"" TYPE integer USING ""Quantidade""::integer;",
                suppressTransaction: true);

            migrationBuilder.Sql(
                @"ALTER TABLE movimentacoes_estoque ALTER COLUMN ""Quantidade"" TYPE integer USING ""Quantidade""::integer;",
                suppressTransaction: true);

            migrationBuilder.Sql(
                @"ALTER TABLE itens_estoque ALTER COLUMN ""QuantidadeAtual"" TYPE integer USING ""QuantidadeAtual""::integer;",
                suppressTransaction: true);

            migrationBuilder.Sql(
                @"ALTER TABLE itens_estoque ALTER COLUMN ""QuantidadeInicial"" TYPE integer USING ""QuantidadeInicial""::integer;",
                suppressTransaction: true);
        }
    }
}
