using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace EasyStock.Infra.Postgre.Migrations
{
    /// <inheritdoc />
    public partial class AddInventario : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "contagens",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    EmpresaId = table.Column<Guid>(type: "uuid", nullable: false),
                    Escopo = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    EscopoRefId = table.Column<Guid>(type: "uuid", nullable: true),
                    Modo = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    EstrategiaLote = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    Status = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    CriadoPorUsuarioId = table.Column<Guid>(type: "uuid", nullable: false),
                    CriadoEm = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    IniciadoEm = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    FinalizadoEm = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    AplicadoPorUsuarioId = table.Column<Guid>(type: "uuid", nullable: true),
                    AplicadoEm = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    Observacao = table.Column<string>(type: "character varying(1000)", maxLength: 1000, nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_contagens", x => x.Id);
                    table.ForeignKey(
                        name: "FK_contagens_empresas_EmpresaId",
                        column: x => x.EmpresaId,
                        principalTable: "empresas",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "ajustes_inventario",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    EmpresaId = table.Column<Guid>(type: "uuid", nullable: false),
                    ContagemId = table.Column<Guid>(type: "uuid", nullable: false),
                    CriadoPorUsuarioId = table.Column<Guid>(type: "uuid", nullable: false),
                    CriadoEm = table.Column<DateTime>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ajustes_inventario", x => x.Id);
                    table.ForeignKey(
                        name: "FK_ajustes_inventario_contagens_ContagemId",
                        column: x => x.ContagemId,
                        principalTable: "contagens",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_ajustes_inventario_empresas_EmpresaId",
                        column: x => x.EmpresaId,
                        principalTable: "empresas",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "itens_contagem",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    EmpresaId = table.Column<Guid>(type: "uuid", nullable: false),
                    ContagemId = table.Column<Guid>(type: "uuid", nullable: false),
                    ProdutoId = table.Column<Guid>(type: "uuid", nullable: false),
                    ItemEstoqueId = table.Column<Guid>(type: "uuid", nullable: true),
                    Validade = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    QtdSistemaNoMomento = table.Column<decimal>(type: "numeric(18,3)", nullable: true),
                    CustoUnitarioSnapshot = table.Column<decimal>(type: "numeric(18,2)", nullable: true),
                    QtdContada = table.Column<decimal>(type: "numeric(18,3)", nullable: true),
                    Conferido = table.Column<bool>(type: "boolean", nullable: false),
                    ContadoPorUsuarioId = table.Column<Guid>(type: "uuid", nullable: true),
                    ContadoEm = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    xmin = table.Column<uint>(type: "xid", rowVersion: true, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_itens_contagem", x => x.Id);
                    table.ForeignKey(
                        name: "FK_itens_contagem_contagens_ContagemId",
                        column: x => x.ContagemId,
                        principalTable: "contagens",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_itens_contagem_empresas_EmpresaId",
                        column: x => x.EmpresaId,
                        principalTable: "empresas",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_itens_contagem_itens_estoque_ItemEstoqueId",
                        column: x => x.ItemEstoqueId,
                        principalTable: "itens_estoque",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_itens_contagem_produtos_ProdutoId",
                        column: x => x.ProdutoId,
                        principalTable: "produtos",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "ajustes_inventario_linha",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    EmpresaId = table.Column<Guid>(type: "uuid", nullable: false),
                    AjusteInventarioId = table.Column<Guid>(type: "uuid", nullable: false),
                    ItemEstoqueId = table.Column<Guid>(type: "uuid", nullable: false),
                    ProdutoId = table.Column<Guid>(type: "uuid", nullable: false),
                    QtdAntes = table.Column<decimal>(type: "numeric(18,3)", nullable: false),
                    QtdDepois = table.Column<decimal>(type: "numeric(18,3)", nullable: false),
                    Delta = table.Column<decimal>(type: "numeric(18,3)", nullable: false),
                    CustoUnitarioSnapshot = table.Column<decimal>(type: "numeric(18,2)", nullable: false),
                    Tipo = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ajustes_inventario_linha", x => x.Id);
                    table.ForeignKey(
                        name: "FK_ajustes_inventario_linha_ajustes_inventario_AjusteInventari~",
                        column: x => x.AjusteInventarioId,
                        principalTable: "ajustes_inventario",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_ajustes_inventario_linha_empresas_EmpresaId",
                        column: x => x.EmpresaId,
                        principalTable: "empresas",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_ajustes_inventario_linha_itens_estoque_ItemEstoqueId",
                        column: x => x.ItemEstoqueId,
                        principalTable: "itens_estoque",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_ajustes_inventario_linha_produtos_ProdutoId",
                        column: x => x.ProdutoId,
                        principalTable: "produtos",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "ix_ajustes_inventario_contagem",
                table: "ajustes_inventario",
                column: "ContagemId");

            migrationBuilder.CreateIndex(
                name: "IX_ajustes_inventario_EmpresaId",
                table: "ajustes_inventario",
                column: "EmpresaId");

            migrationBuilder.CreateIndex(
                name: "IX_ajustes_inventario_linha_AjusteInventarioId",
                table: "ajustes_inventario_linha",
                column: "AjusteInventarioId");

            migrationBuilder.CreateIndex(
                name: "IX_ajustes_inventario_linha_EmpresaId",
                table: "ajustes_inventario_linha",
                column: "EmpresaId");

            migrationBuilder.CreateIndex(
                name: "IX_ajustes_inventario_linha_ItemEstoqueId",
                table: "ajustes_inventario_linha",
                column: "ItemEstoqueId");

            migrationBuilder.CreateIndex(
                name: "IX_ajustes_inventario_linha_ProdutoId",
                table: "ajustes_inventario_linha",
                column: "ProdutoId");

            migrationBuilder.CreateIndex(
                name: "ix_contagens_empresa_status",
                table: "contagens",
                columns: new[] { "EmpresaId", "Status" });

            migrationBuilder.CreateIndex(
                name: "ix_itens_contagem_contagem",
                table: "itens_contagem",
                column: "ContagemId");

            migrationBuilder.CreateIndex(
                name: "IX_itens_contagem_EmpresaId",
                table: "itens_contagem",
                column: "EmpresaId");

            migrationBuilder.CreateIndex(
                name: "IX_itens_contagem_ItemEstoqueId",
                table: "itens_contagem",
                column: "ItemEstoqueId");

            migrationBuilder.CreateIndex(
                name: "IX_itens_contagem_ProdutoId",
                table: "itens_contagem",
                column: "ProdutoId");

            // RLS (ADR-0010 / camada 2): habilita tenant_isolation nas 4 tabelas novas.
            // A AddRowLevelSecurity (dynamic, one-time) so protege tabelas existentes no
            // instante em que roda; tabelas novas precisam habilitar explicitamente
            // (mesmo padrao de 20260523162436_AddRlsBackfill...). Idempotente: ENABLE/FORCE
            // sao no-op se ja ligados; a policy usa DROP IF EXISTS antes do CREATE.
            // NULLIF(current_setting('app.empresa_id',true),'')::uuid = fail-closed quando
            // o tenant nao foi setado na conexao (0 linhas).
            migrationBuilder.Sql("""
DO $rls$
DECLARE
    rec RECORD;
    target_tables TEXT[] := ARRAY[
        'contagens',
        'itens_contagem',
        'ajustes_inventario',
        'ajustes_inventario_linha'
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
                name: "ajustes_inventario_linha");

            migrationBuilder.DropTable(
                name: "itens_contagem");

            migrationBuilder.DropTable(
                name: "ajustes_inventario");

            migrationBuilder.DropTable(
                name: "contagens");
        }
    }
}
