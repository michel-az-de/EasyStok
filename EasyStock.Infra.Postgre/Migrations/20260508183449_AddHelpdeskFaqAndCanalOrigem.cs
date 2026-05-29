using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace EasyStock.Infra.Postgre.Migrations
{
    /// <inheritdoc />
    public partial class AddHelpdeskFaqAndCanalOrigem : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<int>(
                name: "CanalOrigem",
                table: "admin_tickets",
                type: "integer",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.CreateTable(
                name: "faq_categorias",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    Nome = table.Column<string>(type: "character varying(80)", maxLength: 80, nullable: false),
                    Slug = table.Column<string>(type: "character varying(80)", maxLength: 80, nullable: false),
                    Descricao = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: true),
                    Icone = table.Column<string>(type: "character varying(60)", maxLength: 60, nullable: true),
                    Ordem = table.Column<int>(type: "integer", nullable: false),
                    Publica = table.Column<bool>(type: "boolean", nullable: false, defaultValue: true),
                    CriadoEm = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    AtualizadoEm = table.Column<DateTime>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_faq_categorias", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "faq_itens",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    CategoriaId = table.Column<Guid>(type: "uuid", nullable: false),
                    Titulo = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    Slug = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    Conteudo = table.Column<string>(type: "character varying(20000)", maxLength: 20000, nullable: false),
                    ConteudoBusca = table.Column<string>(type: "character varying(20000)", maxLength: 20000, nullable: false),
                    TagsCsv = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: false, defaultValue: ""),
                    Status = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    PublicadoEm = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    CriadoEm = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    AtualizadoEm = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    AutorId = table.Column<Guid>(type: "uuid", nullable: true),
                    Ordem = table.Column<int>(type: "integer", nullable: false),
                    Visualizacoes = table.Column<int>(type: "integer", nullable: false, defaultValue: 0),
                    UtilCount = table.Column<int>(type: "integer", nullable: false, defaultValue: 0),
                    NaoUtilCount = table.Column<int>(type: "integer", nullable: false, defaultValue: 0)
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
                name: "faq_feedbacks",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    ItemId = table.Column<Guid>(type: "uuid", nullable: false),
                    Util = table.Column<bool>(type: "boolean", nullable: false),
                    Comentario = table.Column<string>(type: "character varying(1000)", maxLength: 1000, nullable: true),
                    IpHash = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    CriadoEm = table.Column<DateTime>(type: "timestamp with time zone", nullable: false)
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
                    IpHash = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    Termo = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: true),
                    Origem = table.Column<string>(type: "character varying(40)", maxLength: 40, nullable: true),
                    CriadoEm = table.Column<DateTime>(type: "timestamp with time zone", nullable: false)
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

            // Indice GIN para FTS portugues — acelera busca por "termo" no FaqRepository
            migrationBuilder.Sql(@"
CREATE INDEX IF NOT EXISTS ix_faq_itens_fts
    ON faq_itens
    USING GIN (to_tsvector('portuguese', ""Titulo"" || ' ' || coalesce(""ConteudoBusca"", '')));
");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql("DROP INDEX IF EXISTS ix_faq_itens_fts;");

            migrationBuilder.DropTable(
                name: "faq_feedbacks");

            migrationBuilder.DropTable(
                name: "faq_visualizacoes");

            migrationBuilder.DropTable(
                name: "faq_itens");

            migrationBuilder.DropTable(
                name: "faq_categorias");

            migrationBuilder.DropColumn(
                name: "CanalOrigem",
                table: "admin_tickets");
        }
    }
}
