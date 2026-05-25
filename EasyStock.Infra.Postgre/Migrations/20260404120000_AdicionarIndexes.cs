using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace EasyStock.Infra.Postgre.Migrations
{
    /// <inheritdoc />
    public partial class AdicionarIndexes : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql(
                "CREATE INDEX IF NOT EXISTS idx_itens_estoque_empresa_qtd ON itens_estoque (\"EmpresaId\", \"QuantidadeAtual\");");

            migrationBuilder.Sql(
                "CREATE INDEX IF NOT EXISTS idx_itens_estoque_empresa_validade ON itens_estoque (\"EmpresaId\", \"ValidadeEm\");");

            migrationBuilder.Sql(
                "CREATE INDEX IF NOT EXISTS idx_itens_estoque_empresa_ultima_mov ON itens_estoque (\"EmpresaId\", \"UltimaMovimentacaoEm\");");

            migrationBuilder.Sql(
                "CREATE INDEX IF NOT EXISTS idx_produtos_empresa_nome ON produtos (\"EmpresaId\", \"Nome\");");

            migrationBuilder.Sql(
                "CREATE INDEX IF NOT EXISTS idx_movimentacoes_empresa_produto ON movimentacoes_estoque (\"EmpresaId\", \"ProdutoId\", \"DataMovimentacao\");");

            migrationBuilder.Sql(
                "CREATE INDEX IF NOT EXISTS idx_vendas_empresa_data ON vendas (\"EmpresaId\", \"DataVenda\");");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql("DROP INDEX IF EXISTS idx_itens_estoque_empresa_qtd;");
            migrationBuilder.Sql("DROP INDEX IF EXISTS idx_itens_estoque_empresa_validade;");
            migrationBuilder.Sql("DROP INDEX IF EXISTS idx_itens_estoque_empresa_ultima_mov;");
            migrationBuilder.Sql("DROP INDEX IF EXISTS idx_produtos_empresa_nome;");
            migrationBuilder.Sql("DROP INDEX IF EXISTS idx_movimentacoes_empresa_produto;");
            migrationBuilder.Sql("DROP INDEX IF EXISTS idx_vendas_empresa_data;");
        }
    }
}
