using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace EasyStock.Infra.Postgre.Migrations
{
    /// <inheritdoc />
    public partial class AddStorefront : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "Bairro",
                table: "clientes",
                type: "character varying(100)",
                maxLength: 100,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "Cep",
                table: "clientes",
                type: "character varying(8)",
                maxLength: 8,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "Cidade",
                table: "clientes",
                type: "character varying(100)",
                maxLength: 100,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "Complemento",
                table: "clientes",
                type: "character varying(100)",
                maxLength: 100,
                nullable: true);

            migrationBuilder.AddColumn<DateTime>(
                name: "ConsentimentoEm",
                table: "clientes",
                type: "timestamp with time zone",
                nullable: true);

            migrationBuilder.AddColumn<bool>(
                name: "ConsentiuMarketing",
                table: "clientes",
                type: "boolean",
                nullable: false,
                defaultValue: false);

            migrationBuilder.AddColumn<string>(
                name: "Cpf",
                table: "clientes",
                type: "character varying(11)",
                maxLength: 11,
                nullable: true);

            migrationBuilder.AddColumn<DateTime>(
                name: "UltimoAcessoStorefrontEm",
                table: "clientes",
                type: "timestamp with time zone",
                nullable: true);

            migrationBuilder.CreateTable(
                name: "checkout_idempotency",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    Key = table.Column<Guid>(type: "uuid", nullable: false),
                    ContentHash = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    FaturaId = table.Column<Guid>(type: "uuid", nullable: true),
                    InitPoint = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: true),
                    CriadoEm = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    ExpiraEm = table.Column<DateTime>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_checkout_idempotency", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "cliente_otp",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    EmpresaId = table.Column<Guid>(type: "uuid", nullable: false),
                    TelefoneHash = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    CodigoHash = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    ExpiraEm = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    Tentativas = table.Column<int>(type: "integer", nullable: false, defaultValue: 0),
                    Consumido = table.Column<bool>(type: "boolean", nullable: false, defaultValue: false),
                    IpOrigem = table.Column<string>(type: "character varying(45)", maxLength: 45, nullable: true),
                    UserAgent = table.Column<string>(type: "character varying(300)", maxLength: 300, nullable: true),
                    CriadoEm = table.Column<DateTime>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_cliente_otp", x => x.Id);
                    table.ForeignKey(
                        name: "FK_cliente_otp_empresas_EmpresaId",
                        column: x => x.EmpresaId,
                        principalTable: "empresas",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "cliente_session",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    ClienteId = table.Column<Guid>(type: "uuid", nullable: false),
                    EmpresaId = table.Column<Guid>(type: "uuid", nullable: false),
                    CriadoEm = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    UltimoUsoEm = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    IpInicial = table.Column<string>(type: "character varying(45)", maxLength: 45, nullable: true),
                    UaInicial = table.Column<string>(type: "character varying(300)", maxLength: 300, nullable: true),
                    Revogada = table.Column<bool>(type: "boolean", nullable: false, defaultValue: false),
                    MotivoRevogacao = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_cliente_session", x => x.Id);
                    table.ForeignKey(
                        name: "FK_cliente_session_clientes_ClienteId",
                        column: x => x.ClienteId,
                        principalTable: "clientes",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_cliente_session_empresas_EmpresaId",
                        column: x => x.EmpresaId,
                        principalTable: "empresas",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "pedido_avaliacao",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    PedidoId = table.Column<Guid>(type: "uuid", nullable: false),
                    ClienteId = table.Column<Guid>(type: "uuid", nullable: false),
                    EmpresaId = table.Column<Guid>(type: "uuid", nullable: false),
                    Estrelas = table.Column<int>(type: "integer", nullable: false),
                    Comentario = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: true),
                    RecomendariaParaAmigos = table.Column<bool>(type: "boolean", nullable: false),
                    FotoUrl = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: true),
                    SolicitadoEm = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    RespondidoEm = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    OcultadoEm = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    RespostaDaBaba = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: true),
                    RespondidaEmPorBaba = table.Column<DateTime>(type: "timestamp with time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_pedido_avaliacao", x => x.Id);
                    table.ForeignKey(
                        name: "FK_pedido_avaliacao_clientes_ClienteId",
                        column: x => x.ClienteId,
                        principalTable: "clientes",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_pedido_avaliacao_pedidos_PedidoId",
                        column: x => x.PedidoId,
                        principalTable: "pedidos",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "storefront",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    EmpresaId = table.Column<Guid>(type: "uuid", nullable: false),
                    LojaPadraoId = table.Column<Guid>(type: "uuid", nullable: true),
                    Slug = table.Column<string>(type: "character varying(40)", maxLength: 40, nullable: false),
                    DominioCustom = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: true),
                    TituloPublico = table.Column<string>(type: "character varying(120)", maxLength: 120, nullable: false),
                    SubtituloPublico = table.Column<string>(type: "character varying(240)", maxLength: 240, nullable: true),
                    LogoUrl = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: true),
                    CorPrimaria = table.Column<string>(type: "character varying(7)", maxLength: 7, nullable: true),
                    WhatsappPedidos = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: true),
                    PedidoMinimoEntrega = table.Column<decimal>(type: "numeric(10,2)", nullable: false),
                    FreteGratisAcima = table.Column<decimal>(type: "numeric(10,2)", nullable: true),
                    MensagemForaArea = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: true),
                    NfeAutomaticaHabilitada = table.Column<bool>(type: "boolean", nullable: false, defaultValue: false),
                    ModeloFiscal = table.Column<string>(type: "character varying(16)", maxLength: 16, nullable: false, defaultValue: "manual"),
                    Ativo = table.Column<bool>(type: "boolean", nullable: false, defaultValue: false),
                    CriadoEm = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    AlteradoEm = table.Column<DateTime>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_storefront", x => x.Id);
                    table.ForeignKey(
                        name: "FK_storefront_empresas_EmpresaId",
                        column: x => x.EmpresaId,
                        principalTable: "empresas",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_storefront_lojas_LojaPadraoId",
                        column: x => x.LojaPadraoId,
                        principalTable: "lojas",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.SetNull);
                });

            migrationBuilder.CreateTable(
                name: "storefront_fale_conosco",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    StorefrontId = table.Column<Guid>(type: "uuid", nullable: false),
                    ClienteId = table.Column<Guid>(type: "uuid", nullable: true),
                    Nome = table.Column<string>(type: "character varying(120)", maxLength: 120, nullable: false),
                    Telefone = table.Column<string>(type: "character varying(30)", maxLength: 30, nullable: false),
                    Email = table.Column<string>(type: "character varying(254)", maxLength: 254, nullable: true),
                    Assunto = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    Mensagem = table.Column<string>(type: "character varying(2000)", maxLength: 2000, nullable: false),
                    CriadoEm = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    Respondido = table.Column<bool>(type: "boolean", nullable: false, defaultValue: false),
                    RespondidoPor = table.Column<string>(type: "character varying(120)", maxLength: 120, nullable: true),
                    RespondidoEm = table.Column<DateTime>(type: "timestamp with time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_storefront_fale_conosco", x => x.Id);
                    table.ForeignKey(
                        name: "FK_storefront_fale_conosco_clientes_ClienteId",
                        column: x => x.ClienteId,
                        principalTable: "clientes",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.SetNull);
                });

            migrationBuilder.CreateTable(
                name: "webhook_processado",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    Provider = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false),
                    EventoId = table.Column<string>(type: "character varying(128)", maxLength: 128, nullable: false),
                    Tipo = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    PayloadRaw = table.Column<string>(type: "jsonb", nullable: false),
                    Status = table.Column<int>(type: "integer", nullable: false),
                    Motivo = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: true),
                    RecebidoEm = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    ProcessadoEm = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    EmpresaId = table.Column<Guid>(type: "uuid", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_webhook_processado", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "cardapio_item",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    StorefrontId = table.Column<Guid>(type: "uuid", nullable: false),
                    ProdutoId = table.Column<Guid>(type: "uuid", nullable: false),
                    Visivel = table.Column<bool>(type: "boolean", nullable: false, defaultValue: false),
                    Disponivel = table.Column<bool>(type: "boolean", nullable: false, defaultValue: true),
                    OrdemExibicao = table.Column<double>(type: "double precision", nullable: false, defaultValue: 0.0),
                    DescricaoPublica = table.Column<string>(type: "character varying(240)", maxLength: 240, nullable: true),
                    Ingredientes = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: true),
                    Alergenos = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: true),
                    SugestaoMolho = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: true),
                    TempoPreparo = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: true),
                    FotoUrl = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: true),
                    PrecoStorefront = table.Column<decimal>(type: "numeric(10,2)", nullable: true),
                    Tag = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: true),
                    FiltrosJson = table.Column<string>(type: "jsonb", nullable: false, defaultValue: "[]"),
                    PesoExibicao = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: true),
                    CriadoEm = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    AlteradoEm = table.Column<DateTime>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_cardapio_item", x => x.Id);
                    table.ForeignKey(
                        name: "FK_cardapio_item_produtos_ProdutoId",
                        column: x => x.ProdutoId,
                        principalTable: "produtos",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_cardapio_item_storefront_StorefrontId",
                        column: x => x.StorefrontId,
                        principalTable: "storefront",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "frete_zona",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    StorefrontId = table.Column<Guid>(type: "uuid", nullable: false),
                    Ordem = table.Column<int>(type: "integer", nullable: false, defaultValue: 0),
                    Label = table.Column<string>(type: "character varying(80)", maxLength: 80, nullable: false),
                    Valor = table.Column<decimal>(type: "numeric(10,2)", nullable: false),
                    TempoEstimadoMinutos = table.Column<int>(type: "integer", nullable: false),
                    Ativa = table.Column<bool>(type: "boolean", nullable: false, defaultValue: true),
                    TipoCobertura = table.Column<string>(type: "character varying(16)", maxLength: 16, nullable: false),
                    CepInicio = table.Column<string>(type: "character varying(8)", maxLength: 8, nullable: true),
                    CepFim = table.Column<string>(type: "character varying(8)", maxLength: 8, nullable: true),
                    BairrosJson = table.Column<string>(type: "jsonb", nullable: true),
                    CriadoEm = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    AlteradoEm = table.Column<DateTime>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_frete_zona", x => x.Id);
                    table.ForeignKey(
                        name: "FK_frete_zona_storefront_StorefrontId",
                        column: x => x.StorefrontId,
                        principalTable: "storefront",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "janela_entrega",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    StorefrontId = table.Column<Guid>(type: "uuid", nullable: false),
                    DiaDaSemana = table.Column<int>(type: "integer", nullable: false),
                    HoraInicio = table.Column<TimeOnly>(type: "time without time zone", nullable: false),
                    HoraFim = table.Column<TimeOnly>(type: "time without time zone", nullable: false),
                    CapacidadeMaxima = table.Column<int>(type: "integer", nullable: false),
                    Label = table.Column<string>(type: "character varying(80)", maxLength: 80, nullable: false),
                    Ativa = table.Column<bool>(type: "boolean", nullable: false, defaultValue: true),
                    CriadoEm = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    AlteradoEm = table.Column<DateTime>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_janela_entrega", x => x.Id);
                    table.ForeignKey(
                        name: "FK_janela_entrega_storefront_StorefrontId",
                        column: x => x.StorefrontId,
                        principalTable: "storefront",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "bloqueio_entrega",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    StorefrontId = table.Column<Guid>(type: "uuid", nullable: false),
                    Data = table.Column<DateOnly>(type: "date", nullable: false),
                    JanelaEspecificaId = table.Column<Guid>(type: "uuid", nullable: true),
                    Motivo = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    CriadoEm = table.Column<DateTime>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_bloqueio_entrega", x => x.Id);
                    table.ForeignKey(
                        name: "FK_bloqueio_entrega_janela_entrega_JanelaEspecificaId",
                        column: x => x.JanelaEspecificaId,
                        principalTable: "janela_entrega",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_bloqueio_entrega_storefront_StorefrontId",
                        column: x => x.StorefrontId,
                        principalTable: "storefront",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "vaga_ocupada",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    JanelaEntregaId = table.Column<Guid>(type: "uuid", nullable: false),
                    DataEntrega = table.Column<DateOnly>(type: "date", nullable: false),
                    PedidoId = table.Column<Guid>(type: "uuid", nullable: false),
                    OcupadoEm = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    LiberadoEm = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    MotivoLiberacao = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_vaga_ocupada", x => x.Id);
                    table.ForeignKey(
                        name: "FK_vaga_ocupada_janela_entrega_JanelaEntregaId",
                        column: x => x.JanelaEntregaId,
                        principalTable: "janela_entrega",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_vaga_ocupada_pedidos_PedidoId",
                        column: x => x.PedidoId,
                        principalTable: "pedidos",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "IX_bloqueio_entrega_JanelaEspecificaId",
                table: "bloqueio_entrega",
                column: "JanelaEspecificaId");

            migrationBuilder.CreateIndex(
                name: "ix_bloqueio_entrega_storefront_data",
                table: "bloqueio_entrega",
                columns: new[] { "StorefrontId", "Data" });

            migrationBuilder.CreateIndex(
                name: "IX_cardapio_item_ProdutoId",
                table: "cardapio_item",
                column: "ProdutoId");

            migrationBuilder.CreateIndex(
                name: "ix_cardapio_item_storefront_ordem",
                table: "cardapio_item",
                columns: new[] { "StorefrontId", "OrdemExibicao" });

            migrationBuilder.CreateIndex(
                name: "uq_cardapio_item_storefront_produto",
                table: "cardapio_item",
                columns: new[] { "StorefrontId", "ProdutoId" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_checkout_idempotency_expira",
                table: "checkout_idempotency",
                column: "ExpiraEm");

            migrationBuilder.CreateIndex(
                name: "uq_checkout_idempotency_key_hash",
                table: "checkout_idempotency",
                columns: new[] { "Key", "ContentHash" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_cliente_otp_empresa_telefone_expira",
                table: "cliente_otp",
                columns: new[] { "EmpresaId", "TelefoneHash", "ExpiraEm" });

            migrationBuilder.CreateIndex(
                name: "ix_cliente_otp_expira",
                table: "cliente_otp",
                column: "ExpiraEm");

            migrationBuilder.CreateIndex(
                name: "ix_cliente_session_cliente_revogada",
                table: "cliente_session",
                columns: new[] { "ClienteId", "Revogada" });

            migrationBuilder.CreateIndex(
                name: "IX_cliente_session_EmpresaId",
                table: "cliente_session",
                column: "EmpresaId");

            migrationBuilder.CreateIndex(
                name: "ix_cliente_session_ultimo_uso",
                table: "cliente_session",
                column: "UltimoUsoEm");

            migrationBuilder.CreateIndex(
                name: "ix_frete_zona_storefront_cep_range",
                table: "frete_zona",
                columns: new[] { "StorefrontId", "CepInicio", "CepFim" },
                filter: "\"cep_inicio\" IS NOT NULL");

            migrationBuilder.CreateIndex(
                name: "ix_frete_zona_storefront_ordem",
                table: "frete_zona",
                columns: new[] { "StorefrontId", "Ordem" });

            migrationBuilder.CreateIndex(
                name: "ix_janela_entrega_storefront_dia",
                table: "janela_entrega",
                columns: new[] { "StorefrontId", "DiaDaSemana" });

            migrationBuilder.CreateIndex(
                name: "IX_pedido_avaliacao_ClienteId",
                table: "pedido_avaliacao",
                column: "ClienteId");

            migrationBuilder.CreateIndex(
                name: "ix_pedido_avaliacao_empresa_respondido",
                table: "pedido_avaliacao",
                columns: new[] { "EmpresaId", "RespondidoEm" });

            migrationBuilder.CreateIndex(
                name: "uq_pedido_avaliacao_pedido",
                table: "pedido_avaliacao",
                column: "PedidoId",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_storefront_empresa_id",
                table: "storefront",
                column: "EmpresaId");

            migrationBuilder.CreateIndex(
                name: "IX_storefront_LojaPadraoId",
                table: "storefront",
                column: "LojaPadraoId");

            migrationBuilder.CreateIndex(
                name: "uq_storefront_dominio_custom",
                table: "storefront",
                column: "DominioCustom",
                unique: true,
                filter: "\"dominio_custom\" IS NOT NULL");

            migrationBuilder.CreateIndex(
                name: "uq_storefront_slug",
                table: "storefront",
                column: "Slug",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_fale_conosco_storefront_resp_data",
                table: "storefront_fale_conosco",
                columns: new[] { "StorefrontId", "Respondido", "CriadoEm" });

            migrationBuilder.CreateIndex(
                name: "IX_storefront_fale_conosco_ClienteId",
                table: "storefront_fale_conosco",
                column: "ClienteId");

            migrationBuilder.CreateIndex(
                name: "ix_vaga_ativa_por_janela_data",
                table: "vaga_ocupada",
                columns: new[] { "JanelaEntregaId", "DataEntrega" },
                filter: "\"liberado_em\" IS NULL");

            migrationBuilder.CreateIndex(
                name: "uq_vaga_ativa_por_pedido",
                table: "vaga_ocupada",
                column: "PedidoId",
                unique: true,
                filter: "\"liberado_em\" IS NULL");

            migrationBuilder.CreateIndex(
                name: "ix_webhook_processado_received_recebido_em",
                table: "webhook_processado",
                columns: new[] { "Status", "RecebidoEm" },
                filter: "\"status\" = 0");

            migrationBuilder.CreateIndex(
                name: "uq_webhook_processado_provider_evento",
                table: "webhook_processado",
                columns: new[] { "Provider", "EventoId" },
                unique: true);

            // ── Row-Level Security ───────────────────────────────────────
            // Tabelas com EmpresaId discriminator recebem policy tenant_isolation
            // idêntica ao padrão AddRowLevelSecurity (2026-05-11). Tabelas storefront
            // sem EmpresaId direto (cardapio_item, frete_zona, janela_entrega,
            // bloqueio_entrega, vaga_ocupada, storefront_fale_conosco) ficam
            // protegidas via Global Query Filter EF + FK CASCADE pra Storefront
            // (já com RLS). checkout_idempotency é per-request, sem tenant scoping.
            migrationBuilder.Sql("""
DO $rls$
DECLARE
    rec RECORD;
    target_tables TEXT[] := ARRAY[
        'storefront',
        'cliente_otp',
        'cliente_session',
        'pedido_avaliacao',
        'webhook_processado'
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

        -- NULLIF('','')::uuid: connection nova de pool sem tenant setado
        -- retorna '' (missing_ok=true), NULLIF vira NULL, comparação UNKNOWN
        -- → 0 linhas (fail-closed). webhook_processado tem EmpresaId NULL antes
        -- do processamento → também filtrado (UNKNOWN), comportamento desejado.
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
            // Reverte RLS antes de DROP TABLE — idempotente.
            migrationBuilder.Sql("""
DO $rls_down$
DECLARE
    rec RECORD;
    target_tables TEXT[] := ARRAY[
        'storefront',
        'cliente_otp',
        'cliente_session',
        'pedido_avaliacao',
        'webhook_processado'
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
    LOOP
        EXECUTE format(
            'DROP POLICY IF EXISTS tenant_isolation ON %I.%I',
            rec.table_schema, rec.table_name);
        EXECUTE format(
            'ALTER TABLE %I.%I NO FORCE ROW LEVEL SECURITY',
            rec.table_schema, rec.table_name);
        EXECUTE format(
            'ALTER TABLE %I.%I DISABLE ROW LEVEL SECURITY',
            rec.table_schema, rec.table_name);
    END LOOP;
END
$rls_down$;
""");

            migrationBuilder.DropTable(
                name: "bloqueio_entrega");

            migrationBuilder.DropTable(
                name: "cardapio_item");

            migrationBuilder.DropTable(
                name: "checkout_idempotency");

            migrationBuilder.DropTable(
                name: "cliente_otp");

            migrationBuilder.DropTable(
                name: "cliente_session");

            migrationBuilder.DropTable(
                name: "frete_zona");

            migrationBuilder.DropTable(
                name: "pedido_avaliacao");

            migrationBuilder.DropTable(
                name: "storefront_fale_conosco");

            migrationBuilder.DropTable(
                name: "vaga_ocupada");

            migrationBuilder.DropTable(
                name: "webhook_processado");

            migrationBuilder.DropTable(
                name: "janela_entrega");

            migrationBuilder.DropTable(
                name: "storefront");

            migrationBuilder.DropColumn(
                name: "Bairro",
                table: "clientes");

            migrationBuilder.DropColumn(
                name: "Cep",
                table: "clientes");

            migrationBuilder.DropColumn(
                name: "Cidade",
                table: "clientes");

            migrationBuilder.DropColumn(
                name: "Complemento",
                table: "clientes");

            migrationBuilder.DropColumn(
                name: "ConsentimentoEm",
                table: "clientes");

            migrationBuilder.DropColumn(
                name: "ConsentiuMarketing",
                table: "clientes");

            migrationBuilder.DropColumn(
                name: "Cpf",
                table: "clientes");

            migrationBuilder.DropColumn(
                name: "UltimoAcessoStorefrontEm",
                table: "clientes");
        }
    }
}
