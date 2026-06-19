using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace EasyStock.Infra.Postgre.Migrations
{
    /// <inheritdoc />
    public partial class AddCardapioVariacaoRotuloUnicoDeferrable : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            // ADR-0035 / #647 (follow-up do eb98e22e): unicidade case-insensitive de Rotulo
            // por CardapioItem. Coluna gerada rotulo_lower + UNIQUE DEFERRABLE — NAO indice de
            // expressao/parcial, porque esses NAO sao DEFERRABLE no Postgres. A reconciliacao
            // keyed-by-Id do admin (F7/#652) troca rotulos ("P"<->"G") numa unica transacao;
            // DEFERRABLE INITIALLY DEFERRED adia a checagem para o COMMIT e evita colisao
            // transiente no UPDATE in-place. Tabela recem-criada (eb98e22e) e vazia -> operacao
            // transacional, sem CONCURRENTLY. DROP+ADD = idempotente (PG nao tem ADD CONSTRAINT IF NOT EXISTS).
            migrationBuilder.Sql(@"
                ALTER TABLE cardapio_item_variacao
                    ADD COLUMN IF NOT EXISTS rotulo_lower text
                    GENERATED ALWAYS AS (lower(""Rotulo"")) STORED;

                ALTER TABLE cardapio_item_variacao
                    DROP CONSTRAINT IF EXISTS uq_cardapio_item_variacao_rotulo;

                ALTER TABLE cardapio_item_variacao
                    ADD CONSTRAINT uq_cardapio_item_variacao_rotulo
                    UNIQUE (""CardapioItemId"", rotulo_lower) DEFERRABLE INITIALLY DEFERRED;");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql(@"
                ALTER TABLE cardapio_item_variacao
                    DROP CONSTRAINT IF EXISTS uq_cardapio_item_variacao_rotulo;

                ALTER TABLE cardapio_item_variacao
                    DROP COLUMN IF EXISTS rotulo_lower;");
        }
    }
}
