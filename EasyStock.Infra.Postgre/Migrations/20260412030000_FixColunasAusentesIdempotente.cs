using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace EasyStock.Infra.Postgre.Migrations
{
    /// <summary>
    /// Migration de recuperacao: adiciona colunas que podem ter sido perdidas
    /// por falha de sincronizacao do historico de migrations em deploy anterior.
    /// Usa IF NOT EXISTS para ser totalmente idempotente.
    /// </summary>
    public partial class FixColunasAusentesIdempotente : Migration
    {
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            // TemaPreferido pode ter ficado fora por migration registrada sem executar
            migrationBuilder.Sql(@"
                ALTER TABLE usuarios
                ADD COLUMN IF NOT EXISTS ""TemaPreferido"" character varying(20) NOT NULL DEFAULT 'light';
            ");

            // EnriquecerProdutoSubcategoriaECaracteristica
            migrationBuilder.Sql(@"
                ALTER TABLE produtos
                ADD COLUMN IF NOT EXISTS ""SubcategoriaId"" uuid;
            ");
            migrationBuilder.Sql(@"
                ALTER TABLE produto_caracteristicas
                ADD COLUMN IF NOT EXISTS ""VariacaoId"" uuid;
            ");
            migrationBuilder.Sql(@"
                CREATE INDEX IF NOT EXISTS ""IX_produtos_SubcategoriaId""
                    ON produtos (""SubcategoriaId"");
                CREATE INDEX IF NOT EXISTS ""IX_produto_caracteristicas_VariacaoId""
                    ON produto_caracteristicas (""VariacaoId"");
            ");

            // AdicionarEstornoMovimentacao
            migrationBuilder.Sql(@"
                ALTER TABLE movimentacoes_estoque
                ADD COLUMN IF NOT EXISTS ""EstornadaEm"" timestamp with time zone;
                ALTER TABLE movimentacoes_estoque
                ADD COLUMN IF NOT EXISTS ""MovimentacaoEstornadaId"" uuid;
            ");

            // FixNotificacaoSeveridadeDefault
            migrationBuilder.Sql(@"
                ALTER TABLE notificacoes
                ADD COLUMN IF NOT EXISTS ""Severidade"" character varying(20) NOT NULL DEFAULT 'Media';
                ALTER TABLE notificacoes
                ADD COLUMN IF NOT EXISTS ""Titulo"" character varying(120) NOT NULL DEFAULT '';
            ");

            // configuracoes_loja pode nao ter sido criada se migration conflitou
            migrationBuilder.Sql(@"
                CREATE TABLE IF NOT EXISTS configuracoes_loja (
                    ""Id""                      uuid        NOT NULL,
                    ""LojaId""                  uuid        NOT NULL,
                    ""DiasAlertaValidade""       integer     NOT NULL DEFAULT 15,
                    ""DiasAlertaParado""         integer     NOT NULL DEFAULT 30,
                    ""QuantidadeMinimaPadrao""   integer     NOT NULL DEFAULT 5,
                    ""NotificarEstoqueCritico""  boolean     NOT NULL DEFAULT true,
                    ""NotificarValidade""        boolean     NOT NULL DEFAULT true,
                    ""NotificarParado""          boolean     NOT NULL DEFAULT true,
                    ""NotificarReposicao""       boolean     NOT NULL DEFAULT true,
                    ""FifoAtivo""               boolean     NOT NULL DEFAULT true,
                    ""Moeda""                   character varying(10)  NOT NULL DEFAULT 'BRL',
                    ""Timezone""                character varying(100) NOT NULL DEFAULT 'America/Sao_Paulo',
                    ""CriadoEm""               timestamp with time zone NOT NULL,
                    ""AlteradoEm""             timestamp with time zone NOT NULL,
                    CONSTRAINT ""PK_configuracoes_loja"" PRIMARY KEY (""Id""),
                    CONSTRAINT ""FK_configuracoes_loja_lojas_LojaId""
                        FOREIGN KEY (""LojaId"") REFERENCES lojas(""Id"") ON DELETE CASCADE
                );
                CREATE UNIQUE INDEX IF NOT EXISTS ""IX_configuracoes_loja_LojaId""
                    ON configuracoes_loja (""LojaId"");
            ");
        }

        protected override void Down(MigrationBuilder migrationBuilder)
        {
            // Down intencional: nao reverter colunas criadas por IF NOT EXISTS
            // pois podem ter sido criadas por outra migration
        }
    }
}
