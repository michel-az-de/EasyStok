using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace EasyStock.Infra.Postgre.Migrations
{
    /// <inheritdoc />
    public partial class AddConsentimentoContato : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "consentimentos_contato",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    EmpresaId = table.Column<Guid>(type: "uuid", nullable: false),
                    ClienteId = table.Column<Guid>(type: "uuid", nullable: false),
                    Canal = table.Column<int>(type: "integer", nullable: false),
                    Finalidade = table.Column<int>(type: "integer", nullable: false),
                    Situacao = table.Column<int>(type: "integer", nullable: false),
                    Origem = table.Column<string>(type: "character varying(60)", maxLength: 60, nullable: false),
                    AtualizadoEm = table.Column<DateTime>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_consentimentos_contato", x => x.Id);
                    table.ForeignKey(
                        name: "FK_consentimentos_contato_clientes_ClienteId",
                        column: x => x.ClienteId,
                        principalTable: "clientes",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_consentimentos_contato_ClienteId",
                table: "consentimentos_contato",
                column: "ClienteId");

            migrationBuilder.CreateIndex(
                name: "uq_consentimentos_contato_cliente_canal_finalidade",
                table: "consentimentos_contato",
                columns: new[] { "EmpresaId", "ClienteId", "Canal", "Finalidade" },
                unique: true);

            // Backfill (S38): quem tinha ConsentiuMarketing=true passa a ter marketing concedido nos
            // canais que o cadastro já tem (WhatsApp = 1 pelo telefone, e-mail = 5). Antes do RLS.
            migrationBuilder.Sql(""""
INSERT INTO consentimentos_contato ("Id", "EmpresaId", "ClienteId", "Canal", "Finalidade", "Situacao", "Origem", "AtualizadoEm")
SELECT gen_random_uuid(), c."EmpresaId", c."Id", 1, 2, 1, 'backfill_consentiu_marketing', now()
FROM clientes c
WHERE c."ConsentiuMarketing" AND COALESCE(TRIM(c."Telefone"), '') <> ''
UNION ALL
SELECT gen_random_uuid(), c."EmpresaId", c."Id", 5, 2, 1, 'backfill_consentiu_marketing', now()
FROM clientes c
WHERE c."ConsentiuMarketing" AND COALESCE(TRIM(c."Email"), '') <> '';
"""");

            // RLS (ADR-0010, defesa em profundidade) — mesmo padrão de AddStorefront/
            // AddRowLevelSecurity para toda tabela nova com EmpresaId.
            migrationBuilder.Sql("""
DO $rls$
DECLARE
    rec RECORD;
    target_tables TEXT[] := ARRAY[
        'consentimentos_contato'
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
                name: "consentimentos_contato");
        }
    }
}
