using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace EasyStock.Infra.Postgre.Migrations
{
    /// <summary>
    /// NO-OP. Migration original tentava ALTER TABLE "Empresas" (PascalCase) em
    /// um schema que estava com naming snake_case ("empresas") — falha hard
    /// (42P01: relation does not exist) em bancos fresh.
    ///
    /// O trabalho real (coluna IsSeedData em empresas + tabela SeedRunLogs) foi
    /// movido para a migration AddNotificationsCore (anterior, 22:15:16) com
    /// SQL idempotente em snake_case. Em bancos antigos que ja aplicaram esta
    /// migration via legado, o registro permanece em __EFMigrationsHistory e
    /// nada e' desfeito.
    /// </summary>
    public partial class AddSeedRunLogAndIsSeedData : Migration
    {
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            // Intencionalmente vazio. Veja XML doc da classe.
        }

        protected override void Down(MigrationBuilder migrationBuilder)
        {
            // Intencionalmente vazio. Veja XML doc da classe.
        }
    }
}
