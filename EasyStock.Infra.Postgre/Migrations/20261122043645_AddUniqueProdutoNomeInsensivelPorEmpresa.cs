using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace EasyStock.Infra.Postgre.Migrations
{
    /// <inheritdoc />
    public partial class AddUniqueProdutoNomeInsensivelPorEmpresa : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            // #3101: unicidade de Nome por empresa sem diferenciar caixa, como backstop de banco
            // para o check de aplicacao (ProdutoRepository.ExistsNomeAsync, BUG-10). O indice
            // antigo IX_Produtos_EmpresaId_Nome diferencia caixa, entao "Vassoura" e "vassoura"
            // passavam em gravacao simultanea ou por caminho sem o check.
            //
            // Indice de EXPRESSAO, e nao coluna gerada como em categorias e planos: o LINQ do
            // repositorio ja emite lower("Nome") e casa com ele, sem FromSql e sem reescrever a
            // tabela. Fica fora do modelo EF (o snapshot nao muda).
            //
            // Deduplica ANTES do indice, senao a migration trava o deploy. Mantem o mais antigo
            // (CriadoEm, Id) e renomeia os demais com sufixo do Id, unico por definicao: a
            // renomeacao nunca gera nova colisao. Nada e apagado. left(...,160) + 15 de sufixo
            // cabe no varchar(180).
            //
            // RLS: o dedup precisa ver TODAS as empresas. Mesmo procedimento de
            // ProdutoUniqueNomePorEmpresa: desliga, opera e religa com FORCE na mesma transacao.
            migrationBuilder.Sql("""
                ALTER TABLE "produtos" DISABLE ROW LEVEL SECURITY;

                UPDATE "produtos" p
                SET "Nome" = left(p."Nome", 160) || ' (dup ' || left(p."Id"::text, 8) || ')'
                FROM (
                    SELECT "Id", row_number() OVER (
                        PARTITION BY "EmpresaId", lower("Nome") ORDER BY "CriadoEm", "Id"
                    ) AS rn
                    FROM "produtos"
                ) r
                WHERE p."Id" = r."Id" AND r.rn > 1;

                CREATE UNIQUE INDEX IF NOT EXISTS uq_produtos_empresa_nome_lower
                    ON "produtos" ("EmpresaId", lower("Nome"));

                ALTER TABLE "produtos" ENABLE ROW LEVEL SECURITY;
                ALTER TABLE "produtos" FORCE ROW LEVEL SECURITY;
                """);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            // Nao desfaz a deduplicacao: nao ha registro do nome original.
            migrationBuilder.Sql("DROP INDEX IF EXISTS uq_produtos_empresa_nome_lower;");
        }
    }
}
