using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace EasyStock.Infra.Postgre.Migrations
{
    /// <inheritdoc />
    public partial class ProdutoUniqueNomePorEmpresa : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            // RLS está ativa em "produtos" desde AddRowLevelSecurity.
            // O CTE de dedup e o CREATE UNIQUE INDEX precisam ver TODAS as linhas —
            // desabilitar RLS na tabela (o owner pode fazer isso sem BYPASSRLS),
            // operar, e reabilitar logo depois, ainda dentro da mesma transação.
            migrationBuilder.Sql("""
                ALTER TABLE "produtos" DISABLE ROW LEVEL SECURITY;

                WITH ranked AS (
                    SELECT "Id",
                           "EmpresaId",
                           "Nome",
                           ROW_NUMBER() OVER (PARTITION BY "EmpresaId", "Nome" ORDER BY "Id") AS rn
                    FROM "produtos"
                )
                UPDATE "produtos" p
                SET "Nome" = p."Nome" || ' (dup-' || (ranked.rn - 1)::text || ')'
                FROM ranked
                WHERE ranked."Id" = p."Id"
                  AND ranked.rn > 1;

                CREATE UNIQUE INDEX IF NOT EXISTS "IX_Produtos_EmpresaId_Nome"
                    ON "produtos" ("EmpresaId", "Nome");

                ALTER TABLE "produtos" ENABLE ROW LEVEL SECURITY;
                ALTER TABLE "produtos" FORCE ROW LEVEL SECURITY;
                """);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_Produtos_EmpresaId_Nome",
                table: "produtos");
        }
    }
}
