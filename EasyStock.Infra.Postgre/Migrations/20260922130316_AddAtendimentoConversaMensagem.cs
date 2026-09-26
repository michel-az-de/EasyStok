using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace EasyStock.Infra.Postgre.Migrations
{
    /// <inheritdoc />
    public partial class AddAtendimentoConversaMensagem : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "atendimento_conversas",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    EmpresaId = table.Column<Guid>(type: "uuid", nullable: false),
                    ClienteId = table.Column<Guid>(type: "uuid", nullable: true),
                    ContatoWaId = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    ContatoNome = table.Column<string>(type: "character varying(120)", maxLength: 120, nullable: true),
                    Canal = table.Column<int>(type: "integer", nullable: false),
                    Situacao = table.Column<int>(type: "integer", nullable: false),
                    PedidoEmAndamentoId = table.Column<Guid>(type: "uuid", nullable: true),
                    ContextoJson = table.Column<string>(type: "jsonb", nullable: false),
                    IniciadaEm = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    UltimaMensagemEm = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    UltimaMensagemEntradaEm = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    NaoLidas = table.Column<int>(type: "integer", nullable: false),
                    EncerradaEm = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    AssumidaPorUsuarioId = table.Column<Guid>(type: "uuid", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_atendimento_conversas", x => x.Id);
                    table.ForeignKey(
                        name: "FK_atendimento_conversas_clientes_ClienteId",
                        column: x => x.ClienteId,
                        principalTable: "clientes",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "atendimento_mensagens",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    EmpresaId = table.Column<Guid>(type: "uuid", nullable: false),
                    ConversaId = table.Column<Guid>(type: "uuid", nullable: false),
                    ExternoId = table.Column<string>(type: "character varying(128)", maxLength: 128, nullable: true),
                    Direcao = table.Column<int>(type: "integer", nullable: false),
                    Autor = table.Column<int>(type: "integer", nullable: false),
                    TipoConteudo = table.Column<int>(type: "integer", nullable: false),
                    Texto = table.Column<string>(type: "character varying(4096)", maxLength: 4096, nullable: true),
                    BotaoId = table.Column<string>(type: "character varying(256)", maxLength: 256, nullable: true),
                    MidiaChave = table.Column<string>(type: "character varying(300)", maxLength: 300, nullable: true),
                    MidiaMime = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: true),
                    Status = table.Column<int>(type: "integer", nullable: false),
                    Erro = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: true),
                    EnviadaEm = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    ProcessadaEm = table.Column<DateTime>(type: "timestamp with time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_atendimento_mensagens", x => x.Id);
                    table.ForeignKey(
                        name: "FK_atendimento_mensagens_atendimento_conversas_ConversaId",
                        column: x => x.ConversaId,
                        principalTable: "atendimento_conversas",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "IX_atendimento_conversas_ClienteId",
                table: "atendimento_conversas",
                column: "ClienteId");

            migrationBuilder.CreateIndex(
                name: "ix_atendimento_conversas_empresa_cliente",
                table: "atendimento_conversas",
                columns: new[] { "EmpresaId", "ClienteId" });

            migrationBuilder.CreateIndex(
                name: "ix_atendimento_conversas_empresa_ultima_msg",
                table: "atendimento_conversas",
                columns: new[] { "EmpresaId", "UltimaMensagemEm" },
                descending: new[] { false, true });

            migrationBuilder.CreateIndex(
                name: "uq_atendimento_conversas_empresa_contato_aberta",
                table: "atendimento_conversas",
                columns: new[] { "EmpresaId", "ContatoWaId" },
                unique: true,
                filter: "\"Situacao\" <> 3");

            migrationBuilder.CreateIndex(
                name: "ix_atendimento_mensagens_conversa_enviada",
                table: "atendimento_mensagens",
                columns: new[] { "ConversaId", "EnviadaEm" });

            migrationBuilder.CreateIndex(
                name: "uq_atendimento_mensagens_empresa_externo",
                table: "atendimento_mensagens",
                columns: new[] { "EmpresaId", "ExternoId" },
                unique: true,
                filter: "\"ExternoId\" IS NOT NULL");

            // RLS (ADR-0010 / camada 2): habilita tenant_isolation nas 2 tabelas novas.
            // Tabelas criadas depois da AddRowLevelSecurity precisam ligar explicitamente
            // (mesmo padrao de 20260627210645_AddInventario). Idempotente: ENABLE/FORCE sao
            // no-op se ja ligados; a policy usa DROP IF EXISTS antes do CREATE.
            // NULLIF(current_setting('app.empresa_id',true),'')::uuid = fail-closed quando
            // o tenant nao foi setado na conexao (0 linhas).
            migrationBuilder.Sql("""
DO $rls$
DECLARE
    rec RECORD;
    target_tables TEXT[] := ARRAY[
        'atendimento_conversas',
        'atendimento_mensagens'
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
                name: "atendimento_mensagens");

            migrationBuilder.DropTable(
                name: "atendimento_conversas");
        }
    }
}
