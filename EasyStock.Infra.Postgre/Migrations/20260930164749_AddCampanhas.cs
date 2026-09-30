using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace EasyStock.Infra.Postgre.Migrations
{
    /// <inheritdoc />
    public partial class AddCampanhas : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "campanhas",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    EmpresaId = table.Column<Guid>(type: "uuid", nullable: false),
                    Nome = table.Column<string>(type: "character varying(120)", maxLength: 120, nullable: false),
                    Mensagem = table.Column<string>(type: "character varying(4096)", maxLength: 4096, nullable: false),
                    ImagemUrl = table.Column<string>(type: "character varying(1000)", maxLength: 1000, nullable: true),
                    TemplateMeta = table.Column<string>(type: "character varying(120)", maxLength: 120, nullable: true),
                    FiltroJson = table.Column<string>(type: "jsonb", nullable: false),
                    TagsRestricaoExcluidas = table.Column<string>(type: "character varying(1000)", maxLength: 1000, nullable: false),
                    Status = table.Column<int>(type: "integer", nullable: false),
                    DisparoEm = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    EncerramentoEm = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    EnviarLembreteEncerramento = table.Column<bool>(type: "boolean", nullable: false),
                    TamanhoOnda = table.Column<int>(type: "integer", nullable: true),
                    OndaAtual = table.Column<int>(type: "integer", nullable: false),
                    CriadaEm = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    CriadaPorUsuarioId = table.Column<Guid>(type: "uuid", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_campanhas", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "campanha_destinatarios",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    EmpresaId = table.Column<Guid>(type: "uuid", nullable: false),
                    CampanhaId = table.Column<Guid>(type: "uuid", nullable: false),
                    ClienteId = table.Column<Guid>(type: "uuid", nullable: false),
                    Onda = table.Column<int>(type: "integer", nullable: false),
                    Status = table.Column<int>(type: "integer", nullable: false),
                    MotivoExclusao = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: true),
                    OutboxMensagemId = table.Column<Guid>(type: "uuid", nullable: true),
                    EnviadoEm = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    PedidoId = table.Column<Guid>(type: "uuid", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_campanha_destinatarios", x => x.Id);
                    table.CheckConstraint("ck_campanha_destinatarios_motivo_exclusao", "\"MotivoExclusao\" IS NULL OR \"MotivoExclusao\" IN ('restricao','limite_semanal','sem_consentimento','bloqueado','sem_telefone','cancelada')");
                    table.ForeignKey(
                        name: "FK_campanha_destinatarios_campanhas_CampanhaId",
                        column: x => x.CampanhaId,
                        principalTable: "campanhas",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_campanha_destinatarios_clientes_ClienteId",
                        column: x => x.ClienteId,
                        principalTable: "clientes",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_campanha_destinatarios_ClienteId_EnviadoEm",
                table: "campanha_destinatarios",
                columns: new[] { "ClienteId", "EnviadoEm" });

            migrationBuilder.CreateIndex(
                name: "uq_campanha_destinatarios_campanha_cliente",
                table: "campanha_destinatarios",
                columns: new[] { "CampanhaId", "ClienteId" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_campanhas_EmpresaId_Status",
                table: "campanhas",
                columns: new[] { "EmpresaId", "Status" });

            // RLS (ADR-0010, defesa em profundidade) — mesmo padrão de AddClienteTagNotaBloqueioPreferencias
            // para toda tabela nova com EmpresaId.
            migrationBuilder.Sql("""
DO $rls$
DECLARE
    rec RECORD;
    target_tables TEXT[] := ARRAY[
        'campanhas',
        'campanha_destinatarios'
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
                name: "campanha_destinatarios");

            migrationBuilder.DropTable(
                name: "campanhas");
        }
    }
}
