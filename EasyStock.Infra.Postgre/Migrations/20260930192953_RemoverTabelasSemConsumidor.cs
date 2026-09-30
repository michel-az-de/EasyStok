using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace EasyStock.Infra.Postgre.Migrations
{
    /// <inheritdoc />
    public partial class RemoverTabelasSemConsumidor : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_faturas_admin_tickets_TicketRelacionadoId",
                table: "faturas");

            migrationBuilder.DropTable(
                name: "admin_acessos_pii_logs");

            migrationBuilder.DropTable(
                name: "admin_impersonation_logs");

            migrationBuilder.DropTable(
                name: "admin_notas_tenant");

            migrationBuilder.DropTable(
                name: "admin_ticket_tecnico_meta");

            migrationBuilder.DropTable(
                name: "anuncios_ia");

            migrationBuilder.DropTable(
                name: "apk_releases");

            migrationBuilder.DropTable(
                name: "banner_confirmacoes");

            migrationBuilder.DropTable(
                name: "empresa_configuracao_fiscal");

            migrationBuilder.DropTable(
                name: "faq_feedbacks");

            migrationBuilder.DropTable(
                name: "faq_visualizacoes");

            migrationBuilder.DropTable(
                name: "leads_publicos");

            migrationBuilder.DropTable(
                name: "sla_configuracao");

            migrationBuilder.DropTable(
                name: "ticket_anexos");

            migrationBuilder.DropTable(
                name: "ticket_historico");

            migrationBuilder.DropTable(
                name: "banners");

            migrationBuilder.DropTable(
                name: "faq_itens");

            migrationBuilder.DropTable(
                name: "admin_ticket_mensagens");

            migrationBuilder.DropTable(
                name: "faq_categorias");

            migrationBuilder.DropTable(
                name: "admin_tickets");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "admin_acessos_pii_logs",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    AdminEmail = table.Column<string>(type: "character varying(256)", maxLength: 256, nullable: false),
                    Campo = table.Column<string>(type: "character varying(40)", maxLength: 40, nullable: false),
                    CriadoEm = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    EntidadeId = table.Column<Guid>(type: "uuid", nullable: false),
                    EntidadeTipo = table.Column<string>(type: "character varying(40)", maxLength: 40, nullable: false),
                    Ip = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: true),
                    Motivo = table.Column<string>(type: "character varying(1000)", maxLength: 1000, nullable: true),
                    TenantId = table.Column<Guid>(type: "uuid", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_admin_acessos_pii_logs", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "admin_impersonation_logs",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    AdminUsuarioId = table.Column<Guid>(type: "uuid", nullable: false),
                    EmpresaId = table.Column<Guid>(type: "uuid", nullable: false),
                    FimEm = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    InicioEm = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    Ip = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_admin_impersonation_logs", x => x.Id);
                    table.ForeignKey(
                        name: "FK_admin_impersonation_logs_empresas_EmpresaId",
                        column: x => x.EmpresaId,
                        principalTable: "empresas",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_admin_impersonation_logs_usuarios_AdminUsuarioId",
                        column: x => x.AdminUsuarioId,
                        principalTable: "usuarios",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "admin_notas_tenant",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    AlteradoEm = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    AutorAdminId = table.Column<Guid>(type: "uuid", nullable: false),
                    AutorEmail = table.Column<string>(type: "character varying(256)", maxLength: 256, nullable: false),
                    CriadoEm = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    ExcluidoEm = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    TenantId = table.Column<Guid>(type: "uuid", nullable: false),
                    Texto = table.Column<string>(type: "character varying(2000)", maxLength: 2000, nullable: false),
                    Tipo = table.Column<int>(type: "integer", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_admin_notas_tenant", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "admin_tickets",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    AtendenteId = table.Column<Guid>(type: "uuid", nullable: true),
                    CriadoPorId = table.Column<Guid>(type: "uuid", nullable: true),
                    EmpresaId = table.Column<Guid>(type: "uuid", nullable: false),
                    FaturaId = table.Column<Guid>(type: "uuid", nullable: true),
                    OrigemTicketId = table.Column<Guid>(type: "uuid", nullable: true),
                    PedidoId = table.Column<Guid>(type: "uuid", nullable: true),
                    AlteradoEm = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    AvaliadoEm = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    CanalOrigem = table.Column<int>(type: "integer", nullable: false),
                    Categoria = table.Column<string>(type: "character varying(30)", maxLength: 30, nullable: false),
                    ComentarioCsat = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: true),
                    ConviteCsatEnviadoEm = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    CriadoEm = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    Descricao = table.Column<string>(type: "character varying(4000)", maxLength: 4000, nullable: false),
                    Nivel = table.Column<string>(type: "character varying(4)", maxLength: 4, nullable: false, defaultValue: "N1"),
                    NotaCsat = table.Column<int>(type: "integer", nullable: true),
                    PrazoResolucao = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    PrazoResposta = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    PrimeiraRespostaEm = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    Prioridade = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    ResolvidoEm = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    SlaResolucaoViolado = table.Column<bool>(type: "boolean", nullable: false, defaultValue: false),
                    SlaRespostaViolado = table.Column<bool>(type: "boolean", nullable: false, defaultValue: false),
                    Status = table.Column<string>(type: "character varying(30)", maxLength: 30, nullable: false),
                    Titulo = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    UltimoAlerta50PctEm = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    UltimoAlerta80PctEm = table.Column<DateTime>(type: "timestamp with time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_admin_tickets", x => x.Id);
                    table.CheckConstraint("ck_admin_tickets_nota_csat_range", "\"NotaCsat\" IS NULL OR (\"NotaCsat\" BETWEEN 1 AND 5)");
                    table.ForeignKey(
                        name: "FK_admin_tickets_admin_tickets_OrigemTicketId",
                        column: x => x.OrigemTicketId,
                        principalTable: "admin_tickets",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.SetNull);
                    table.ForeignKey(
                        name: "FK_admin_tickets_empresas_EmpresaId",
                        column: x => x.EmpresaId,
                        principalTable: "empresas",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_admin_tickets_faturas_FaturaId",
                        column: x => x.FaturaId,
                        principalTable: "faturas",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.SetNull);
                    table.ForeignKey(
                        name: "FK_admin_tickets_pedidos_PedidoId",
                        column: x => x.PedidoId,
                        principalTable: "pedidos",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.SetNull);
                    table.ForeignKey(
                        name: "FK_admin_tickets_usuarios_AtendenteId",
                        column: x => x.AtendenteId,
                        principalTable: "usuarios",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.SetNull);
                    table.ForeignKey(
                        name: "FK_admin_tickets_usuarios_CriadoPorId",
                        column: x => x.CriadoPorId,
                        principalTable: "usuarios",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.SetNull);
                });

            migrationBuilder.CreateTable(
                name: "anuncios_ia",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    ProdutoId = table.Column<Guid>(type: "uuid", nullable: false),
                    Conteudo = table.Column<string>(type: "text", nullable: false),
                    CriadoEm = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    EmpresaId = table.Column<Guid>(type: "uuid", nullable: false),
                    InstrucoesUsadas = table.Column<string>(type: "text", nullable: true),
                    ProdutoVariacaoId = table.Column<Guid>(type: "uuid", nullable: true),
                    Salvo = table.Column<bool>(type: "boolean", nullable: false),
                    Titulo = table.Column<string>(type: "character varying(255)", maxLength: 255, nullable: false),
                    TokensConsumidos = table.Column<int>(type: "integer", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_anuncios_ia", x => x.Id);
                    table.ForeignKey(
                        name: "FK_anuncios_ia_produtos_ProdutoId",
                        column: x => x.ProdutoId,
                        principalTable: "produtos",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "apk_releases",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    app_id = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    criado_em = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    criado_por_id = table.Column<Guid>(type: "uuid", nullable: true),
                    file_content = table.Column<byte[]>(type: "bytea", nullable: false),
                    file_size_bytes = table.Column<long>(type: "bigint", nullable: false),
                    is_active = table.Column<bool>(type: "boolean", nullable: false),
                    is_canary_only = table.Column<bool>(type: "boolean", nullable: false),
                    release_notes = table.Column<string>(type: "character varying(2048)", maxLength: 2048, nullable: true),
                    sha256 = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    version = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_apk_releases", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "banners",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    AlturaPx = table.Column<int>(type: "integer", nullable: true),
                    Ativo = table.Column<bool>(type: "boolean", nullable: false),
                    AtualizadoEm = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    Corpo = table.Column<string>(type: "character varying(4000)", maxLength: 4000, nullable: true),
                    CriadoEm = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    CriadoPorUsuarioId = table.Column<Guid>(type: "uuid", nullable: true),
                    ExigeConfirmacao = table.Column<bool>(type: "boolean", nullable: false),
                    FimEm = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    ImagemStorageKey = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: true),
                    ImagemUrl = table.Column<string>(type: "character varying(1000)", maxLength: 1000, nullable: true),
                    InicioEm = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    LarguraPx = table.Column<int>(type: "integer", nullable: true),
                    LinkAtivo = table.Column<bool>(type: "boolean", nullable: false),
                    LinkUrl = table.Column<string>(type: "character varying(2000)", maxLength: 2000, nullable: true),
                    NotificadoEm = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    NotificarAoPublicar = table.Column<bool>(type: "boolean", nullable: false),
                    NovaAba = table.Column<bool>(type: "boolean", nullable: false),
                    Prioridade = table.Column<int>(type: "integer", nullable: false),
                    TamanhoModo = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    Tipo = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    TituloInterno = table.Column<string>(type: "character varying(120)", maxLength: 120, nullable: false),
                    TooltipAtivo = table.Column<bool>(type: "boolean", nullable: false),
                    TooltipTexto = table.Column<string>(type: "character varying(300)", maxLength: 300, nullable: true),
                    VisualizacaoUnica = table.Column<bool>(type: "boolean", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_banners", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "empresa_configuracao_fiscal",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    EmpresaId = table.Column<Guid>(type: "uuid", nullable: false),
                    AlteradoEm = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    Ambiente = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    CertificadoCredencialId = table.Column<Guid>(type: "uuid", nullable: true),
                    CriadoEm = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    CscId = table.Column<string>(type: "character varying(10)", maxLength: 10, nullable: true),
                    CscToken = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: true),
                    endereco = table.Column<string>(type: "jsonb", nullable: true),
                    Habilitada = table.Column<bool>(type: "boolean", nullable: false, defaultValue: false),
                    InscricaoEstadual = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: true),
                    InscricaoMunicipal = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: true),
                    ProvedorPreferido = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false, defaultValue: "mock"),
                    ProximoNumeroNfce = table.Column<long>(type: "bigint", nullable: false, defaultValue: 1L),
                    RegimeTributario = table.Column<string>(type: "character varying(40)", maxLength: 40, nullable: false),
                    SerieNfce = table.Column<short>(type: "smallint", nullable: false, defaultValue: (short)1),
                    xmin = table.Column<uint>(type: "xid", rowVersion: true, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_empresa_configuracao_fiscal", x => x.Id);
                    table.ForeignKey(
                        name: "FK_empresa_configuracao_fiscal_empresas_EmpresaId",
                        column: x => x.EmpresaId,
                        principalTable: "empresas",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "faq_categorias",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    AtualizadoEm = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    CriadoEm = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    Descricao = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: true),
                    Icone = table.Column<string>(type: "character varying(60)", maxLength: 60, nullable: true),
                    Nome = table.Column<string>(type: "character varying(80)", maxLength: 80, nullable: false),
                    Ordem = table.Column<int>(type: "integer", nullable: false),
                    Publica = table.Column<bool>(type: "boolean", nullable: false, defaultValue: true),
                    Slug = table.Column<string>(type: "character varying(80)", maxLength: 80, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_faq_categorias", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "leads_publicos",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    ConsentimentoLgpd = table.Column<bool>(type: "boolean", nullable: false),
                    CriadoEm = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    Email = table.Column<string>(type: "character varying(255)", maxLength: 255, nullable: false),
                    Empresa = table.Column<string>(type: "character varying(150)", maxLength: 150, nullable: true),
                    EmpresaCriadaId = table.Column<Guid>(type: "uuid", nullable: true),
                    IpOrigem = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: true),
                    Mensagem = table.Column<string>(type: "text", nullable: true),
                    Nome = table.Column<string>(type: "character varying(150)", maxLength: 150, nullable: false),
                    Origem = table.Column<int>(type: "integer", nullable: false),
                    ProcessadoEm = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    ReceberNewsletter = table.Column<bool>(type: "boolean", nullable: false),
                    Telefone = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: true),
                    TicketGeradoId = table.Column<Guid>(type: "uuid", nullable: true),
                    TipoNegocio = table.Column<string>(type: "character varying(80)", maxLength: 80, nullable: true),
                    UserAgent = table.Column<string>(type: "character varying(512)", maxLength: 512, nullable: true),
                    UtmCampaign = table.Column<string>(type: "character varying(120)", maxLength: 120, nullable: true),
                    UtmMedium = table.Column<string>(type: "character varying(120)", maxLength: 120, nullable: true),
                    UtmSource = table.Column<string>(type: "character varying(120)", maxLength: 120, nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_leads_publicos", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "sla_configuracao",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    EmpresaId = table.Column<Guid>(type: "uuid", nullable: true),
                    PlanoId = table.Column<Guid>(type: "uuid", nullable: true),
                    AlteradoEm = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    CriadoEm = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    HorarioComercialApenas = table.Column<bool>(type: "boolean", nullable: false, defaultValue: false),
                    MinutosResolucao = table.Column<int>(type: "integer", nullable: false),
                    MinutosResposta = table.Column<int>(type: "integer", nullable: false),
                    Prioridade = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false)
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
                name: "admin_ticket_mensagens",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    AutorId = table.Column<Guid>(type: "uuid", nullable: false),
                    TicketId = table.Column<Guid>(type: "uuid", nullable: false),
                    Conteudo = table.Column<string>(type: "character varying(8000)", maxLength: 8000, nullable: false),
                    CriadoEm = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    Interno = table.Column<bool>(type: "boolean", nullable: false, defaultValue: false),
                    IsAdmin = table.Column<bool>(type: "boolean", nullable: false),
                    LidoPeloAdmin = table.Column<bool>(type: "boolean", nullable: false, defaultValue: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_admin_ticket_mensagens", x => x.Id);
                    table.ForeignKey(
                        name: "FK_admin_ticket_mensagens_admin_tickets_TicketId",
                        column: x => x.TicketId,
                        principalTable: "admin_tickets",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_admin_ticket_mensagens_usuarios_AutorId",
                        column: x => x.AutorId,
                        principalTable: "usuarios",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "admin_ticket_tecnico_meta",
                columns: table => new
                {
                    TicketId = table.Column<Guid>(type: "uuid", nullable: false),
                    ComponenteAfetado = table.Column<string>(type: "character varying(120)", maxLength: 120, nullable: true),
                    CriadoEm = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    FixVersion = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: true),
                    ResolvidoEm = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    SeveridadeTecnica = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    StackTrace = table.Column<string>(type: "text", nullable: true)
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
                name: "ticket_historico",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    AutorId = table.Column<Guid>(type: "uuid", nullable: true),
                    TicketId = table.Column<Guid>(type: "uuid", nullable: false),
                    Acao = table.Column<string>(type: "character varying(40)", maxLength: 40, nullable: false),
                    CriadoEm = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    MetadadosJson = table.Column<string>(type: "jsonb", nullable: true),
                    ValorAntes = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: true),
                    ValorDepois = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: true)
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

            migrationBuilder.CreateTable(
                name: "banner_confirmacoes",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    BannerId = table.Column<Guid>(type: "uuid", nullable: false),
                    RegistradoEm = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    Tipo = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    UsuarioId = table.Column<Guid>(type: "uuid", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_banner_confirmacoes", x => x.Id);
                    table.ForeignKey(
                        name: "FK_banner_confirmacoes_banners_BannerId",
                        column: x => x.BannerId,
                        principalTable: "banners",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "faq_itens",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    CategoriaId = table.Column<Guid>(type: "uuid", nullable: false),
                    AtualizadoEm = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    AutorId = table.Column<Guid>(type: "uuid", nullable: true),
                    Conteudo = table.Column<string>(type: "character varying(20000)", maxLength: 20000, nullable: false),
                    ConteudoBusca = table.Column<string>(type: "character varying(20000)", maxLength: 20000, nullable: false),
                    CriadoEm = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    NaoUtilCount = table.Column<int>(type: "integer", nullable: false, defaultValue: 0),
                    Ordem = table.Column<int>(type: "integer", nullable: false),
                    PublicadoEm = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    Slug = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    Status = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    TagsCsv = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: false, defaultValue: ""),
                    Titulo = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    UtilCount = table.Column<int>(type: "integer", nullable: false, defaultValue: 0),
                    Visualizacoes = table.Column<int>(type: "integer", nullable: false, defaultValue: 0)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_faq_itens", x => x.Id);
                    table.ForeignKey(
                        name: "FK_faq_itens_faq_categorias_CategoriaId",
                        column: x => x.CategoriaId,
                        principalTable: "faq_categorias",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "ticket_anexos",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    EnviadoPorId = table.Column<Guid>(type: "uuid", nullable: false),
                    MensagemId = table.Column<Guid>(type: "uuid", nullable: true),
                    TicketId = table.Column<Guid>(type: "uuid", nullable: false),
                    ContentType = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    CriadoEm = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    IsAdmin = table.Column<bool>(type: "boolean", nullable: false, defaultValue: false),
                    IsPublico = table.Column<bool>(type: "boolean", nullable: false, defaultValue: false),
                    NomeArquivo = table.Column<string>(type: "character varying(255)", maxLength: 255, nullable: false),
                    StorageKey = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: false),
                    TamanhoBytes = table.Column<long>(type: "bigint", nullable: false),
                    Url = table.Column<string>(type: "character varying(1000)", maxLength: 1000, nullable: false)
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
                name: "faq_feedbacks",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    ItemId = table.Column<Guid>(type: "uuid", nullable: false),
                    Comentario = table.Column<string>(type: "character varying(1000)", maxLength: 1000, nullable: true),
                    CriadoEm = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    IpHash = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    Util = table.Column<bool>(type: "boolean", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_faq_feedbacks", x => x.Id);
                    table.ForeignKey(
                        name: "FK_faq_feedbacks_faq_itens_ItemId",
                        column: x => x.ItemId,
                        principalTable: "faq_itens",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "faq_visualizacoes",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    ItemId = table.Column<Guid>(type: "uuid", nullable: false),
                    CriadoEm = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    IpHash = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    Origem = table.Column<string>(type: "character varying(40)", maxLength: 40, nullable: true),
                    Termo = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_faq_visualizacoes", x => x.Id);
                    table.ForeignKey(
                        name: "FK_faq_visualizacoes_faq_itens_ItemId",
                        column: x => x.ItemId,
                        principalTable: "faq_itens",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_admin_acessos_pii_logs_AdminEmail",
                table: "admin_acessos_pii_logs",
                column: "AdminEmail");

            migrationBuilder.CreateIndex(
                name: "IX_admin_acessos_pii_logs_CriadoEm",
                table: "admin_acessos_pii_logs",
                column: "CriadoEm");

            migrationBuilder.CreateIndex(
                name: "ix_admin_acessos_pii_logs_tenant_criado",
                table: "admin_acessos_pii_logs",
                columns: new[] { "TenantId", "CriadoEm" });

            migrationBuilder.CreateIndex(
                name: "IX_admin_acessos_pii_logs_TenantId_EntidadeId",
                table: "admin_acessos_pii_logs",
                columns: new[] { "TenantId", "EntidadeId" });

            migrationBuilder.CreateIndex(
                name: "IX_admin_impersonation_logs_AdminUsuarioId",
                table: "admin_impersonation_logs",
                column: "AdminUsuarioId");

            migrationBuilder.CreateIndex(
                name: "IX_admin_impersonation_logs_EmpresaId",
                table: "admin_impersonation_logs",
                column: "EmpresaId");

            migrationBuilder.CreateIndex(
                name: "IX_admin_notas_tenant_TenantId_CriadoEm",
                table: "admin_notas_tenant",
                columns: new[] { "TenantId", "CriadoEm" });

            migrationBuilder.CreateIndex(
                name: "IX_admin_ticket_mensagens_AutorId",
                table: "admin_ticket_mensagens",
                column: "AutorId");

            migrationBuilder.CreateIndex(
                name: "ix_admin_ticket_mensagens_ticket_interno_criado",
                table: "admin_ticket_mensagens",
                columns: new[] { "TicketId", "Interno", "CriadoEm" });

            migrationBuilder.CreateIndex(
                name: "ix_admin_tickets_atendente_status",
                table: "admin_tickets",
                columns: new[] { "AtendenteId", "Status" });

            migrationBuilder.CreateIndex(
                name: "ix_admin_tickets_criado_por_id",
                table: "admin_tickets",
                column: "CriadoPorId");

            migrationBuilder.CreateIndex(
                name: "ix_admin_tickets_empresa_status_prioridade",
                table: "admin_tickets",
                columns: new[] { "EmpresaId", "Status", "Prioridade" });

            migrationBuilder.CreateIndex(
                name: "ix_admin_tickets_fatura_id",
                table: "admin_tickets",
                column: "FaturaId",
                filter: "\"FaturaId\" IS NOT NULL");

            migrationBuilder.CreateIndex(
                name: "ix_admin_tickets_nivel_status",
                table: "admin_tickets",
                columns: new[] { "Nivel", "Status" });

            migrationBuilder.CreateIndex(
                name: "ix_admin_tickets_origem_ticket_id",
                table: "admin_tickets",
                column: "OrigemTicketId");

            migrationBuilder.CreateIndex(
                name: "ix_admin_tickets_pedido_id",
                table: "admin_tickets",
                column: "PedidoId",
                filter: "\"PedidoId\" IS NOT NULL");

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
                name: "IX_anuncios_ia_EmpresaId_CriadoEm",
                table: "anuncios_ia",
                columns: new[] { "EmpresaId", "CriadoEm" });

            migrationBuilder.CreateIndex(
                name: "IX_anuncios_ia_EmpresaId_ProdutoId",
                table: "anuncios_ia",
                columns: new[] { "EmpresaId", "ProdutoId" });

            migrationBuilder.CreateIndex(
                name: "IX_anuncios_ia_ProdutoId",
                table: "anuncios_ia",
                column: "ProdutoId");

            migrationBuilder.CreateIndex(
                name: "ix_banner_confirmacoes_usuario",
                table: "banner_confirmacoes",
                columns: new[] { "UsuarioId", "BannerId" });

            migrationBuilder.CreateIndex(
                name: "ux_banner_confirmacoes_banner_usuario_tipo",
                table: "banner_confirmacoes",
                columns: new[] { "BannerId", "UsuarioId", "Tipo" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_banners_ativos",
                table: "banners",
                columns: new[] { "Ativo", "InicioEm", "FimEm" },
                filter: "\"Ativo\" = TRUE");

            migrationBuilder.CreateIndex(
                name: "ix_banners_prioridade",
                table: "banners",
                column: "Prioridade");

            migrationBuilder.CreateIndex(
                name: "ix_empresa_configuracao_fiscal_certificado",
                table: "empresa_configuracao_fiscal",
                column: "CertificadoCredencialId",
                filter: "\"CertificadoCredencialId\" IS NOT NULL");

            migrationBuilder.CreateIndex(
                name: "ux_empresa_configuracao_fiscal_empresa",
                table: "empresa_configuracao_fiscal",
                column: "EmpresaId",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_faq_categorias_publica_ordem",
                table: "faq_categorias",
                columns: new[] { "Publica", "Ordem" });

            migrationBuilder.CreateIndex(
                name: "ux_faq_categorias_slug",
                table: "faq_categorias",
                column: "Slug",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_faq_feedbacks_item_util",
                table: "faq_feedbacks",
                columns: new[] { "ItemId", "Util" });

            migrationBuilder.CreateIndex(
                name: "ix_faq_itens_status_publicado",
                table: "faq_itens",
                columns: new[] { "Status", "PublicadoEm" });

            migrationBuilder.CreateIndex(
                name: "ix_faq_itens_visualizacoes",
                table: "faq_itens",
                column: "Visualizacoes");

            migrationBuilder.CreateIndex(
                name: "ux_faq_itens_categoria_slug",
                table: "faq_itens",
                columns: new[] { "CategoriaId", "Slug" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_faq_visualizacoes_item_criado",
                table: "faq_visualizacoes",
                columns: new[] { "ItemId", "CriadoEm" });

            migrationBuilder.CreateIndex(
                name: "IX_leads_publicos_CriadoEm",
                table: "leads_publicos",
                column: "CriadoEm");

            migrationBuilder.CreateIndex(
                name: "IX_leads_publicos_IpOrigem",
                table: "leads_publicos",
                column: "IpOrigem");

            migrationBuilder.CreateIndex(
                name: "IX_leads_publicos_Origem",
                table: "leads_publicos",
                column: "Origem");

            migrationBuilder.CreateIndex(
                name: "IX_leads_publicos_ProcessadoEm",
                table: "leads_publicos",
                column: "ProcessadoEm");

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
                name: "FK_faturas_admin_tickets_TicketRelacionadoId",
                table: "faturas",
                column: "TicketRelacionadoId",
                principalTable: "admin_tickets",
                principalColumn: "Id",
                onDelete: ReferentialAction.SetNull);
        }
    }
}
