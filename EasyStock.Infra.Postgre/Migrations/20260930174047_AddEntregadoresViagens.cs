using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace EasyStock.Infra.Postgre.Migrations
{
    /// <inheritdoc />
    public partial class AddEntregadoresViagens : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "entregadores",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    EmpresaId = table.Column<Guid>(type: "uuid", nullable: false),
                    Nome = table.Column<string>(type: "character varying(120)", maxLength: 120, nullable: false),
                    Tipo = table.Column<int>(type: "integer", nullable: false),
                    Empresa = table.Column<int>(type: "integer", nullable: false),
                    Telefone = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: true),
                    Veiculo = table.Column<string>(type: "character varying(60)", maxLength: 60, nullable: true),
                    Placa = table.Column<string>(type: "character varying(10)", maxLength: 10, nullable: true),
                    Ativo = table.Column<bool>(type: "boolean", nullable: false),
                    CriadoEm = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    AlteradoEm = table.Column<DateTime>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_entregadores", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "viagens",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    EmpresaId = table.Column<Guid>(type: "uuid", nullable: false),
                    EntregadorId = table.Column<Guid>(type: "uuid", nullable: true),
                    Situacao = table.Column<int>(type: "integer", nullable: false),
                    CriadaEm = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    SaiuEm = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    ConcluidaEm = table.Column<DateTime>(type: "timestamp with time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_viagens", x => x.Id);
                    table.ForeignKey(
                        name: "FK_viagens_entregadores_EntregadorId",
                        column: x => x.EntregadorId,
                        principalTable: "entregadores",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.SetNull);
                });

            migrationBuilder.CreateTable(
                name: "chamados_entregador",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    EmpresaId = table.Column<Guid>(type: "uuid", nullable: false),
                    ViagemId = table.Column<Guid>(type: "uuid", nullable: true),
                    Texto = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: false),
                    Situacao = table.Column<int>(type: "integer", nullable: false),
                    AbertoEm = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    AtendidoEm = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    CanceladoEm = table.Column<DateTime>(type: "timestamp with time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_chamados_entregador", x => x.Id);
                    table.ForeignKey(
                        name: "FK_chamados_entregador_viagens_ViagemId",
                        column: x => x.ViagemId,
                        principalTable: "viagens",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.SetNull);
                });

            migrationBuilder.CreateTable(
                name: "viagem_paradas",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    ViagemId = table.Column<Guid>(type: "uuid", nullable: false),
                    PedidoId = table.Column<Guid>(type: "uuid", nullable: false),
                    Ordem = table.Column<int>(type: "integer", nullable: false),
                    EntregueEm = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    EntregadorNome = table.Column<string>(type: "character varying(120)", maxLength: 120, nullable: true),
                    Veiculo = table.Column<string>(type: "character varying(60)", maxLength: 60, nullable: true),
                    Placa = table.Column<string>(type: "character varying(10)", maxLength: 10, nullable: true),
                    EmpresaEntregador = table.Column<int>(type: "integer", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_viagem_paradas", x => x.Id);
                    table.ForeignKey(
                        name: "FK_viagem_paradas_pedidos_PedidoId",
                        column: x => x.PedidoId,
                        principalTable: "pedidos",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_viagem_paradas_viagens_ViagemId",
                        column: x => x.ViagemId,
                        principalTable: "viagens",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "ix_chamados_entregador_empresa_situacao_aberto_em",
                table: "chamados_entregador",
                columns: new[] { "EmpresaId", "Situacao", "AbertoEm" });

            migrationBuilder.CreateIndex(
                name: "IX_chamados_entregador_ViagemId",
                table: "chamados_entregador",
                column: "ViagemId");

            migrationBuilder.CreateIndex(
                name: "ix_entregadores_empresa_ativo_nome",
                table: "entregadores",
                columns: new[] { "EmpresaId", "Ativo", "Nome" });

            migrationBuilder.CreateIndex(
                name: "ix_viagem_paradas_pedido",
                table: "viagem_paradas",
                column: "PedidoId");

            migrationBuilder.CreateIndex(
                name: "ux_viagem_paradas_viagem_pedido",
                table: "viagem_paradas",
                columns: new[] { "ViagemId", "PedidoId" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_viagens_empresa_situacao_criada_em",
                table: "viagens",
                columns: new[] { "EmpresaId", "Situacao", "CriadaEm" });

            migrationBuilder.CreateIndex(
                name: "IX_viagens_EntregadorId",
                table: "viagens",
                column: "EntregadorId");

            // RLS (ADR-0010, defesa em profundidade) — mesmo padrão de AddStorefront/
            // AddRowLevelSecurity para toda tabela nova com EmpresaId.
            migrationBuilder.Sql("""
DO $rls$
DECLARE
    rec RECORD;
    target_tables TEXT[] := ARRAY[
        'entregadores',
        'viagens',
        'chamados_entregador'
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
                name: "chamados_entregador");

            migrationBuilder.DropTable(
                name: "viagem_paradas");

            migrationBuilder.DropTable(
                name: "viagens");

            migrationBuilder.DropTable(
                name: "entregadores");
        }
    }
}
