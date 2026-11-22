using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace EasyStock.Infra.Postgre.Migrations
{
    /// <inheritdoc />
    public partial class AddClienteEmailLower : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            // #3101: coluna gerada fora do modelo EF (como categorias.nome_lower). Sob RLS o Postgres so usa
            // como Index Cond o que e leakproof e lower() nao e; um indice de expressao nao serviria, e a
            // coluna gerada sim. So DDL: a RLS de clientes nao e desligada nem religada. O ADD COLUMN
            // STORED reescreve a tabela, aceitavel pelo tamanho de clientes. Indice nao unico: e-mail
            // repetido e permitido.
            migrationBuilder.Sql(@"
                ALTER TABLE clientes
                    ADD COLUMN IF NOT EXISTS email_lower text
                    GENERATED ALWAYS AS (lower(""Email"")) STORED;

                CREATE INDEX IF NOT EXISTS ix_clientes_empresa_email_lower
                    ON clientes (""EmpresaId"", email_lower);");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql(@"
                DROP INDEX IF EXISTS ix_clientes_empresa_email_lower;

                ALTER TABLE clientes
                    DROP COLUMN IF EXISTS email_lower;");
        }
    }
}
