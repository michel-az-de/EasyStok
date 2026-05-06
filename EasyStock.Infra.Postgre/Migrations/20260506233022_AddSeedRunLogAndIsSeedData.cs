using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace EasyStock.Infra.Postgre.Migrations
{
    /// <summary>
    /// Migration escrita manualmente porque a auto-geração saiu vazia
    /// (snapshot avançou sem SQL na primeira tentativa). Aplica:
    ///   1. Coluna IsSeedData (bool, default false) na tabela Empresas — marca
    ///      registros criados pelo seed pra cleanup inteligente.
    ///   2. Tabela SeedRunLogs — auditoria de cada execução de seed.
    ///
    /// Idempotente: usa "IF NOT EXISTS" via raw SQL pra tolerar bancos que
    /// já tenham parte do schema (ex.: rodaram a migration vazia antes).
    /// </summary>
    public partial class AddSeedRunLogAndIsSeedData : Migration
    {
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            // 1. IsSeedData em Empresas
            migrationBuilder.Sql(@"
                ALTER TABLE ""Empresas""
                ADD COLUMN IF NOT EXISTS ""IsSeedData"" boolean NOT NULL DEFAULT false;
            ");

            // 2. Tabela SeedRunLogs
            migrationBuilder.Sql(@"
                CREATE TABLE IF NOT EXISTS ""SeedRunLogs"" (
                    ""Id"" uuid NOT NULL,
                    ""AdminEmail"" text NOT NULL,
                    ""TipoSeed"" text NOT NULL,
                    ""Volume"" text NULL,
                    ""StartedAt"" timestamp with time zone NOT NULL,
                    ""CompletedAt"" timestamp with time zone NULL,
                    ""Status"" text NOT NULL,
                    ""EtapasJson"" text NULL,
                    ""BackupJson"" text NULL,
                    ""Erro"" text NULL,
                    ""Resumo"" text NULL,
                    CONSTRAINT ""PK_SeedRunLogs"" PRIMARY KEY (""Id"")
                );
            ");
        }

        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql(@"DROP TABLE IF EXISTS ""SeedRunLogs"";");
            migrationBuilder.Sql(@"ALTER TABLE ""Empresas"" DROP COLUMN IF EXISTS ""IsSeedData"";");
        }
    }
}
