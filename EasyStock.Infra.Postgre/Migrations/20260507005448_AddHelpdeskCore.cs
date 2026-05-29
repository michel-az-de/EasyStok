using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace EasyStock.Infra.Postgre.Migrations
{
    /// <inheritdoc />
    public partial class AddHelpdeskCore : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_admin_tickets_AtendenteId",
                table: "admin_tickets");

            migrationBuilder.DropIndex(
                name: "IX_admin_tickets_EmpresaId",
                table: "admin_tickets");

            migrationBuilder.DropIndex(
                name: "IX_admin_ticket_mensagens_TicketId",
                table: "admin_ticket_mensagens");

            migrationBuilder.AddColumn<string>(
                name: "Nivel",
                table: "admin_tickets",
                type: "character varying(4)",
                maxLength: 4,
                nullable: false,
                defaultValue: "N1");

            migrationBuilder.AddColumn<Guid>(
                name: "OrigemTicketId",
                table: "admin_tickets",
                type: "uuid",
                nullable: true);

            migrationBuilder.AddColumn<DateTime>(
                name: "PrazoResolucao",
                table: "admin_tickets",
                type: "timestamp with time zone",
                nullable: true);

            migrationBuilder.AddColumn<DateTime>(
                name: "PrazoResposta",
                table: "admin_tickets",
                type: "timestamp with time zone",
                nullable: true);

            migrationBuilder.AddColumn<DateTime>(
                name: "PrimeiraRespostaEm",
                table: "admin_tickets",
                type: "timestamp with time zone",
                nullable: true);

            migrationBuilder.AddColumn<DateTime>(
                name: "ResolvidoEm",
                table: "admin_tickets",
                type: "timestamp with time zone",
                nullable: true);

            migrationBuilder.AddColumn<bool>(
                name: "SlaResolucaoViolado",
                table: "admin_tickets",
                type: "boolean",
                nullable: false,
                defaultValue: false);

            migrationBuilder.AddColumn<bool>(
                name: "SlaRespostaViolado",
                table: "admin_tickets",
                type: "boolean",
                nullable: false,
                defaultValue: false);

            migrationBuilder.AddColumn<DateTime>(
                name: "UltimoAlerta50PctEm",
                table: "admin_tickets",
                type: "timestamp with time zone",
                nullable: true);

            migrationBuilder.AddColumn<DateTime>(
                name: "UltimoAlerta80PctEm",
                table: "admin_tickets",
                type: "timestamp with time zone",
                nullable: true);

            migrationBuilder.AddColumn<bool>(
                name: "Interno",
                table: "admin_ticket_mensagens",
                type: "boolean",
                nullable: false,
                defaultValue: false);

            migrationBuilder.CreateTable(
                name: "admin_ticket_tecnico_meta",
                columns: table => new
                {
                    TicketId = table.Column<Guid>(type: "uuid", nullable: false),
                    SeveridadeTecnica = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    ComponenteAfetado = table.Column<string>(type: "character varying(120)", maxLength: 120, nullable: true),
                    StackTrace = table.Column<string>(type: "text", nullable: true),
                    FixVersion = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: true),
                    ResolvidoEm = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    CriadoEm = table.Column<DateTime>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_admin_ticket_tecnico_meta", x => x.TicketId);
                    table.ForeignKey(
                        name: "FK_admin_ticket_tecnico_meta_admin_tickets_TicketId",
                        column: x => x.TicketId,
                        principalTable: "admin_tickets",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "sla_configuracao",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    EmpresaId = table.Column<Guid>(type: "uuid", nullable: true),
                    PlanoId = table.Column<Guid>(type: "uuid", nullable: true),
                    Prioridade = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    MinutosResposta = table.Column<int>(type: "integer", nullable: false),
                    MinutosResolucao = table.Column<int>(type: "integer", nullable: false),
                    HorarioComercialApenas = table.Column<bool>(type: "boolean", nullable: false, defaultValue: false),
                    CriadoEm = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    AlteradoEm = table.Column<DateTime>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_sla_configuracao", x => x.Id);
                    table.ForeignKey(
                        name: "FK_sla_configuracao_empresas_EmpresaId",
                        column: x => x.EmpresaId,
                        principalTable: "empresas",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_sla_configuracao_planos_PlanoId",
                        column: x => x.PlanoId,
                        principalTable: "planos",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "ticket_anexos",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    TicketId = table.Column<Guid>(type: "uuid", nullable: false),
                    MensagemId = table.Column<Guid>(type: "uuid", nullable: true),
                    NomeArquivo = table.Column<string>(type: "character varying(255)", maxLength: 255, nullable: false),
                    ContentType = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    TamanhoBytes = table.Column<long>(type: "bigint", nullable: false),
                    StorageKey = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: false),
                    Url = table.Column<string>(type: "character varying(1000)", maxLength: 1000, nullable: false),
                    IsPublico = table.Column<bool>(type: "boolean", nullable: false, defaultValue: false),
                    EnviadoPorId = table.Column<Guid>(type: "uuid", nullable: false),
                    IsAdmin = table.Column<bool>(type: "boolean", nullable: false, defaultValue: false),
                    CriadoEm = table.Column<DateTime>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ticket_anexos", x => x.Id);
                    table.ForeignKey(
                        name: "FK_ticket_anexos_admin_ticket_mensagens_MensagemId",
                        column: x => x.MensagemId,
                        principalTable: "admin_ticket_mensagens",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.SetNull);
                    table.ForeignKey(
                        name: "FK_ticket_anexos_admin_tickets_TicketId",
                        column: x => x.TicketId,
                        principalTable: "admin_tickets",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_ticket_anexos_usuarios_EnviadoPorId",
                        column: x => x.EnviadoPorId,
                        principalTable: "usuarios",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "ticket_historico",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    TicketId = table.Column<Guid>(type: "uuid", nullable: false),
                    AutorId = table.Column<Guid>(type: "uuid", nullable: true),
                    Acao = table.Column<string>(type: "character varying(40)", maxLength: 40, nullable: false),
                    ValorAntes = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: true),
                    ValorDepois = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: true),
                    MetadadosJson = table.Column<string>(type: "jsonb", nullable: true),
                    CriadoEm = table.Column<DateTime>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ticket_historico", x => x.Id);
                    table.ForeignKey(
                        name: "FK_ticket_historico_admin_tickets_TicketId",
                        column: x => x.TicketId,
                        principalTable: "admin_tickets",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_ticket_historico_usuarios_AutorId",
                        column: x => x.AutorId,
                        principalTable: "usuarios",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.SetNull);
                });

            migrationBuilder.CreateIndex(
                name: "ix_admin_tickets_atendente_status",
                table: "admin_tickets",
                columns: new[] { "AtendenteId", "Status" });

            migrationBuilder.CreateIndex(
                name: "ix_admin_tickets_empresa_status_prioridade",
                table: "admin_tickets",
                columns: new[] { "EmpresaId", "Status", "Prioridade" });

            migrationBuilder.CreateIndex(
                name: "ix_admin_tickets_nivel_status",
                table: "admin_tickets",
                columns: new[] { "Nivel", "Status" });

            migrationBuilder.CreateIndex(
                name: "ix_admin_tickets_origem_ticket_id",
                table: "admin_tickets",
                column: "OrigemTicketId");

            migrationBuilder.CreateIndex(
                name: "ix_admin_tickets_status_prazo_resolucao",
                table: "admin_tickets",
                columns: new[] { "Status", "PrazoResolucao" },
                filter: "\"Status\" IN ('Aberto','EmAtendimento','AguardandoCliente') AND \"PrazoResolucao\" IS NOT NULL");

            migrationBuilder.CreateIndex(
                name: "ix_admin_tickets_status_prazo_resposta",
                table: "admin_tickets",
                columns: new[] { "Status", "PrazoResposta" },
                filter: "\"Status\" IN ('Aberto','EmAtendimento','AguardandoCliente') AND \"PrazoResposta\" IS NOT NULL");

            migrationBuilder.CreateIndex(
                name: "ix_admin_ticket_mensagens_ticket_interno_criado",
                table: "admin_ticket_mensagens",
                columns: new[] { "TicketId", "Interno", "CriadoEm" });

            migrationBuilder.CreateIndex(
                name: "ix_sla_configuracao_empresa_prioridade",
                table: "sla_configuracao",
                columns: new[] { "EmpresaId", "Prioridade" },
                filter: "\"EmpresaId\" IS NOT NULL");

            migrationBuilder.CreateIndex(
                name: "ix_sla_configuracao_plano_prioridade",
                table: "sla_configuracao",
                columns: new[] { "PlanoId", "Prioridade" },
                filter: "\"PlanoId\" IS NOT NULL");

            migrationBuilder.CreateIndex(
                name: "IX_ticket_anexos_EnviadoPorId",
                table: "ticket_anexos",
                column: "EnviadoPorId");

            migrationBuilder.CreateIndex(
                name: "ix_ticket_anexos_mensagem_id",
                table: "ticket_anexos",
                column: "MensagemId");

            migrationBuilder.CreateIndex(
                name: "ix_ticket_anexos_ticket_id",
                table: "ticket_anexos",
                column: "TicketId");

            migrationBuilder.CreateIndex(
                name: "IX_ticket_historico_AutorId",
                table: "ticket_historico",
                column: "AutorId");

            migrationBuilder.CreateIndex(
                name: "ix_ticket_historico_ticket_criado",
                table: "ticket_historico",
                columns: new[] { "TicketId", "CriadoEm" });

            migrationBuilder.AddForeignKey(
                name: "FK_admin_tickets_admin_tickets_OrigemTicketId",
                table: "admin_tickets",
                column: "OrigemTicketId",
                principalTable: "admin_tickets",
                principalColumn: "Id",
                onDelete: ReferentialAction.SetNull);

            // Backfill de tickets existentes com prazos default conservadores
            // (24h resposta, 72h resolucao a partir da data de criacao).
            // Idempotente: so atualiza onde ainda nao foi setado.
            migrationBuilder.Sql(@"
                UPDATE admin_tickets
                SET ""PrazoResposta"" = ""CriadoEm"" + interval '24 hours'
                WHERE ""PrazoResposta"" IS NULL;

                UPDATE admin_tickets
                SET ""PrazoResolucao"" = ""CriadoEm"" + interval '72 hours'
                WHERE ""PrazoResolucao"" IS NULL;
            ");

            // Seed da matriz default de SLA (Prioridade) — global (PlanoId/EmpresaId NULL).
            // Tempos em minutos. Idempotente via NOT EXISTS no nivel global.
            migrationBuilder.Sql(@"
                INSERT INTO sla_configuracao (""Id"", ""EmpresaId"", ""PlanoId"", ""Prioridade"", ""MinutosResposta"", ""MinutosResolucao"", ""HorarioComercialApenas"", ""CriadoEm"", ""AlteradoEm"")
                SELECT gen_random_uuid(), NULL, NULL, p.prio, p.resp, p.resol, false, now() at time zone 'utc', now() at time zone 'utc'
                FROM (VALUES
                    ('Critica', 30, 240),
                    ('Alta', 120, 480),
                    ('Normal', 480, 1440),
                    ('Baixa', 1440, 4320)
                ) AS p(prio, resp, resol)
                WHERE NOT EXISTS (
                    SELECT 1 FROM sla_configuracao s
                    WHERE s.""Prioridade"" = p.prio AND s.""EmpresaId"" IS NULL AND s.""PlanoId"" IS NULL
                );
            ");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_admin_tickets_admin_tickets_OrigemTicketId",
                table: "admin_tickets");

            migrationBuilder.DropTable(
                name: "admin_ticket_tecnico_meta");

            migrationBuilder.DropTable(
                name: "sla_configuracao");

            migrationBuilder.DropTable(
                name: "ticket_anexos");

            migrationBuilder.DropTable(
                name: "ticket_historico");

            migrationBuilder.DropIndex(
                name: "ix_admin_tickets_atendente_status",
                table: "admin_tickets");

            migrationBuilder.DropIndex(
                name: "ix_admin_tickets_empresa_status_prioridade",
                table: "admin_tickets");

            migrationBuilder.DropIndex(
                name: "ix_admin_tickets_nivel_status",
                table: "admin_tickets");

            migrationBuilder.DropIndex(
                name: "ix_admin_tickets_origem_ticket_id",
                table: "admin_tickets");

            migrationBuilder.DropIndex(
                name: "ix_admin_tickets_status_prazo_resolucao",
                table: "admin_tickets");

            migrationBuilder.DropIndex(
                name: "ix_admin_tickets_status_prazo_resposta",
                table: "admin_tickets");

            migrationBuilder.DropIndex(
                name: "ix_admin_ticket_mensagens_ticket_interno_criado",
                table: "admin_ticket_mensagens");

            migrationBuilder.DropColumn(
                name: "Nivel",
                table: "admin_tickets");

            migrationBuilder.DropColumn(
                name: "OrigemTicketId",
                table: "admin_tickets");

            migrationBuilder.DropColumn(
                name: "PrazoResolucao",
                table: "admin_tickets");

            migrationBuilder.DropColumn(
                name: "PrazoResposta",
                table: "admin_tickets");

            migrationBuilder.DropColumn(
                name: "PrimeiraRespostaEm",
                table: "admin_tickets");

            migrationBuilder.DropColumn(
                name: "ResolvidoEm",
                table: "admin_tickets");

            migrationBuilder.DropColumn(
                name: "SlaResolucaoViolado",
                table: "admin_tickets");

            migrationBuilder.DropColumn(
                name: "SlaRespostaViolado",
                table: "admin_tickets");

            migrationBuilder.DropColumn(
                name: "UltimoAlerta50PctEm",
                table: "admin_tickets");

            migrationBuilder.DropColumn(
                name: "UltimoAlerta80PctEm",
                table: "admin_tickets");

            migrationBuilder.DropColumn(
                name: "Interno",
                table: "admin_ticket_mensagens");

            migrationBuilder.CreateIndex(
                name: "IX_admin_tickets_AtendenteId",
                table: "admin_tickets",
                column: "AtendenteId");

            migrationBuilder.CreateIndex(
                name: "IX_admin_tickets_EmpresaId",
                table: "admin_tickets",
                column: "EmpresaId");

            migrationBuilder.CreateIndex(
                name: "IX_admin_ticket_mensagens_TicketId",
                table: "admin_ticket_mensagens",
                column: "TicketId");
        }
    }
}
