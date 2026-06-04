using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace EasyStock.Infra.Postgre.Migrations
{
    /// <summary>
    /// #465 — reconcilia 3 migrations que foram commitadas SEM o arquivo
    /// <c>.Designer.cs</c> e por isso ficaram invisíveis ao EF runtime: o
    /// <c>RunMigrationsOnStartup</c> nunca as aplicou em ambiente nenhum (reportava
    /// "0 pendentes"), apesar do snapshot já as refletir. Resultado: drift silencioso
    /// (42703 em runtime) que parecia bug de feature.
    ///
    /// Espelha o DDL das 3 de forma IDEMPOTENTE (IF NOT EXISTS / guardas) para curar
    /// qualquer banco no próximo deploy sem reescrever histórico. As 3 .cs órfãs foram
    /// removidas no mesmo commit. Reconciliadas:
    ///   - 20260414140000_AdicionarIndexesCompostos
    ///   - 20260525200000_AddPedidoStorefrontResolucao
    ///   - 20260528202748_AddNfeF1RepoIndexes
    /// </summary>
    public partial class ReconciliarMigrationsSemDesigner : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            // Tudo num único DO block guardado por to_regclass: como o compose roda
            // MigrationsFailFast=true, uma migration que estoura derruba a API. Se
            // alguma tabela faltar no ambiente, o passo é pulado (não falha) e o
            // detector de drift (#464) loga o que ficou ausente. Tudo idempotente.
            migrationBuilder.Sql(@"
DO $$
BEGIN
  -- (1) 20260414140000_AdicionarIndexesCompostos — 2 índices (só performance).
  IF to_regclass('public.itens_estoque') IS NOT NULL THEN
    CREATE INDEX IF NOT EXISTS ix_itens_estoque_empresa_loja
      ON itens_estoque (""EmpresaId"", ""LojaId"") WHERE ""LojaId"" IS NOT NULL;
  END IF;
  IF to_regclass('public.audit_logs') IS NOT NULL THEN
    CREATE INDEX IF NOT EXISTS ix_audit_logs_usuario_data
      ON audit_logs (""UsuarioId"", ""DataHora"" DESC);
  END IF;

  -- (2) 20260525200000_AddPedidoStorefrontResolucao — aprovado_em & cia.
  IF to_regclass('public.pedidos') IS NOT NULL THEN
    ALTER TABLE pedidos ADD COLUMN IF NOT EXISTS aprovado_em timestamp with time zone;
    ALTER TABLE pedidos ADD COLUMN IF NOT EXISTS aprovado_por_usuario_id uuid;
    ALTER TABLE pedidos ADD COLUMN IF NOT EXISTS recusado_em timestamp with time zone;
    ALTER TABLE pedidos ADD COLUMN IF NOT EXISTS recusado_por_usuario_id uuid;
    ALTER TABLE pedidos ADD COLUMN IF NOT EXISTS motivo_recusa character varying(40);
    ALTER TABLE pedidos ADD COLUMN IF NOT EXISTS mensagem_recusa_cliente character varying(280);
    -- Status: alarga 20->32 só se ainda estiver menor (evita rewrite à toa).
    IF EXISTS (SELECT 1 FROM information_schema.columns
               WHERE table_name='pedidos' AND column_name='Status'
                 AND character_maximum_length < 32) THEN
      ALTER TABLE pedidos ALTER COLUMN ""Status"" TYPE character varying(32);
    END IF;
  END IF;

  -- (3) 20260528202748_AddNfeF1RepoIndexes — IdempotencyKey + índice parcial unique (#290).
  IF to_regclass('public.nfe_documentos') IS NOT NULL THEN
    ALTER TABLE nfe_documentos ADD COLUMN IF NOT EXISTS ""IdempotencyKey"" character varying(120);
    CREATE UNIQUE INDEX IF NOT EXISTS ux_nfe_documentos_empresa_idempotency
      ON nfe_documentos (""EmpresaId"", ""IdempotencyKey"") WHERE ""IdempotencyKey"" IS NOT NULL;
  END IF;
END $$;");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            // Intencionalmente vazio: esta migration reconcilia DDL que pertence
            // conceitualmente às 3 migrations anteriores (#465). Reverter NÃO deve
            // dropar colunas/índices legítimos — o rollback real seria reverter aquelas 3.
        }
    }
}
