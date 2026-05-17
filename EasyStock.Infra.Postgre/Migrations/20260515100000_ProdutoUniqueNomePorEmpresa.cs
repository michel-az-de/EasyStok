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
            // Renomeia duplicatas exatas antes de criar o índice único.
            // Mantém o registro com Id mais baixo como canônico; os outros recebem sufixo " (dup-N)".
            migrationBuilder.Sql("""
                WITH ranked AS (
                    SELECT "Id",
                           "EmpresaId",
                           "Nome",
                           ROW_NUMBER() OVER (PARTITION BY "EmpresaId", "Nome" ORDER BY "Id") AS rn
                    FROM "Produtos"
                )
                UPDATE "Produtos" p
                SET "Nome" = p."Nome" || ' (dup-' || (ranked.rn - 1)::text || ')'
                FROM ranked
                WHERE ranked."Id" = p."Id"
                  AND ranked.rn > 1;
                """);

            migrationBuilder.CreateIndex(
                name: "IX_Produtos_EmpresaId_Nome",
                table: "Produtos",
                columns: new[] { "EmpresaId", "Nome" },
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_Produtos_EmpresaId_Nome",
                table: "Produtos");
        }
    }
}
