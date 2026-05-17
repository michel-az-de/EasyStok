using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace EasyStock.Infra.Postgre.Migrations
{
    /// <summary>
    /// Habilita Row-Level Security (RLS) no Postgres como defesa em profundidade
    /// do isolamento multi-tenant. O Global Query Filter do EF Core
    /// (<see cref="Data.EasyStockDbContext.CurrentTenantId"/>) continua sendo a
    /// primeira camada; o RLS protege contra:
    /// <list type="bullet">
    ///   <item>SQL crua via Dapper/<c>FromSqlRaw</c> sem filtro manual.</item>
    ///   <item>LINQ com <c>IgnoreQueryFilters()</c> esquecido em uma branch nova.</item>
    ///   <item>Bug futuro no <c>ApplyTenantQueryFilters</c> que vaze uma entidade.</item>
    /// </list>
    /// O contexto de tenant chega via <c>SET app.empresa_id</c> emitido pelo
    /// <c>SetTenantOnConnectionInterceptor</c> a cada abertura de conexão. Para
    /// jobs e seeds cross-tenant (migrations, SuperAdmin seed, reconciliação de
    /// fatura, etc.) o interceptor emite <c>SET app.bypass_rls = 'true'</c>
    /// quando a flag <see cref="Data.EasyStockDbContext.BypassRowLevelSecurity"/>
    /// está ligada via <c>UseRowLevelSecurityBypass()</c>.
    ///
    /// Por que dinâmico (information_schema) em vez de lista hardcoded:
    /// a) Surge entidade nova com EmpresaId → automaticamente protegida no
    ///    próximo deploy sem ter que lembrar de editar migration.
    /// b) Skip list permanece pequeno e explícito.
    ///
    /// Tabelas isentas (cross-tenant ou esquema separado):
    /// <list type="bullet">
    ///   <item><c>admin_impersonation_logs</c> — audit cross-tenant do SuperAdmin.</item>
    ///   <item><c>TenantFeatureFlags</c> — toggles globais avaliados sem JWT.</item>
    ///   <item><c>fatura_contador</c> — sequência por (EmpresaId, Ano), acesso via PK direta.</item>
    ///   <item><c>mobile_*</c> — módulo Casa da Baba tem isolamento por loja, não empresa.</item>
    /// </list>
    /// </summary>
    public partial class AddRowLevelSecurity : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            // Raw string literal (""""..."""") — o SQL contém $rls$ e $pol$ como
            // dollar-quoted strings do Postgres, que conflitam com $"..." em C#.
            migrationBuilder.Sql("""
DO $rls$
DECLARE
    rec RECORD;
    skip_tables TEXT[] := ARRAY[
        'admin_impersonation_logs',
        'TenantFeatureFlags',
        'fatura_contador'
    ];
BEGIN
    FOR rec IN
        SELECT c.table_schema, c.table_name
        FROM information_schema.columns c
        JOIN information_schema.tables t
          ON t.table_schema = c.table_schema
         AND t.table_name   = c.table_name
        WHERE c.column_name = 'EmpresaId'
          AND c.table_schema = current_schema()
          AND t.table_type   = 'BASE TABLE'
          AND c.table_name <> ALL(skip_tables)
          AND c.table_name NOT LIKE 'mobile\_%' ESCAPE '\'
        ORDER BY c.table_name
    LOOP
        EXECUTE format(
            'ALTER TABLE %I.%I ENABLE ROW LEVEL SECURITY',
            rec.table_schema, rec.table_name);

        -- FORCE garante que mesmo o owner da tabela respeita a policy.
        -- Sem isso, o usuário que criou a tabela (geralmente o app) bypassa RLS
        -- por default — exatamente o que queremos evitar.
        EXECUTE format(
            'ALTER TABLE %I.%I FORCE ROW LEVEL SECURITY',
            rec.table_schema, rec.table_name);

        EXECUTE format(
            'DROP POLICY IF EXISTS tenant_isolation ON %I.%I',
            rec.table_schema, rec.table_name);

        -- USING (SELECT/UPDATE/DELETE visibility): bypass OU tenant casa.
        -- WITH CHECK (INSERT/UPDATE writability): mesma regra — evita escrita
        -- cross-tenant mesmo via UPDATE que mudaria EmpresaId.
        -- NULLIF('','')::uuid trata o caso de connection nova de pool sem tenant
        -- setado: current_setting retorna '' (missing_ok=true), NULLIF vira NULL,
        -- comparação com NULL é UNKNOWN/false → 0 linhas (fail-closed).
        EXECUTE format($pol$
            CREATE POLICY tenant_isolation ON %I.%I
                USING (
                    current_setting('app.bypass_rls', true) = 'true'
                    OR "EmpresaId" = NULLIF(current_setting('app.empresa_id', true), '')::uuid
                )
                WITH CHECK (
                    current_setting('app.bypass_rls', true) = 'true'
                    OR "EmpresaId" = NULLIF(current_setting('app.empresa_id', true), '')::uuid
                )
        $pol$, rec.table_schema, rec.table_name);
    END LOOP;
END
$rls$;
""");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql("""
DO $rls_down$
DECLARE
    rec RECORD;
    skip_tables TEXT[] := ARRAY[
        'admin_impersonation_logs',
        'TenantFeatureFlags',
        'fatura_contador'
    ];
BEGIN
    FOR rec IN
        SELECT c.table_schema, c.table_name
        FROM information_schema.columns c
        JOIN information_schema.tables t
          ON t.table_schema = c.table_schema
         AND t.table_name   = c.table_name
        WHERE c.column_name = 'EmpresaId'
          AND c.table_schema = current_schema()
          AND t.table_type   = 'BASE TABLE'
          AND c.table_name <> ALL(skip_tables)
          AND c.table_name NOT LIKE 'mobile\_%' ESCAPE '\'
    LOOP
        EXECUTE format('DROP POLICY IF EXISTS tenant_isolation ON %I.%I',
            rec.table_schema, rec.table_name);
        EXECUTE format('ALTER TABLE %I.%I NO FORCE ROW LEVEL SECURITY',
            rec.table_schema, rec.table_name);
        EXECUTE format('ALTER TABLE %I.%I DISABLE ROW LEVEL SECURITY',
            rec.table_schema, rec.table_name);
    END LOOP;
END
$rls_down$;
""");
        }
    }
}
