using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace EasyStock.Infra.Postgre.Migrations
{
    /// <inheritdoc />
    public partial class AddLogisticaDemonstracao : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "logistica_cotacoes",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    EmpresaId = table.Column<Guid>(type: "uuid", nullable: false),
                    Provedor = table.Column<string>(type: "character varying(40)", maxLength: 40, nullable: false),
                    Referencia = table.Column<string>(type: "character varying(300)", maxLength: 300, nullable: false),
                    SolicitacaoJson = table.Column<string>(type: "jsonb", nullable: false),
                    ValorCentavos = table.Column<int>(type: "integer", nullable: false),
                    DuracaoMinutos = table.Column<int>(type: "integer", nullable: false),
                    ExpiraEm = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    CriadoEm = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    Simulado = table.Column<bool>(type: "boolean", nullable: false),
                    Indisponibilidade = table.Column<string>(type: "text", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_logistica_cotacoes", x => x.Id);
                    table.ForeignKey(
                        name: "FK_logistica_cotacoes_empresas_EmpresaId",
                        column: x => x.EmpresaId,
                        principalTable: "empresas",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "logistica_politicas",
                columns: table => new
                {
                    EmpresaId = table.Column<Guid>(type: "uuid", nullable: false),
                    Automatico = table.Column<bool>(type: "boolean", nullable: false),
                    TetoCentavos = table.Column<int>(type: "integer", nullable: false),
                    PermitirAgrupar = table.Column<bool>(type: "boolean", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_logistica_politicas", x => x.EmpresaId);
                    table.ForeignKey(
                        name: "FK_logistica_politicas_empresas_EmpresaId",
                        column: x => x.EmpresaId,
                        principalTable: "empresas",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "logistica_despachos",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    EmpresaId = table.Column<Guid>(type: "uuid", nullable: false),
                    CotacaoId = table.Column<Guid>(type: "uuid", nullable: false),
                    UsuarioId = table.Column<Guid>(type: "uuid", nullable: false),
                    ChaveIdempotencia = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    Provedor = table.Column<string>(type: "character varying(40)", maxLength: 40, nullable: false),
                    Referencia = table.Column<string>(type: "text", nullable: false),
                    RastreioUrl = table.Column<string>(type: "text", nullable: true),
                    PedidoIdsJson = table.Column<string>(type: "jsonb", nullable: false),
                    EventosJson = table.Column<string>(type: "jsonb", nullable: false),
                    Status = table.Column<string>(type: "character varying(30)", maxLength: 30, nullable: false),
                    ValorCentavos = table.Column<int>(type: "integer", nullable: false),
                    Simulado = table.Column<bool>(type: "boolean", nullable: false),
                    CriadoEm = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    MovimentoCaixaId = table.Column<Guid>(type: "uuid", nullable: true),
                    ChavePagamento = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: true),
                    MeioPagamento = table.Column<string>(type: "text", nullable: true),
                    ReferenciaPagamento = table.Column<string>(type: "text", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_logistica_despachos", x => x.Id);
                    table.ForeignKey(
                        name: "FK_logistica_despachos_empresas_EmpresaId",
                        column: x => x.EmpresaId,
                        principalTable: "empresas",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_logistica_despachos_logistica_cotacoes_CotacaoId",
                        column: x => x.CotacaoId,
                        principalTable: "logistica_cotacoes",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_logistica_despachos_movimentos_caixa_MovimentoCaixaId",
                        column: x => x.MovimentoCaixaId,
                        principalTable: "movimentos_caixa",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "IX_logistica_cotacoes_EmpresaId_CriadoEm",
                table: "logistica_cotacoes",
                columns: new[] { "EmpresaId", "CriadoEm" });

            migrationBuilder.CreateIndex(
                name: "IX_logistica_despachos_CotacaoId",
                table: "logistica_despachos",
                column: "CotacaoId");

            migrationBuilder.CreateIndex(
                name: "IX_logistica_despachos_EmpresaId_ChaveIdempotencia",
                table: "logistica_despachos",
                columns: new[] { "EmpresaId", "ChaveIdempotencia" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_logistica_despachos_EmpresaId_CotacaoId",
                table: "logistica_despachos",
                columns: new[] { "EmpresaId", "CotacaoId" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_logistica_despachos_MovimentoCaixaId",
                table: "logistica_despachos",
                column: "MovimentoCaixaId",
                unique: true);
            migrationBuilder.Sql("""
                DO $rls$
                DECLARE tabela text;
                BEGIN
                  FOREACH tabela IN ARRAY ARRAY['logistica_cotacoes', 'logistica_despachos', 'logistica_politicas'] LOOP
                    EXECUTE format('ALTER TABLE %I.%I ENABLE ROW LEVEL SECURITY', current_schema(), tabela);
                    EXECUTE format('ALTER TABLE %I.%I FORCE ROW LEVEL SECURITY', current_schema(), tabela);
                    EXECUTE format($pol$
                      CREATE POLICY tenant_isolation ON %I.%I
                      USING (current_setting('app.bypass_rls', true) = 'true'
                        OR "EmpresaId" = NULLIF(current_setting('app.empresa_id', true), '')::uuid)
                      WITH CHECK (current_setting('app.bypass_rls', true) = 'true'
                        OR "EmpresaId" = NULLIF(current_setting('app.empresa_id', true), '')::uuid)
                    $pol$, current_schema(), tabela);
                  END LOOP;
                END $rls$;
                """);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "logistica_despachos");

            migrationBuilder.DropTable(
                name: "logistica_politicas");

            migrationBuilder.DropTable(
                name: "logistica_cotacoes");
        }
    }
}
