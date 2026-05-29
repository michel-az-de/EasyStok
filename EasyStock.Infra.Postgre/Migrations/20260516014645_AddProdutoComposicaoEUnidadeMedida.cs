using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace EasyStock.Infra.Postgre.Migrations
{
    /// <inheritdoc />
    public partial class AddProdutoComposicaoEUnidadeMedida : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<bool>(
                name: "EhInsumo",
                table: "produtos",
                type: "boolean",
                nullable: false,
                defaultValue: false);

            migrationBuilder.AddColumn<decimal>(
                name: "RendimentoBase",
                table: "produtos",
                type: "numeric(19,4)",
                nullable: false,
                defaultValue: 1m);

            migrationBuilder.AddColumn<string>(
                name: "RendimentoUnidade",
                table: "produtos",
                type: "character varying(8)",
                maxLength: 8,
                nullable: false,
                defaultValue: "Un");

            migrationBuilder.AddColumn<string>(
                name: "UnidadeMedidaBase",
                table: "produtos",
                type: "character varying(8)",
                maxLength: 8,
                nullable: false,
                defaultValue: "Un");

            migrationBuilder.CreateTable(
                name: "produtos_composicao",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    EmpresaId = table.Column<Guid>(type: "uuid", nullable: false),
                    ProdutoFinalId = table.Column<Guid>(type: "uuid", nullable: false),
                    InsumoId = table.Column<Guid>(type: "uuid", nullable: false),
                    LojaId = table.Column<Guid>(type: "uuid", nullable: true),
                    Quantidade = table.Column<decimal>(type: "numeric(19,4)", nullable: false),
                    Unidade = table.Column<string>(type: "character varying(8)", maxLength: 8, nullable: false),
                    Observacao = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: true),
                    OrdemExibicao = table.Column<int>(type: "integer", nullable: false),
                    CriadoEm = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    AlteradoEm = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    CriadoPor = table.Column<Guid>(type: "uuid", nullable: true),
                    AlteradoPor = table.Column<Guid>(type: "uuid", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_produtos_composicao", x => x.Id);
                    table.ForeignKey(
                        name: "FK_produtos_composicao_empresas_EmpresaId",
                        column: x => x.EmpresaId,
                        principalTable: "empresas",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_produtos_composicao_lojas_LojaId",
                        column: x => x.LojaId,
                        principalTable: "lojas",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_produtos_composicao_produtos_InsumoId",
                        column: x => x.InsumoId,
                        principalTable: "produtos",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_produtos_composicao_produtos_ProdutoFinalId",
                        column: x => x.ProdutoFinalId,
                        principalTable: "produtos",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "produtos_composicao_alteracao",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    EmpresaId = table.Column<Guid>(type: "uuid", nullable: false),
                    ProdutoFinalId = table.Column<Guid>(type: "uuid", nullable: false),
                    LojaId = table.Column<Guid>(type: "uuid", nullable: true),
                    UsuarioId = table.Column<Guid>(type: "uuid", nullable: false),
                    Acao = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: false),
                    AlteracoesJson = table.Column<string>(type: "text", nullable: true),
                    Observacao = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: true),
                    AlteradoEm = table.Column<DateTime>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_produtos_composicao_alteracao", x => x.Id);
                    table.ForeignKey(
                        name: "FK_produtos_composicao_alteracao_lojas_LojaId",
                        column: x => x.LojaId,
                        principalTable: "lojas",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.SetNull);
                    table.ForeignKey(
                        name: "FK_produtos_composicao_alteracao_produtos_ProdutoFinalId",
                        column: x => x.ProdutoFinalId,
                        principalTable: "produtos",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_produtos_composicao_alteracao_usuarios_UsuarioId",
                        column: x => x.UsuarioId,
                        principalTable: "usuarios",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.SetNull);
                });

            migrationBuilder.CreateIndex(
                name: "ix_produtos_composicao_empresa_final",
                table: "produtos_composicao",
                columns: new[] { "EmpresaId", "ProdutoFinalId" });

            migrationBuilder.CreateIndex(
                name: "ix_produtos_composicao_empresa_final_insumo_loja_unique",
                table: "produtos_composicao",
                columns: new[] { "EmpresaId", "ProdutoFinalId", "InsumoId", "LojaId" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_produtos_composicao_empresa_insumo",
                table: "produtos_composicao",
                columns: new[] { "EmpresaId", "InsumoId" });

            migrationBuilder.CreateIndex(
                name: "IX_produtos_composicao_InsumoId",
                table: "produtos_composicao",
                column: "InsumoId");

            migrationBuilder.CreateIndex(
                name: "IX_produtos_composicao_LojaId",
                table: "produtos_composicao",
                column: "LojaId");

            migrationBuilder.CreateIndex(
                name: "IX_produtos_composicao_ProdutoFinalId",
                table: "produtos_composicao",
                column: "ProdutoFinalId");

            migrationBuilder.CreateIndex(
                name: "ix_produtos_composicao_alteracao_empresa_alterado_em",
                table: "produtos_composicao_alteracao",
                columns: new[] { "EmpresaId", "AlteradoEm" });

            migrationBuilder.CreateIndex(
                name: "IX_produtos_composicao_alteracao_LojaId",
                table: "produtos_composicao_alteracao",
                column: "LojaId");

            migrationBuilder.CreateIndex(
                name: "IX_produtos_composicao_alteracao_ProdutoFinalId_AlteradoEm",
                table: "produtos_composicao_alteracao",
                columns: new[] { "ProdutoFinalId", "AlteradoEm" });

            migrationBuilder.CreateIndex(
                name: "IX_produtos_composicao_alteracao_UsuarioId",
                table: "produtos_composicao_alteracao",
                column: "UsuarioId");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "produtos_composicao");

            migrationBuilder.DropTable(
                name: "produtos_composicao_alteracao");

            migrationBuilder.DropColumn(
                name: "EhInsumo",
                table: "produtos");

            migrationBuilder.DropColumn(
                name: "RendimentoBase",
                table: "produtos");

            migrationBuilder.DropColumn(
                name: "RendimentoUnidade",
                table: "produtos");

            migrationBuilder.DropColumn(
                name: "UnidadeMedidaBase",
                table: "produtos");
        }
    }
}
