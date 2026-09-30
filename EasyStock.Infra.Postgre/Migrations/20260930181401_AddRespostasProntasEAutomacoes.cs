using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace EasyStock.Infra.Postgre.Migrations
{
    /// <inheritdoc />
    public partial class AddRespostasProntasEAutomacoes : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "regras_automaticas",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    EmpresaId = table.Column<Guid>(type: "uuid", nullable: false),
                    Gatilho = table.Column<int>(type: "integer", nullable: false),
                    Ligada = table.Column<bool>(type: "boolean", nullable: false),
                    Texto = table.Column<string>(type: "character varying(1024)", maxLength: 1024, nullable: false),
                    CriadaEm = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    AlteradaEm = table.Column<DateTime>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_regras_automaticas", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "respostas_prontas",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    EmpresaId = table.Column<Guid>(type: "uuid", nullable: false),
                    Titulo = table.Column<string>(type: "character varying(120)", maxLength: 120, nullable: false),
                    Atalho = table.Column<string>(type: "character varying(40)", maxLength: 40, nullable: false),
                    Texto = table.Column<string>(type: "character varying(4096)", maxLength: 4096, nullable: false),
                    Arquivada = table.Column<bool>(type: "boolean", nullable: false),
                    CriadaEm = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    AlteradaEm = table.Column<DateTime>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_respostas_prontas", x => x.Id);
                });

            migrationBuilder.CreateIndex(
                name: "ux_regras_automaticas_empresa_gatilho",
                table: "regras_automaticas",
                columns: new[] { "EmpresaId", "Gatilho" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ux_respostas_prontas_empresa_atalho",
                table: "respostas_prontas",
                columns: new[] { "EmpresaId", "Atalho" },
                unique: true);

            // RLS (ADR-0010, defesa em profundidade) — mesmo padrão de AddStorefront/
            // AddRowLevelSecurity para toda tabela nova com EmpresaId.
            migrationBuilder.Sql("""
DO $rls$
DECLARE
    rec RECORD;
    target_tables TEXT[] := ARRAY[
        'respostas_prontas',
        'regras_automaticas'
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
          AND c.table_name = ANY(target_tables)
        ORDER BY c.table_name
    LOOP
        EXECUTE format(
            'ALTER TABLE %I.%I ENABLE ROW LEVEL SECURITY',
            rec.table_schema, rec.table_name);

        EXECUTE format(
            'ALTER TABLE %I.%I FORCE ROW LEVEL SECURITY',
            rec.table_schema, rec.table_name);

        EXECUTE format(
            'DROP POLICY IF EXISTS tenant_isolation ON %I.%I',
            rec.table_schema, rec.table_name);

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
            migrationBuilder.DropTable(
                name: "regras_automaticas");

            migrationBuilder.DropTable(
                name: "respostas_prontas");
        }
    }
}
