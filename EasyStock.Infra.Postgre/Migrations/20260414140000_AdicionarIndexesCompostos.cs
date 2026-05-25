using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace EasyStock.Infra.Postgre.Migrations
{
    /// <inheritdoc />
    public partial class AdicionarIndexesCompostos : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            // Índice composto para queries de estoque filtradas por loja
            // Cobre: GetEstoqueBaixoAsync, GetProximoVencimentoAsync, GetItensParadosAsync, GetSugestaoReposicaoAsync com LojaId
            migrationBuilder.Sql(
                "CREATE INDEX IF NOT EXISTS ix_itens_estoque_empresa_loja " +
                "ON itens_estoque (\"EmpresaId\", \"LojaId\") WHERE \"LojaId\" IS NOT NULL;");

            // Índice composto para paginação de audit_log por usuário com ordenação por data
            // Cobre: GetByUsuarioIdAsync(usuarioId, page, pageSize) com OrderByDescending(DataHora)
            migrationBuilder.Sql(
                "CREATE INDEX IF NOT EXISTS ix_audit_logs_usuario_data " +
                "ON audit_logs (\"UsuarioId\", \"DataHora\" DESC);");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql("DROP INDEX IF EXISTS ix_itens_estoque_empresa_loja;");
            migrationBuilder.Sql("DROP INDEX IF EXISTS ix_audit_logs_usuario_data;");
        }
    }
}
