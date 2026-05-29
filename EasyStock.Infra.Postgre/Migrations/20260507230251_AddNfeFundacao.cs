using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace EasyStock.Infra.Postgre.Migrations
{
    /// <inheritdoc />
    public partial class AddNfeFundacao : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "empresa_configuracao_fiscal",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    EmpresaId = table.Column<Guid>(type: "uuid", nullable: false),
                    RegimeTributario = table.Column<string>(type: "character varying(40)", maxLength: 40, nullable: false),
                    InscricaoEstadual = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: true),
                    InscricaoMunicipal = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: true),
                    endereco = table.Column<string>(type: "jsonb", nullable: true),
                    Ambiente = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    ProvedorPreferido = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false, defaultValue: "mock"),
                    SerieNfce = table.Column<short>(type: "smallint", nullable: false, defaultValue: (short)1),
                    ProximoNumeroNfce = table.Column<long>(type: "bigint", nullable: false, defaultValue: 1L),
                    CertificadoCredencialId = table.Column<Guid>(type: "uuid", nullable: true),
                    Habilitada = table.Column<bool>(type: "boolean", nullable: false, defaultValue: false),
                    CriadoEm = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    AlteradoEm = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
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
                name: "nfe_documentos",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    EmpresaId = table.Column<Guid>(type: "uuid", nullable: false),
                    PedidoId = table.Column<Guid>(type: "uuid", nullable: false),
                    Modelo = table.Column<string>(type: "character varying(2)", maxLength: 2, nullable: false),
                    Serie = table.Column<short>(type: "smallint", nullable: false),
                    Numero = table.Column<long>(type: "bigint", nullable: false),
                    ChaveAcesso = table.Column<string>(type: "character varying(44)", maxLength: 44, nullable: true),
                    Status = table.Column<string>(type: "character varying(40)", maxLength: 40, nullable: false),
                    ProtocoloAutorizacao = table.Column<string>(type: "character varying(40)", maxLength: 40, nullable: true),
                    DataAutorizacao = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    MotivoRejeicao = table.Column<string>(type: "character varying(2000)", maxLength: 2000, nullable: true),
                    XmlAssinadoStorageKey = table.Column<string>(type: "character varying(300)", maxLength: 300, nullable: true),
                    DanfeUrl = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: true),
                    dados_emitente = table.Column<string>(type: "jsonb", nullable: false),
                    dados_destinatario = table.Column<string>(type: "jsonb", nullable: true),
                    TotalNota = table.Column<decimal>(type: "numeric(14,2)", nullable: false),
                    CriadoEm = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    AlteradoEm = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    xmin = table.Column<uint>(type: "xid", rowVersion: true, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_nfe_documentos", x => x.Id);
                    table.ForeignKey(
                        name: "FK_nfe_documentos_empresas_EmpresaId",
                        column: x => x.EmpresaId,
                        principalTable: "empresas",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_nfe_documentos_pedidos_PedidoId",
                        column: x => x.PedidoId,
                        principalTable: "pedidos",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "nfe_eventos",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    NfeDocumentoId = table.Column<Guid>(type: "uuid", nullable: false),
                    Tipo = table.Column<string>(type: "character varying(40)", maxLength: 40, nullable: false),
                    DadosJson = table.Column<string>(type: "jsonb", nullable: true),
                    UsuarioId = table.Column<Guid>(type: "uuid", nullable: true),
                    UsuarioNome = table.Column<string>(type: "character varying(120)", maxLength: 120, nullable: true),
                    Origem = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: true),
                    OcorridoEm = table.Column<DateTime>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_nfe_eventos", x => x.Id);
                    table.ForeignKey(
                        name: "FK_nfe_eventos_nfe_documentos_NfeDocumentoId",
                        column: x => x.NfeDocumentoId,
                        principalTable: "nfe_documentos",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "nfe_itens",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    NfeDocumentoId = table.Column<Guid>(type: "uuid", nullable: false),
                    Ordem = table.Column<int>(type: "integer", nullable: false),
                    ProdutoIdSnapshot = table.Column<Guid>(type: "uuid", nullable: true),
                    NomeSnapshot = table.Column<string>(type: "character varying(300)", maxLength: 300, nullable: false),
                    NcmSnapshot = table.Column<string>(type: "character varying(8)", maxLength: 8, nullable: true),
                    CfopSnapshot = table.Column<string>(type: "character varying(4)", maxLength: 4, nullable: true),
                    OrigemMercadoria = table.Column<byte>(type: "smallint", nullable: false),
                    Quantidade = table.Column<decimal>(type: "numeric(14,3)", nullable: false),
                    Unidade = table.Column<string>(type: "character varying(6)", maxLength: 6, nullable: false),
                    PrecoUnitario = table.Column<decimal>(type: "numeric(14,2)", nullable: false),
                    Subtotal = table.Column<decimal>(type: "numeric(14,2)", nullable: false),
                    CstOuCsosn = table.Column<string>(type: "character varying(4)", maxLength: 4, nullable: true),
                    CriadoEm = table.Column<DateTime>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_nfe_itens", x => x.Id);
                    table.ForeignKey(
                        name: "FK_nfe_itens_nfe_documentos_NfeDocumentoId",
                        column: x => x.NfeDocumentoId,
                        principalTable: "nfe_documentos",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

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
                name: "ix_nfe_documentos_empresa_status",
                table: "nfe_documentos",
                columns: new[] { "EmpresaId", "Status" });

            migrationBuilder.CreateIndex(
                name: "ix_nfe_documentos_pedido",
                table: "nfe_documentos",
                column: "PedidoId");

            migrationBuilder.CreateIndex(
                name: "ux_nfe_documentos_empresa_chave_acesso",
                table: "nfe_documentos",
                columns: new[] { "EmpresaId", "ChaveAcesso" },
                unique: true,
                filter: "\"ChaveAcesso\" IS NOT NULL");

            migrationBuilder.CreateIndex(
                name: "ux_nfe_documentos_empresa_modelo_serie_numero",
                table: "nfe_documentos",
                columns: new[] { "EmpresaId", "Modelo", "Serie", "Numero" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_nfe_eventos_documento_ocorrido",
                table: "nfe_eventos",
                columns: new[] { "NfeDocumentoId", "OcorridoEm" });

            migrationBuilder.CreateIndex(
                name: "ix_nfe_itens_documento_ordem",
                table: "nfe_itens",
                columns: new[] { "NfeDocumentoId", "Ordem" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "empresa_configuracao_fiscal");

            migrationBuilder.DropTable(
                name: "nfe_eventos");

            migrationBuilder.DropTable(
                name: "nfe_itens");

            migrationBuilder.DropTable(
                name: "nfe_documentos");
        }
    }
}
