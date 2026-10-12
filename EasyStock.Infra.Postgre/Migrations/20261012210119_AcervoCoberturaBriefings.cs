using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace EasyStock.Infra.Postgre.Migrations
{
    /// <inheritdoc />
    public partial class AcervoCoberturaBriefings : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<Guid>(
                name: "BriefingId",
                table: "acervo_solicitacoes",
                type: "uuid",
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "Altura",
                table: "acervo_imagens",
                type: "integer",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.AddColumn<int>(
                name: "Largura",
                table: "acervo_imagens",
                type: "integer",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.AddColumn<string>(
                name: "Natureza",
                table: "acervo_imagens",
                type: "text",
                nullable: false,
                defaultValue: "NaoClassificada");

            migrationBuilder.CreateTable(
                name: "acervo_briefings",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    EmpresaId = table.Column<Guid>(type: "uuid", nullable: false),
                    UsuarioId = table.Column<Guid>(type: "uuid", nullable: false),
                    ItemId = table.Column<Guid>(type: "uuid", nullable: false),
                    RaizId = table.Column<Guid>(type: "uuid", nullable: false),
                    AnteriorId = table.Column<Guid>(type: "uuid", nullable: true),
                    Versao = table.Column<int>(type: "integer", nullable: false),
                    Canal = table.Column<string>(type: "text", nullable: false),
                    Finalidade = table.Column<string>(type: "text", nullable: false),
                    Formato = table.Column<string>(type: "text", nullable: false),
                    ReferenciasJson = table.Column<string>(type: "jsonb", nullable: false),
                    Restricoes = table.Column<string>(type: "text", nullable: false),
                    Prompt = table.Column<string>(type: "text", nullable: false),
                    ConteudoHash = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    CriadoEm = table.Column<DateTime>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_acervo_briefings", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "acervo_usos",
                columns: table => new
                {
                    EmpresaId = table.Column<Guid>(type: "uuid", nullable: false),
                    ImagemId = table.Column<Guid>(type: "uuid", nullable: false),
                    Canal = table.Column<string>(type: "text", nullable: false),
                    LarguraMm = table.Column<decimal>(type: "numeric", nullable: true),
                    AlturaMm = table.Column<decimal>(type: "numeric", nullable: true),
                    UsuarioId = table.Column<Guid>(type: "uuid", nullable: false),
                    ConfirmadoEm = table.Column<DateTime>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_acervo_usos", x => new { x.EmpresaId, x.ImagemId, x.Canal });
                });

            migrationBuilder.CreateIndex(
                name: "IX_acervo_solicitacoes_EmpresaId_BriefingId",
                table: "acervo_solicitacoes",
                columns: new[] { "EmpresaId", "BriefingId" },
                unique: true,
                filter: "\"BriefingId\" IS NOT NULL");

            migrationBuilder.CreateIndex(
                name: "IX_acervo_briefings_EmpresaId_ConteudoHash",
                table: "acervo_briefings",
                columns: new[] { "EmpresaId", "ConteudoHash" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_acervo_briefings_EmpresaId_RaizId_Versao",
                table: "acervo_briefings",
                columns: new[] { "EmpresaId", "RaizId", "Versao" },
                unique: true);
            foreach (var table in new[] { "acervo_briefings", "acervo_usos" })
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
                name: "acervo_briefings");

            migrationBuilder.DropTable(
                name: "acervo_usos");

            migrationBuilder.DropIndex(
                name: "IX_acervo_solicitacoes_EmpresaId_BriefingId",
                table: "acervo_solicitacoes");

            migrationBuilder.DropColumn(
                name: "BriefingId",
                table: "acervo_solicitacoes");

            migrationBuilder.DropColumn(
                name: "Altura",
                table: "acervo_imagens");

            migrationBuilder.DropColumn(
                name: "Largura",
                table: "acervo_imagens");

            migrationBuilder.DropColumn(
                name: "Natureza",
                table: "acervo_imagens");
        }
    }
}
