using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace EasyStock.Infra.Postgre.Migrations
{
    /// <inheritdoc />
    public partial class AddCobrancaPedido : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "cobrancas_pedido",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    EmpresaId = table.Column<Guid>(type: "uuid", nullable: false),
                    PedidoId = table.Column<Guid>(type: "uuid", nullable: false),
                    ConversaId = table.Column<Guid>(type: "uuid", nullable: true),
                    Provedor = table.Column<string>(type: "character varying(30)", maxLength: 30, nullable: false),
                    ReferenciaExterna = table.Column<string>(type: "character varying(120)", maxLength: 120, nullable: true),
                    LinkPagamento = table.Column<string>(type: "character varying(1000)", maxLength: 1000, nullable: true),
                    Valor = table.Column<decimal>(type: "numeric(14,2)", nullable: false),
                    ExpiraEm = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    Status = table.Column<int>(type: "integer", nullable: false),
                    PagaEm = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    ValorPago = table.Column<decimal>(type: "numeric(14,2)", nullable: true),
                    PagamentoExternoId = table.Column<string>(type: "character varying(120)", maxLength: 120, nullable: true),
                    MetodoPagamento = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: true),
                    Tentativa = table.Column<int>(type: "integer", nullable: false),
                    CriadaEm = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    AtualizadaEm = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    Motivo = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_cobrancas_pedido", x => x.Id);
                    table.ForeignKey(
                        name: "FK_cobrancas_pedido_pedidos_PedidoId",
                        column: x => x.PedidoId,
                        principalTable: "pedidos",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "ix_cobrancas_pedido_pedido_status",
                table: "cobrancas_pedido",
                columns: new[] { "PedidoId", "Status" });

            migrationBuilder.CreateIndex(
                name: "ix_cobrancas_pedido_pendentes_expira",
                table: "cobrancas_pedido",
                column: "ExpiraEm",
                filter: "\"Status\" = 1");

            migrationBuilder.CreateIndex(
                name: "uq_cobrancas_pedido_provedor_referencia",
                table: "cobrancas_pedido",
                columns: new[] { "Provedor", "ReferenciaExterna" },
                unique: true);

            // RLS (ADR-0010 / camada 2): habilita tenant_isolation na tabela nova. Tabelas criadas depois
            // da AddRowLevelSecurity precisam ligar explicitamente (mesmo bloco de
            // 20260922130316_AddAtendimentoConversaMensagem). Idempotente: ENABLE/FORCE sao no-op se ja
            // ligados; a policy usa DROP IF EXISTS antes do CREATE. Fail-closed sem app.empresa_id.
            migrationBuilder.Sql("""
DO $rls$
DECLARE
    rec RECORD;
    target_tables TEXT[] := ARRAY[
        'cobrancas_pedido'
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
                name: "cobrancas_pedido");
        }
    }
}
