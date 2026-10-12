using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace EasyStock.Infra.Postgre.Migrations
{
    /// <inheritdoc />
    public partial class AcervoImagensCanva : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "acervo_imagens",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    EmpresaId = table.Column<Guid>(type: "uuid", nullable: false),
                    CriadoPorId = table.Column<Guid>(type: "uuid", nullable: false),
                    Titulo = table.Column<string>(type: "character varying(160)", maxLength: 160, nullable: false),
                    Origem = table.Column<string>(type: "text", nullable: false),
                    Formato = table.Column<string>(type: "text", nullable: false),
                    Sha256 = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    StorageKey = table.Column<string>(type: "text", nullable: false),
                    ContentType = table.Column<string>(type: "text", nullable: false),
                    Tamanho = table.Column<long>(type: "bigint", nullable: false),
                    Prompt = table.Column<string>(type: "text", nullable: false),
                    Referencias = table.Column<string>(type: "text", nullable: false),
                    ItensJson = table.Column<string>(type: "jsonb", nullable: false),
                    OriginalId = table.Column<Guid>(type: "uuid", nullable: true),
                    SolicitacaoId = table.Column<Guid>(type: "uuid", nullable: true),
                    CanvaUrl = table.Column<string>(type: "text", nullable: true),
                    CanvaAssetId = table.Column<string>(type: "text", nullable: true),
                    CriadoEm = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    AprovadoEm = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    AprovadoPorId = table.Column<Guid>(type: "uuid", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_acervo_imagens", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "acervo_publicacoes",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    EmpresaId = table.Column<Guid>(type: "uuid", nullable: false),
                    ImagemId = table.Column<Guid>(type: "uuid", nullable: false),
                    ItemId = table.Column<Guid>(type: "uuid", nullable: false),
                    ProdutoId = table.Column<Guid>(type: "uuid", nullable: true),
                    UsuarioId = table.Column<Guid>(type: "uuid", nullable: false),
                    Galeria = table.Column<bool>(type: "boolean", nullable: false),
                    Url = table.Column<string>(type: "text", nullable: false),
                    CapaAnterior = table.Column<string>(type: "text", nullable: true),
                    CriadoEm = table.Column<DateTime>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_acervo_publicacoes", x => new { x.EmpresaId, x.Id });
                });

            migrationBuilder.CreateTable(
                name: "acervo_solicitacoes",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    EmpresaId = table.Column<Guid>(type: "uuid", nullable: false),
                    UsuarioId = table.Column<Guid>(type: "uuid", nullable: false),
                    ItemId = table.Column<Guid>(type: "uuid", nullable: false),
                    Formato = table.Column<string>(type: "text", nullable: false),
                    Brief = table.Column<string>(type: "text", nullable: false),
                    ReferenciasJson = table.Column<string>(type: "jsonb", nullable: false),
                    CriadoEm = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    ConcluidoEm = table.Column<DateTime>(type: "timestamp with time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_acervo_solicitacoes", x => x.Id);
                });

            migrationBuilder.CreateIndex(
                name: "IX_acervo_imagens_EmpresaId_CriadoEm",
                table: "acervo_imagens",
                columns: new[] { "EmpresaId", "CriadoEm" });

            migrationBuilder.CreateIndex(
                name: "IX_acervo_imagens_EmpresaId_Sha256",
                table: "acervo_imagens",
                columns: new[] { "EmpresaId", "Sha256" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_acervo_publicacoes_EmpresaId_ImagemId_CriadoEm",
                table: "acervo_publicacoes",
                columns: new[] { "EmpresaId", "ImagemId", "CriadoEm" });

            migrationBuilder.CreateIndex(
                name: "IX_acervo_solicitacoes_EmpresaId_ConcluidoEm_CriadoEm",
                table: "acervo_solicitacoes",
                columns: new[] { "EmpresaId", "ConcluidoEm", "CriadoEm" });
            foreach (var table in new[] { "acervo_imagens", "acervo_publicacoes", "acervo_solicitacoes" })
                migrationBuilder.Sql($"""
                    ALTER TABLE {table} ENABLE ROW LEVEL SECURITY;
                    ALTER TABLE {table} FORCE ROW LEVEL SECURITY;
                    CREATE POLICY tenant_isolation ON {table}
                    USING (current_setting('app.bypass_rls', true) = 'true' OR "EmpresaId" = NULLIF(current_setting('app.empresa_id', true), '')::uuid)
                    WITH CHECK (current_setting('app.bypass_rls', true) = 'true' OR "EmpresaId" = NULLIF(current_setting('app.empresa_id', true), '')::uuid);
                    """);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "acervo_imagens");

            migrationBuilder.DropTable(
                name: "acervo_publicacoes");

            migrationBuilder.DropTable(
                name: "acervo_solicitacoes");
        }
    }
}
