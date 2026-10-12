using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace EasyStock.Infra.Postgre.Migrations
{
    /// <inheritdoc />
    public partial class AddApuracaoOcorrencia : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<DateTime>(
                name: "ApuradaEm",
                table: "ocorrencias",
                type: "timestamp with time zone",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "ApuradaPorNome",
                table: "ocorrencias",
                type: "character varying(120)",
                maxLength: 120,
                nullable: true);

            migrationBuilder.AddColumn<Guid>(
                name: "ApuradaPorUsuarioId",
                table: "ocorrencias",
                type: "uuid",
                nullable: true);

            migrationBuilder.AddColumn<DateTime>(
                name: "ReembolsoSolicitadoEm",
                table: "ocorrencias",
                type: "timestamp with time zone",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "ResolvidaPorNome",
                table: "ocorrencias",
                type: "character varying(120)",
                maxLength: 120,
                nullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql("""
                SET LOCAL row_security = off;
                DO $$ BEGIN
                    IF EXISTS (SELECT 1 FROM ocorrencias WHERE "ApuradaEm" IS NOT NULL
                        OR "ReembolsoSolicitadoEm" IS NOT NULL OR "ResolvidaPorNome" IS NOT NULL) THEN
                        RAISE EXCEPTION 'Existem ocorrências com auditoria ou reembolso em andamento. Preserve o histórico antes de reverter.';
                    END IF;
                END $$;
                """);
            migrationBuilder.DropColumn(
                name: "ApuradaEm",
                table: "ocorrencias");

            migrationBuilder.DropColumn(
                name: "ApuradaPorNome",
                table: "ocorrencias");

            migrationBuilder.DropColumn(
                name: "ApuradaPorUsuarioId",
                table: "ocorrencias");

            migrationBuilder.DropColumn(
                name: "ReembolsoSolicitadoEm",
                table: "ocorrencias");

            migrationBuilder.DropColumn(
                name: "ResolvidaPorNome",
                table: "ocorrencias");
        }
    }
}
