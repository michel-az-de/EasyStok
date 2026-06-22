using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace EasyStock.Infra.Postgre.Migrations
{
    /// <inheritdoc />
    public partial class AddUniqueCategoriaNomeInsensivelPorEmpresa : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            // QA v1.10 BUG-11: unicidade case-insensitive de Nome por empresa — backstop de banco
            // para o check de aplicacao (GerenciarCategoriaUseCase.ExisteNomeAsync), fechando a
            // janela TOCTOU. Espelha o padrao de cardapio_item_variacao (#647): coluna gerada
            // nome_lower + UNIQUE constraint. A coluna extra e invisivel ao SchemaDriftCheck, que
            // so acusa coluna DO MODELO ausente no banco — nao coluna a mais no banco.
            //
            // Diferenca vs cardapio: categorias JA tem dados, entao deduplicamos as colisoes ANTES
            // da constraint. Mantem a mais antiga (CriadoEm, Id) e renomeia as demais com sufixo
            // baseado no Id (unico por definicao -> a renomeacao NUNCA gera nova colisao, o que
            // travaria o deploy). Nada e apagado: os produtos preservam a CategoriaId.
            // left(...,100) respeita o limite varchar(120) da coluna apos o sufixo.
            migrationBuilder.Sql(@"
                ALTER TABLE categorias
                    ADD COLUMN IF NOT EXISTS nome_lower text
                    GENERATED ALWAYS AS (lower(""Nome"")) STORED;

                UPDATE categorias c
                SET ""Nome"" = left(c.""Nome"", 100) || ' (dup ' || left(c.""Id""::text, 8) || ')'
                FROM (
                    SELECT ""Id"", row_number() OVER (
                        PARTITION BY ""EmpresaId"", lower(""Nome"") ORDER BY ""CriadoEm"", ""Id""
                    ) AS rn
                    FROM categorias
                ) r
                WHERE c.""Id"" = r.""Id"" AND r.rn > 1;

                ALTER TABLE categorias
                    DROP CONSTRAINT IF EXISTS uq_categorias_empresa_nome_lower;

                ALTER TABLE categorias
                    ADD CONSTRAINT uq_categorias_empresa_nome_lower
                    UNIQUE (""EmpresaId"", nome_lower);");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql(@"
                ALTER TABLE categorias
                    DROP CONSTRAINT IF EXISTS uq_categorias_empresa_nome_lower;

                ALTER TABLE categorias
                    DROP COLUMN IF EXISTS nome_lower;");
        }
    }
}
