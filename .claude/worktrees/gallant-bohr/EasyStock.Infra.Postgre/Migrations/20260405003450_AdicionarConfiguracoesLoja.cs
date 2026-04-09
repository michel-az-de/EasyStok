using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace EasyStock.Infra.Postgre.Migrations
{
    /// <inheritdoc />
    public partial class AdicionarConfiguracoesLoja : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<int>(
                name: "LimiteGeracoesIaMensais",
                table: "planos",
                type: "integer",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.CreateTable(
                name: "anuncios_ia",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    EmpresaId = table.Column<Guid>(type: "uuid", nullable: false),
                    ProdutoId = table.Column<Guid>(type: "uuid", nullable: false),
                    ProdutoVariacaoId = table.Column<Guid>(type: "uuid", nullable: true),
                    Titulo = table.Column<string>(type: "character varying(255)", maxLength: 255, nullable: false),
                    Conteudo = table.Column<string>(type: "text", nullable: false),
                    InstrucoesUsadas = table.Column<string>(type: "text", nullable: true),
                    TokensConsumidos = table.Column<int>(type: "integer", nullable: false),
                    Salvo = table.Column<bool>(type: "boolean", nullable: false),
                    CriadoEm = table.Column<DateTime>(type: "timestamp with time zone", nullable: false)
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
                name: "configuracoes_loja",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    LojaId = table.Column<Guid>(type: "uuid", nullable: false),
                    DiasAlertaValidade = table.Column<int>(type: "integer", nullable: false, defaultValue: 15),
                    DiasAlertaParado = table.Column<int>(type: "integer", nullable: false, defaultValue: 30),
                    QuantidadeMinimaPadrao = table.Column<int>(type: "integer", nullable: false, defaultValue: 5),
                    NotificarEstoqueCritico = table.Column<bool>(type: "boolean", nullable: false, defaultValue: true),
                    NotificarValidade = table.Column<bool>(type: "boolean", nullable: false, defaultValue: true),
                    NotificarParado = table.Column<bool>(type: "boolean", nullable: false, defaultValue: true),
                    NotificarReposicao = table.Column<bool>(type: "boolean", nullable: false, defaultValue: true),
                    FifoAtivo = table.Column<bool>(type: "boolean", nullable: false, defaultValue: true),
                    Moeda = table.Column<string>(type: "character varying(10)", maxLength: 10, nullable: false, defaultValue: "BRL"),
                    Timezone = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false, defaultValue: "America/Sao_Paulo"),
                    CriadoEm = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    AlteradoEm = table.Column<DateTime>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_configuracoes_loja", x => x.Id);
                    table.ForeignKey(
                        name: "FK_configuracoes_loja_lojas_LojaId",
                        column: x => x.LojaId,
                        principalTable: "lojas",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "uso_ia",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    EmpresaId = table.Column<Guid>(type: "uuid", nullable: false),
                    Ano = table.Column<int>(type: "integer", nullable: false),
                    Mes = table.Column<int>(type: "integer", nullable: false),
                    TotalGeracoes = table.Column<int>(type: "integer", nullable: false),
                    TotalTokens = table.Column<int>(type: "integer", nullable: false),
                    AtualizadoEm = table.Column<DateTime>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_uso_ia", x => x.Id);
                });

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
                name: "IX_configuracoes_loja_LojaId",
                table: "configuracoes_loja",
                column: "LojaId",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_uso_ia_EmpresaId_Ano_Mes",
                table: "uso_ia",
                columns: new[] { "EmpresaId", "Ano", "Mes" },
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "anuncios_ia");

            migrationBuilder.DropTable(
                name: "configuracoes_loja");

            migrationBuilder.DropTable(
                name: "uso_ia");

            migrationBuilder.DropColumn(
                name: "LimiteGeracoesIaMensais",
                table: "planos");
        }
    }
}
