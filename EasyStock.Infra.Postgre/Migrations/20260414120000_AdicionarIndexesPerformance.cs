using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace EasyStock.Infra.Postgre.Migrations
{
    /// <inheritdoc />
    public partial class AdicionarIndexesPerformance : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            // Índice para queries de KPI/filtro por Tipo em movimentacoes_estoque
            migrationBuilder.Sql(
                "CREATE INDEX IF NOT EXISTS idx_movimentacoes_empresa_tipo_data " +
                "ON movimentacoes_estoque (\"EmpresaId\", \"Tipo\", \"DataMovimentacao\");");

            // Índice para queries por Natureza (Venda/Perda)
            migrationBuilder.Sql(
                "CREATE INDEX IF NOT EXISTS idx_movimentacoes_empresa_natureza " +
                "ON movimentacoes_estoque (\"EmpresaId\", \"Natureza\");");

            // Índice parcial para saídas — padrão mais comum nas queries de analytics
            migrationBuilder.Sql(
                "CREATE INDEX IF NOT EXISTS idx_movimentacoes_saidas_data " +
                "ON movimentacoes_estoque (\"EmpresaId\", \"DataMovimentacao\") " +
                "WHERE \"Tipo\" = 'Saida';");

            // Índice para lookup de usuário por email (já existe como unique, mas garantir)
            migrationBuilder.Sql(
                "CREATE UNIQUE INDEX IF NOT EXISTS ix_usuarios_email " +
                "ON usuarios (\"Email\");");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql("DROP INDEX IF EXISTS idx_movimentacoes_empresa_tipo_data;");
            migrationBuilder.Sql("DROP INDEX IF EXISTS idx_movimentacoes_empresa_natureza;");
            migrationBuilder.Sql("DROP INDEX IF EXISTS idx_movimentacoes_saidas_data;");
            migrationBuilder.Sql("DROP INDEX IF EXISTS ix_usuarios_email;");
        }
    }
}
