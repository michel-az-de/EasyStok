using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace EasyStock.Infra.Postgre.Migrations
{
    /// <summary>
    /// RLS (ADR-0010 / camada 2) nas tabelas com empresa ou loja que ficaram de fora (#3108). Lista
    /// explícita, não varredura: <c>admin_audit_logs</c>, <c>system_error_logs</c>,
    /// <c>TenantFeatureFlags</c> e <c>fatura_contador</c> seguem sem RLS por desenho (ADR-0010, Skip list).
    /// Policy igual à do núcleo, com a coluna de cada tabela. <c>configuracoes_loja</c> só tem a loja: a
    /// linha aparece se a loja aparece (o EXISTS passa pela policy de <c>lojas</c>, tenant ou bypass).
    /// Idempotente (ENABLE/FORCE no-op se ligados, DROP IF EXISTS antes do CREATE) e fail-closed sem tenant.
    /// <c>report_runs.empresa_id</c> é anulável de propósito: run de plataforma (AdminSaaS) não tem empresa
    /// e só aparece sob bypass, por isso não há guarda de linha sem empresa aqui.
    /// Os workers que atravessam empresas nessas tabelas já rodam sob bypass desde a etapa anterior (#3140).
    /// </summary>
    public partial class AddRlsTabelasRestantes : Migration
    {
        /// <summary>
        /// Tabela, coluna de empresa e ordem. As duas mais gravadas (auditoria e outbox) ficam por último:
        /// se o lock de uma delas estourar o <c>lock_timeout</c>, a transação desfaz tudo sem ter segurado
        /// as outras por mais tempo que o necessário.
        /// </summary>
        private const string Tabelas = """
(VALUES (1, 'cliente_alteracoes', 'EmpresaId'),
        (2, 'etiqueta_templates', 'EmpresaId'),
        (3, 'etiqueta_empresa_default', 'EmpresaId'),
        (4, 'preferencias_menu_usuario', 'EmpresaId'),
        (5, 'report_runs', 'empresa_id'),
        (6, 'configuracoes_loja', NULL),
        (7, 'entity_alteracoes', 'EmpresaId'),
        (8, 'outbox_evento_integracao', 'empresa_id')) AS t(ordem, table_name, column_name)
""";

        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            // ENABLE/FORCE pedem lock exclusivo: melhor falhar o deploy do que enfileirar o app atrás dele.
            migrationBuilder.Sql("SET LOCAL lock_timeout = '5s';");
            migrationBuilder.Sql($"""
DO $rls$
DECLARE
    rec RECORD;
BEGIN
    FOR rec IN
        SELECT t.table_name, t.column_name FROM {Tabelas}
        WHERE to_regclass(format('%I.%I', current_schema(), t.table_name)) IS NOT NULL
        ORDER BY t.ordem
    LOOP
        EXECUTE format('ALTER TABLE %I.%I ENABLE ROW LEVEL SECURITY', current_schema(), rec.table_name);
        EXECUTE format('ALTER TABLE %I.%I FORCE ROW LEVEL SECURITY', current_schema(), rec.table_name);
        EXECUTE format('DROP POLICY IF EXISTS tenant_isolation ON %I.%I', current_schema(), rec.table_name);
        IF rec.column_name IS NULL THEN
            EXECUTE format($pol$
                CREATE POLICY tenant_isolation ON %1$I.%2$I
                    USING (EXISTS (SELECT 1 FROM %1$I.lojas l WHERE l."Id" = "LojaId"))
                    WITH CHECK (EXISTS (SELECT 1 FROM %1$I.lojas l WHERE l."Id" = "LojaId"))
            $pol$, current_schema(), rec.table_name);
        ELSE
            EXECUTE format($pol$
                CREATE POLICY tenant_isolation ON %1$I.%2$I
                    USING (
                        current_setting('app.bypass_rls', true) = 'true'
                        OR %3$I = NULLIF(current_setting('app.empresa_id', true), '')::uuid
                    )
                    WITH CHECK (
                        current_setting('app.bypass_rls', true) = 'true'
                        OR %3$I = NULLIF(current_setting('app.empresa_id', true), '')::uuid
                    )
            $pol$, current_schema(), rec.table_name, rec.column_name);
        END IF;
    END LOOP;
END
$rls$;
""");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql("SET LOCAL lock_timeout = '5s';");
            migrationBuilder.Sql($"""
DO $rls_down$
DECLARE
    rec RECORD;
BEGIN
    FOR rec IN
        SELECT t.table_name FROM {Tabelas}
        WHERE to_regclass(format('%I.%I', current_schema(), t.table_name)) IS NOT NULL
        ORDER BY t.ordem
    LOOP
        EXECUTE format('DROP POLICY IF EXISTS tenant_isolation ON %I.%I', current_schema(), rec.table_name);
        EXECUTE format('ALTER TABLE %I.%I NO FORCE ROW LEVEL SECURITY', current_schema(), rec.table_name);
        EXECUTE format('ALTER TABLE %I.%I DISABLE ROW LEVEL SECURITY', current_schema(), rec.table_name);
    END LOOP;
END
$rls_down$;
""");
        }
    }
}
