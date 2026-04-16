using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace EasyStock.Infra.Postgre.Migrations
{
    /// <inheritdoc />
    public partial class AuditoriaCompletaProduto : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<Guid>(
                name: "AlteradoPor",
                table: "produtos",
                type: "uuid",
                nullable: true);

            migrationBuilder.AddColumn<Guid>(
                name: "CriadoPor",
                table: "produtos",
                type: "uuid",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "ObservacaoInterna",
                table: "produtos",
                type: "character varying(1000)",
                maxLength: 1000,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "Motivo",
                table: "produto_alteracoes",
                type: "character varying(100)",
                maxLength: 100,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "Observacao",
                table: "produto_alteracoes",
                type: "character varying(500)",
                maxLength: 500,
                nullable: true);

            // Backfill CriadoPor do primeiro registro de auditoria "cadastrado"
            migrationBuilder.Sql("""
                UPDATE produtos p
                SET "CriadoPor" = pa."UsuarioId"
                FROM produto_alteracoes pa
                WHERE pa."ProdutoId" = p."Id"
                  AND pa."Acao" = 'cadastrado'
                  AND p."CriadoPor" IS NULL;
                """);

            // Backfill AlteradoPor do registro mais recente por produto
            migrationBuilder.Sql("""
                UPDATE produtos p
                SET "AlteradoPor" = sub."UsuarioId"
                FROM (
                    SELECT DISTINCT ON ("ProdutoId") "ProdutoId", "UsuarioId"
                    FROM produto_alteracoes
                    ORDER BY "ProdutoId", "AlteradoEm" DESC
                ) sub
                WHERE sub."ProdutoId" = p."Id"
                  AND p."AlteradoPor" IS NULL;
                """);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "AlteradoPor",
                table: "produtos");

            migrationBuilder.DropColumn(
                name: "CriadoPor",
                table: "produtos");

            migrationBuilder.DropColumn(
                name: "ObservacaoInterna",
                table: "produtos");

            migrationBuilder.DropColumn(
                name: "Motivo",
                table: "produto_alteracoes");

            migrationBuilder.DropColumn(
                name: "Observacao",
                table: "produto_alteracoes");
        }
    }
}
