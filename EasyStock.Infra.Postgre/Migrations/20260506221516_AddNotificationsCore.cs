using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace EasyStock.Infra.Postgre.Migrations
{
    /// <inheritdoc />
    public partial class AddNotificationsCore : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<Guid>(
                name: "OutboxMensagemId",
                table: "notificacoes",
                type: "uuid",
                nullable: true);

            // IsSeedData/SeedRunLogs vieram de uma migration anterior vazia (AddSeedRunLogAndIsSeedData).
            // Aplicar idempotente para não falhar em ambientes onde já existem via DDL manual.
            migrationBuilder.Sql("ALTER TABLE empresas ADD COLUMN IF NOT EXISTS \"IsSeedData\" boolean NOT NULL DEFAULT false;");

            migrationBuilder.CreateTable(
                name: "notif_bloqueios",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    EmpresaId = table.Column<Guid>(type: "uuid", nullable: true),
                    Canal = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: true),
                    Motivo = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: false),
                    AtivadoEm = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    AtivadoPor = table.Column<string>(type: "character varying(256)", maxLength: 256, nullable: false),
                    ExpiraEm = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    RemovidoEm = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    RemovidoPor = table.Column<string>(type: "character varying(256)", maxLength: 256, nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_notif_bloqueios", x => x.Id);
                    table.ForeignKey(
                        name: "FK_notif_bloqueios_empresas_EmpresaId",
                        column: x => x.EmpresaId,
                        principalTable: "empresas",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "notif_configuracoes_canal",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    EmpresaId = table.Column<Guid>(type: "uuid", nullable: true),
                    Canal = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    ProviderAtivo = table.Column<string>(type: "character varying(40)", maxLength: 40, nullable: false),
                    CredenciaisCifradas = table.Column<byte[]>(type: "bytea", nullable: true),
                    LimiteDiarioPorUsuario = table.Column<int>(type: "integer", nullable: true),
                    JanelaPermitidaInicio = table.Column<TimeOnly>(type: "time without time zone", nullable: true),
                    JanelaPermitidaFim = table.Column<TimeOnly>(type: "time without time zone", nullable: true),
                    AtivoNoTenant = table.Column<bool>(type: "boolean", nullable: false),
                    AtualizadoEm = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    AtualizadoPor = table.Column<string>(type: "character varying(256)", maxLength: 256, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_notif_configuracoes_canal", x => x.Id);
                    table.ForeignKey(
                        name: "FK_notif_configuracoes_canal_empresas_EmpresaId",
                        column: x => x.EmpresaId,
                        principalTable: "empresas",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "notif_consentimentos",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    UsuarioId = table.Column<Guid>(type: "uuid", nullable: false),
                    Canal = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    Categoria = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    OptIn = table.Column<bool>(type: "boolean", nullable: false),
                    AtualizadoEm = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    AtualizadoPor = table.Column<string>(type: "character varying(256)", maxLength: 256, nullable: false),
                    IpOrigem = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: true),
                    MotivoOptOut = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_notif_consentimentos", x => x.Id);
                    table.ForeignKey(
                        name: "FK_notif_consentimentos_usuarios_UsuarioId",
                        column: x => x.UsuarioId,
                        principalTable: "usuarios",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "notif_eventos",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    Tipo = table.Column<string>(type: "character varying(40)", maxLength: 40, nullable: false),
                    EmpresaId = table.Column<Guid>(type: "uuid", nullable: false),
                    RefEntidadeId = table.Column<Guid>(type: "uuid", nullable: true),
                    PayloadJson = table.Column<string>(type: "jsonb", nullable: false),
                    OcorridoEm = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    ProcessadoEm = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    Status = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    CorrelationId = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    ErroProcessamento = table.Column<string>(type: "character varying(2000)", maxLength: 2000, nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_notif_eventos", x => x.Id);
                    table.ForeignKey(
                        name: "FK_notif_eventos_empresas_EmpresaId",
                        column: x => x.EmpresaId,
                        principalTable: "empresas",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "notif_preferencias_usuario",
                columns: table => new
                {
                    UsuarioId = table.Column<Guid>(type: "uuid", nullable: false),
                    RotinaCodigo = table.Column<string>(type: "character varying(120)", maxLength: 120, nullable: false),
                    EmpresaId = table.Column<Guid>(type: "uuid", nullable: false),
                    Habilitada = table.Column<bool>(type: "boolean", nullable: false),
                    CanalPreferido = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: true),
                    AtualizadaEm = table.Column<DateTime>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_notif_preferencias_usuario", x => new { x.UsuarioId, x.RotinaCodigo });
                    table.ForeignKey(
                        name: "FK_notif_preferencias_usuario_empresas_EmpresaId",
                        column: x => x.EmpresaId,
                        principalTable: "empresas",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_notif_preferencias_usuario_usuarios_UsuarioId",
                        column: x => x.UsuarioId,
                        principalTable: "usuarios",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "notif_rotinas",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    Codigo = table.Column<string>(type: "character varying(120)", maxLength: 120, nullable: false),
                    Nome = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    TipoEvento = table.Column<string>(type: "character varying(40)", maxLength: 40, nullable: false),
                    TriggerTipo = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    CronExpression = table.Column<string>(type: "character varying(120)", maxLength: 120, nullable: true),
                    ParametrosJson = table.Column<string>(type: "jsonb", nullable: false),
                    CanaisOrdemFallbackJson = table.Column<string>(type: "jsonb", nullable: false),
                    TemplateCodigo = table.Column<string>(type: "character varying(120)", maxLength: 120, nullable: false),
                    Categoria = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    Ativa = table.Column<bool>(type: "boolean", nullable: false),
                    EmpresaId = table.Column<Guid>(type: "uuid", nullable: true),
                    JanelaInicio = table.Column<TimeOnly>(type: "time without time zone", nullable: true),
                    JanelaFim = table.Column<TimeOnly>(type: "time without time zone", nullable: true),
                    RespeitarFusoLoja = table.Column<bool>(type: "boolean", nullable: false),
                    CriadaEm = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    AtualizadaEm = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    AtualizadaPor = table.Column<string>(type: "character varying(256)", maxLength: 256, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_notif_rotinas", x => x.Id);
                    table.ForeignKey(
                        name: "FK_notif_rotinas_empresas_EmpresaId",
                        column: x => x.EmpresaId,
                        principalTable: "empresas",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "notif_templates",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    Codigo = table.Column<string>(type: "character varying(120)", maxLength: 120, nullable: false),
                    Nome = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    Canal = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    TipoEvento = table.Column<string>(type: "character varying(40)", maxLength: 40, nullable: false),
                    AssuntoTemplate = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: false),
                    CorpoTemplate = table.Column<string>(type: "text", nullable: false),
                    Idioma = table.Column<string>(type: "character varying(10)", maxLength: 10, nullable: false),
                    Ativo = table.Column<bool>(type: "boolean", nullable: false),
                    Aprovado = table.Column<bool>(type: "boolean", nullable: false),
                    EmpresaId = table.Column<Guid>(type: "uuid", nullable: true),
                    Versao = table.Column<int>(type: "integer", nullable: false),
                    ChecksumSha256 = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    CriadoEm = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    AtualizadoEm = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    AtualizadoPor = table.Column<string>(type: "character varying(256)", maxLength: 256, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_notif_templates", x => x.Id);
                    table.ForeignKey(
                        name: "FK_notif_templates_empresas_EmpresaId",
                        column: x => x.EmpresaId,
                        principalTable: "empresas",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "notif_variaveis_template_catalogo",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    TipoEvento = table.Column<string>(type: "character varying(40)", maxLength: 40, nullable: false),
                    NomeVariavel = table.Column<string>(type: "character varying(80)", maxLength: 80, nullable: false),
                    Tipo = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    Descricao = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: false),
                    Exemplo = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_notif_variaveis_template_catalogo", x => x.Id);
                });

            // SeedRunLogs também é drift da migration vazia anterior — criar idempotente.
            migrationBuilder.Sql(@"
                CREATE TABLE IF NOT EXISTS ""SeedRunLogs"" (
                    ""Id"" uuid NOT NULL PRIMARY KEY,
                    ""AdminEmail"" text NOT NULL,
                    ""TipoSeed"" text NOT NULL,
                    ""Volume"" text NULL,
                    ""StartedAt"" timestamp with time zone NOT NULL,
                    ""CompletedAt"" timestamp with time zone NULL,
                    ""Status"" text NOT NULL,
                    ""EtapasJson"" text NULL,
                    ""BackupJson"" text NULL,
                    ""Erro"" text NULL,
                    ""Resumo"" text NULL
                );");

            migrationBuilder.CreateTable(
                name: "notif_outbox_mensagens",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    EventoId = table.Column<Guid>(type: "uuid", nullable: false),
                    RotinaId = table.Column<Guid>(type: "uuid", nullable: true),
                    TemplateId = table.Column<Guid>(type: "uuid", nullable: false),
                    EmpresaId = table.Column<Guid>(type: "uuid", nullable: false),
                    UsuarioDestinoId = table.Column<Guid>(type: "uuid", nullable: true),
                    Canal = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    Destinatario = table.Column<string>(type: "character varying(320)", maxLength: 320, nullable: false),
                    AssuntoRenderizado = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: false),
                    CorpoRenderizado = table.Column<string>(type: "text", nullable: false),
                    Status = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    Tentativas = table.Column<int>(type: "integer", nullable: false),
                    MaxTentativas = table.Column<int>(type: "integer", nullable: false),
                    ProximaTentativaEm = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    EnviadoEm = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    ProviderUsado = table.Column<string>(type: "character varying(40)", maxLength: 40, nullable: true),
                    ErroUltimaTentativa = table.Column<string>(type: "character varying(2000)", maxLength: 2000, nullable: true),
                    IdempotencyKey = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    TenantTimezone = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    CanaisFallbackRestantesJson = table.Column<string>(type: "jsonb", nullable: false),
                    Categoria = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    CriadoEm = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    ShardKey = table.Column<int>(type: "integer", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_notif_outbox_mensagens", x => x.Id);
                    table.ForeignKey(
                        name: "FK_notif_outbox_mensagens_empresas_EmpresaId",
                        column: x => x.EmpresaId,
                        principalTable: "empresas",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_notif_outbox_mensagens_notif_eventos_EventoId",
                        column: x => x.EventoId,
                        principalTable: "notif_eventos",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_notif_outbox_mensagens_notif_rotinas_RotinaId",
                        column: x => x.RotinaId,
                        principalTable: "notif_rotinas",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.SetNull);
                    table.ForeignKey(
                        name: "FK_notif_outbox_mensagens_notif_templates_TemplateId",
                        column: x => x.TemplateId,
                        principalTable: "notif_templates",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_notif_outbox_mensagens_usuarios_UsuarioDestinoId",
                        column: x => x.UsuarioDestinoId,
                        principalTable: "usuarios",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.SetNull);
                });

            migrationBuilder.CreateTable(
                name: "notif_logs_envio",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    OutboxMensagemId = table.Column<Guid>(type: "uuid", nullable: false),
                    Tentativa = table.Column<int>(type: "integer", nullable: false),
                    Canal = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    Provider = table.Column<string>(type: "character varying(40)", maxLength: 40, nullable: false),
                    StatusHttp = table.Column<int>(type: "integer", nullable: true),
                    RespostaProviderJson = table.Column<string>(type: "jsonb", nullable: true),
                    DuracaoMs = table.Column<long>(type: "bigint", nullable: false),
                    OcorridoEm = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    ErroDetalhado = table.Column<string>(type: "character varying(4000)", maxLength: 4000, nullable: true),
                    BypassConsentimento = table.Column<bool>(type: "boolean", nullable: false),
                    Sucesso = table.Column<bool>(type: "boolean", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_notif_logs_envio", x => x.Id);
                    table.ForeignKey(
                        name: "FK_notif_logs_envio_notif_outbox_mensagens_OutboxMensagemId",
                        column: x => x.OutboxMensagemId,
                        principalTable: "notif_outbox_mensagens",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_notificacoes_OutboxMensagemId",
                table: "notificacoes",
                column: "OutboxMensagemId");

            migrationBuilder.CreateIndex(
                name: "IX_notif_bloqueios_EmpresaId_Canal_RemovidoEm",
                table: "notif_bloqueios",
                columns: new[] { "EmpresaId", "Canal", "RemovidoEm" });

            migrationBuilder.CreateIndex(
                name: "IX_notif_configuracoes_canal_EmpresaId_Canal",
                table: "notif_configuracoes_canal",
                columns: new[] { "EmpresaId", "Canal" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_notif_consentimentos_UsuarioId_Canal_Categoria_AtualizadoEm",
                table: "notif_consentimentos",
                columns: new[] { "UsuarioId", "Canal", "Categoria", "AtualizadoEm" });

            migrationBuilder.CreateIndex(
                name: "IX_notif_eventos_EmpresaId_Tipo",
                table: "notif_eventos",
                columns: new[] { "EmpresaId", "Tipo" });

            migrationBuilder.CreateIndex(
                name: "IX_notif_eventos_Status_OcorridoEm",
                table: "notif_eventos",
                columns: new[] { "Status", "OcorridoEm" });

            migrationBuilder.CreateIndex(
                name: "IX_notif_logs_envio_OcorridoEm",
                table: "notif_logs_envio",
                column: "OcorridoEm");

            migrationBuilder.CreateIndex(
                name: "IX_notif_logs_envio_OutboxMensagemId_Tentativa",
                table: "notif_logs_envio",
                columns: new[] { "OutboxMensagemId", "Tentativa" });

            migrationBuilder.CreateIndex(
                name: "IX_notif_outbox_mensagens_EmpresaId_CriadoEm",
                table: "notif_outbox_mensagens",
                columns: new[] { "EmpresaId", "CriadoEm" });

            migrationBuilder.CreateIndex(
                name: "IX_notif_outbox_mensagens_EventoId",
                table: "notif_outbox_mensagens",
                column: "EventoId");

            migrationBuilder.CreateIndex(
                name: "IX_notif_outbox_mensagens_IdempotencyKey",
                table: "notif_outbox_mensagens",
                column: "IdempotencyKey",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_notif_outbox_mensagens_RotinaId",
                table: "notif_outbox_mensagens",
                column: "RotinaId");

            migrationBuilder.CreateIndex(
                name: "IX_notif_outbox_mensagens_ShardKey_Status_ProximaTentativaEm",
                table: "notif_outbox_mensagens",
                columns: new[] { "ShardKey", "Status", "ProximaTentativaEm" });

            migrationBuilder.CreateIndex(
                name: "IX_notif_outbox_mensagens_Status_ProximaTentativaEm",
                table: "notif_outbox_mensagens",
                columns: new[] { "Status", "ProximaTentativaEm" });

            migrationBuilder.CreateIndex(
                name: "IX_notif_outbox_mensagens_TemplateId",
                table: "notif_outbox_mensagens",
                column: "TemplateId");

            migrationBuilder.CreateIndex(
                name: "IX_notif_outbox_mensagens_UsuarioDestinoId",
                table: "notif_outbox_mensagens",
                column: "UsuarioDestinoId");

            migrationBuilder.CreateIndex(
                name: "IX_notif_preferencias_usuario_EmpresaId_RotinaCodigo",
                table: "notif_preferencias_usuario",
                columns: new[] { "EmpresaId", "RotinaCodigo" });

            migrationBuilder.CreateIndex(
                name: "IX_notif_rotinas_Ativa_TipoEvento",
                table: "notif_rotinas",
                columns: new[] { "Ativa", "TipoEvento" });

            migrationBuilder.CreateIndex(
                name: "IX_notif_rotinas_Codigo_EmpresaId",
                table: "notif_rotinas",
                columns: new[] { "Codigo", "EmpresaId" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_notif_rotinas_EmpresaId",
                table: "notif_rotinas",
                column: "EmpresaId");

            migrationBuilder.CreateIndex(
                name: "IX_notif_templates_Codigo_EmpresaId_Versao",
                table: "notif_templates",
                columns: new[] { "Codigo", "EmpresaId", "Versao" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_notif_templates_EmpresaId",
                table: "notif_templates",
                column: "EmpresaId");

            migrationBuilder.CreateIndex(
                name: "IX_notif_templates_TipoEvento_Canal_Ativo",
                table: "notif_templates",
                columns: new[] { "TipoEvento", "Canal", "Ativo" });

            migrationBuilder.CreateIndex(
                name: "IX_notif_variaveis_template_catalogo_TipoEvento_NomeVariavel",
                table: "notif_variaveis_template_catalogo",
                columns: new[] { "TipoEvento", "NomeVariavel" },
                unique: true);

            // pg_notify trigger: acorda o OutboxListenService no Worker assim que uma mensagem
            // pendente é inserida. Sem isso, dispatcher dependeria apenas de polling (10s).
            // Payload é o Id da mensagem para o listener pular polling e ir direto.
            migrationBuilder.Sql(@"
                CREATE OR REPLACE FUNCTION notif_outbox_notify_fn() RETURNS trigger AS $$
                BEGIN
                    IF NEW.""Status"" = 'Pendente' THEN
                        PERFORM pg_notify('notif_outbox', NEW.""Id""::text);
                    END IF;
                    RETURN NEW;
                END;
                $$ LANGUAGE plpgsql;

                DROP TRIGGER IF EXISTS notif_outbox_notify_trg ON notif_outbox_mensagens;
                CREATE TRIGGER notif_outbox_notify_trg
                    AFTER INSERT ON notif_outbox_mensagens
                    FOR EACH ROW
                    EXECUTE FUNCTION notif_outbox_notify_fn();");

            SeedCatalogoVariaveis(migrationBuilder);
            SeedConfiguracoesCanalGlobais(migrationBuilder);
        }

        private static void SeedCatalogoVariaveis(MigrationBuilder mb)
        {
            // Catálogo de variáveis disponíveis por TipoEvento — alimenta o painel admin.
            // Idempotente via UNIQUE(TipoEvento, NomeVariavel).
            var rows = new (string TipoEvento, string Nome, string Tipo, string Descricao, string Exemplo)[]
            {
                ("ProdutoVencendo", "produto.nome", "string", "Nome do produto", "Leite Integral 1L"),
                ("ProdutoVencendo", "produto.validade", "date", "Data de validade", "2026-06-15"),
                ("ProdutoVencendo", "produto.codigoInterno", "string", "Código interno do produto", "PRD-0001"),
                ("ProdutoVencendo", "produto.diasRestantes", "number", "Dias até vencer", "7"),
                ("ProdutoVencendo", "loja.nome", "string", "Loja onde o produto está", "Clube do Fone"),

                ("ProdutoVencido", "produto.nome", "string", "Nome do produto", "Iogurte Natural"),
                ("ProdutoVencido", "produto.validade", "date", "Data de validade", "2026-04-30"),
                ("ProdutoVencido", "produto.diasVencido", "number", "Dias após o vencimento", "3"),
                ("ProdutoVencido", "loja.nome", "string", "Loja onde o produto está", "Cantina da Thati"),

                ("TarefaPendente", "tarefa.titulo", "string", "Título da tarefa", "Conferir caixa"),
                ("TarefaPendente", "tarefa.prazo", "date", "Prazo de entrega", "2026-05-08"),
                ("TarefaPendente", "tarefa.descricao", "string", "Descrição completa", "Fechar movimento de caixa..."),
                ("TarefaPendente", "usuario.nome", "string", "Responsável", "Felipe"),

                ("ResetSenha", "usuario.nome", "string", "Nome do usuário", "Felipe"),
                ("ResetSenha", "urlReset", "string", "URL com token de reset", "https://app.easystock.com/reset?token=..."),
                ("ResetSenha", "expiracaoMinutos", "number", "Minutos até o token expirar", "30"),

                ("AssinaturaExpirando", "usuario.nome", "string", "Nome do admin do tenant", "Felipe"),
                ("AssinaturaExpirando", "plano.nome", "string", "Nome do plano", "Pro Mensal"),
                ("AssinaturaExpirando", "dataVencimento", "date", "Vencimento", "2026-05-15"),
                ("AssinaturaExpirando", "valorRenovacao", "string", "Valor formatado", "R$ 99,90"),
                ("AssinaturaExpirando", "urlPagamento", "string", "URL para pagamento", "https://app.easystock.com/billing"),

                ("AssinaturaExpirada", "usuario.nome", "string", "Nome do admin", "Felipe"),
                ("AssinaturaExpirada", "plano.nome", "string", "Nome do plano", "Pro Mensal"),
                ("AssinaturaExpirada", "urlPagamento", "string", "URL para regularizar", "https://app.easystock.com/billing"),

                ("BroadcastSuperAdmin", "titulo", "string", "Título do comunicado", "Manutenção programada"),
                ("BroadcastSuperAdmin", "conteudo", "string", "Conteúdo livre (HTML permitido)", "<p>Olá!</p>"),

                ("ConfirmacaoEmail", "usuario.nome", "string", "Nome do usuário", "Felipe"),
                ("ConfirmacaoEmail", "urlConfirmacao", "string", "URL de confirmação", "https://app.easystock.com/confirm?token=..."),

                ("AlertaEstoqueCritico", "produto.nome", "string", "Nome do produto", "Café 500g"),
                ("AlertaEstoqueCritico", "quantidadeAtual", "number", "Quantidade em estoque", "2"),
                ("AlertaEstoqueCritico", "estoqueMinimo", "number", "Limiar mínimo configurado", "10"),
                ("AlertaEstoqueCritico", "loja.nome", "string", "Loja afetada", "Clube do Fone"),
            };

            foreach (var r in rows)
            {
                mb.Sql($@"
                    INSERT INTO notif_variaveis_template_catalogo (""Id"", ""TipoEvento"", ""NomeVariavel"", ""Tipo"", ""Descricao"", ""Exemplo"")
                    VALUES (gen_random_uuid(), '{r.TipoEvento}', '{r.Nome}', '{r.Tipo}', '{Escape(r.Descricao)}', '{Escape(r.Exemplo)}')
                    ON CONFLICT (""TipoEvento"", ""NomeVariavel"") DO NOTHING;");
            }
        }

        private static void SeedConfiguracoesCanalGlobais(MigrationBuilder mb)
        {
            // ConfiguracaoCanal global default (EmpresaId NULL).
            // Email usa "smtp" para reusar SmtpEmailService existente.
            // Outros canais começam como "stub" — admin troca para Twilio/Meta/Zenvia depois.
            // Idempotente via UNIQUE(EmpresaId, Canal) — porém UNIQUE em coluna nullable
            // permite múltiplos NULL no Postgres. Por isso usamos NOT EXISTS antes do INSERT.
            var defaults = new (string Canal, string Provider)[]
            {
                ("Email", "smtp"),
                ("Sms", "stub"),
                ("WhatsApp", "stub"),
                ("InApp", "inapp"),
            };

            foreach (var d in defaults)
            {
                mb.Sql($@"
                    INSERT INTO notif_configuracoes_canal
                        (""Id"", ""EmpresaId"", ""Canal"", ""ProviderAtivo"", ""AtivoNoTenant"", ""AtualizadoEm"", ""AtualizadoPor"")
                    SELECT gen_random_uuid(), NULL, '{d.Canal}', '{d.Provider}', true, NOW() AT TIME ZONE 'UTC', 'system'
                    WHERE NOT EXISTS (
                        SELECT 1 FROM notif_configuracoes_canal
                        WHERE ""EmpresaId"" IS NULL AND ""Canal"" = '{d.Canal}'
                    );");
            }
        }

        private static string Escape(string s) => s.Replace("'", "''");

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            // Trigger e função primeiro (depende de notif_outbox_mensagens).
            migrationBuilder.Sql("DROP TRIGGER IF EXISTS notif_outbox_notify_trg ON notif_outbox_mensagens;");
            migrationBuilder.Sql("DROP FUNCTION IF EXISTS notif_outbox_notify_fn();");

            migrationBuilder.DropTable(name: "notif_bloqueios");
            migrationBuilder.DropTable(name: "notif_configuracoes_canal");
            migrationBuilder.DropTable(name: "notif_consentimentos");
            migrationBuilder.DropTable(name: "notif_logs_envio");
            migrationBuilder.DropTable(name: "notif_preferencias_usuario");
            migrationBuilder.DropTable(name: "notif_variaveis_template_catalogo");
            migrationBuilder.DropTable(name: "notif_outbox_mensagens");
            migrationBuilder.DropTable(name: "notif_eventos");
            migrationBuilder.DropTable(name: "notif_rotinas");
            migrationBuilder.DropTable(name: "notif_templates");

            migrationBuilder.DropIndex(
                name: "IX_notificacoes_OutboxMensagemId",
                table: "notificacoes");

            migrationBuilder.DropColumn(
                name: "OutboxMensagemId",
                table: "notificacoes");

            // IsSeedData e SeedRunLogs NÃO são removidos: pertencem à migration anterior
            // (AddSeedRunLogAndIsSeedData), aqui apenas regularizamos o drift.
        }
    }
}
