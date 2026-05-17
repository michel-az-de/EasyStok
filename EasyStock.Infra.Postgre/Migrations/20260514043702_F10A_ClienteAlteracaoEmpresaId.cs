using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace EasyStock.Infra.Postgre.Migrations
{
    /// <summary>
    /// F10-A — adiciona EmpresaId em cliente_alteracoes pra que o Global Query
    /// Filter aplique tenant isolation (antes a tabela era cross-tenant porque
    /// sem EmpresaId o ApplyTenantQueryFilters skipava).
    ///
    /// Backfill via JOIN com clientes pra herdar EmpresaId do dono. Coluna
    /// entra nullable primeiro, popula, ALTER NOT NULL.
    ///
    /// Index composto (EmpresaId, ClienteId, AlteradoEm) substitui o antigo
    /// (ClienteId, AlteradoEm).
    /// </summary>
    public partial class F10A_ClienteAlteracaoEmpresaId : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            // 1. Drop do index antigo
            migrationBuilder.Sql(@"DROP INDEX IF EXISTS ""IX_cliente_alteracoes_ClienteId_AlteradoEm"";");

            // 2. Adiciona coluna NULL (sem default — entradas existentes ficam NULL).
            migrationBuilder.Sql(@"
                ALTER TABLE cliente_alteracoes
                ADD COLUMN IF NOT EXISTS ""EmpresaId"" uuid;
            ");

            // 3. Backfill: copia EmpresaId do Cliente dono.
            migrationBuilder.Sql(@"
                UPDATE cliente_alteracoes ca
                SET ""EmpresaId"" = c.""EmpresaId""
                FROM clientes c
                WHERE c.""Id"" = ca.""ClienteId""
                  AND ca.""EmpresaId"" IS NULL;
            ");

            // 4. Garante NOT NULL agora que está populado.
            migrationBuilder.Sql(@"
                ALTER TABLE cliente_alteracoes
                ALTER COLUMN ""EmpresaId"" SET NOT NULL;
            ");

            // 5. Index composto (EmpresaId, ClienteId, AlteradoEm).
            migrationBuilder.Sql(@"
                CREATE INDEX IF NOT EXISTS ""IX_cliente_alteracoes_EmpresaId_ClienteId_AlteradoEm""
                ON cliente_alteracoes (""EmpresaId"", ""ClienteId"", ""AlteradoEm"");
            ");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql(@"DROP INDEX IF EXISTS ""IX_cliente_alteracoes_EmpresaId_ClienteId_AlteradoEm"";");
            migrationBuilder.Sql(@"ALTER TABLE cliente_alteracoes DROP COLUMN IF EXISTS ""EmpresaId"";");
            migrationBuilder.Sql(@"
                CREATE INDEX IF NOT EXISTS ""IX_cliente_alteracoes_ClienteId_AlteradoEm""
                ON cliente_alteracoes (""ClienteId"", ""AlteradoEm"");
            ");
        }
    }
}
