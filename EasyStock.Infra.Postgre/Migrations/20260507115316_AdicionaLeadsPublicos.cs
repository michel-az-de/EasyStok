using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace EasyStock.Infra.Postgre.Migrations
{
    /// <summary>
    /// NO-OP. Migration duplicada — a tabela leads_publicos (com mesmas colunas
    /// e indices) ja foi criada pela migration Onda2_HasMaxLengthSweep
    /// (20260507025613, anterior por timestamp). Em banco fresh, esta tentava
    /// CREATE TABLE de novo e falhava com 42P07 (relation already exists),
    /// abortando todas migrations posteriores.
    ///
    /// Bancos antigos onde esta ja foi aplicada antes da Onda2 nao sao afetados:
    /// o registro permanece em __EFMigrationsHistory e nada e' desfeito.
    /// </summary>
    public partial class AdicionaLeadsPublicos : Migration
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
