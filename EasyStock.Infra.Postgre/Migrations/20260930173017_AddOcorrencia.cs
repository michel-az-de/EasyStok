using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace EasyStock.Infra.Postgre.Migrations
{
    /// <inheritdoc />
    public partial class AddOcorrencia : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "ocorrencias",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    EmpresaId = table.Column<Guid>(type: "uuid", nullable: false),
                    PedidoId = table.Column<Guid>(type: "uuid", nullable: false),
                    ClienteId = table.Column<Guid>(type: "uuid", nullable: false),
                    ConversaId = table.Column<Guid>(type: "uuid", nullable: true),
                    Origem = table.Column<int>(type: "integer", nullable: false),
                    Categoria = table.Column<int>(type: "integer", nullable: false),
                    Relato = table.Column<string>(type: "character varying(1000)", maxLength: 1000, nullable: false),
                    Status = table.Column<int>(type: "integer", nullable: false),
                    Resolucao = table.Column<string>(type: "character varying(1000)", maxLength: 1000, nullable: true),
                    ReembolsoValor = table.Column<decimal>(type: "numeric(18,2)", precision: 18, scale: 2, nullable: true),
                    ReembolsoIdSolicitacao = table.Column<string>(type: "character varying(120)", maxLength: 120, nullable: true),
                    ReembolsoEm = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    CriadaEm = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    ResolvidaEm = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    ResolvidaPorUsuarioId = table.Column<Guid>(type: "uuid", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ocorrencias", x => x.Id);
                    table.ForeignKey(
                        name: "FK_ocorrencias_pedidos_PedidoId",
                        column: x => x.PedidoId,
                        principalTable: "pedidos",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "ix_ocorrencias_empresa_status_criada",
                table: "ocorrencias",
                columns: new[] { "EmpresaId", "Status", "CriadaEm" });

            migrationBuilder.CreateIndex(
                name: "ix_ocorrencias_pedido",
                table: "ocorrencias",
                column: "PedidoId");

            // RLS (ADR-0010 / camada 2): habilita tenant_isolation na tabela nova. Tabelas criadas depois
            // da AddRowLevelSecurity precisam ligar explicitamente (mesmo bloco de
            // 20260922130316_AddAtendimentoConversaMensagem). Idempotente: ENABLE/FORCE sao no-op se ja
            // ligados; a policy usa DROP IF EXISTS antes do CREATE. Fail-closed sem app.empresa_id.
            migrationBuilder.Sql("""
DO $rls$
DECLARE
    rec RECORD;
    target_tables TEXT[] := ARRAY[
        'ocorrencias'
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
        EXECUTE format('ALTER TABLE %I.%I ENABLE ROW LEVEL SECURITY', rec.table_schema, rec.table_name);
        EXECUTE format('ALTER TABLE %I.%I FORCE ROW LEVEL SECURITY', rec.table_schema, rec.table_name);
        EXECUTE format('DROP POLICY IF EXISTS tenant_isolation ON %I.%I', rec.table_schema, rec.table_name);
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
                name: "ocorrencias");
        }
    }
}
