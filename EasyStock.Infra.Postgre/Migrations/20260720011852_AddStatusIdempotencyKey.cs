using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace EasyStock.Infra.Postgre.Migrations
{
    /// <inheritdoc />
    public partial class AddStatusIdempotencyKey : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            // issue 917 Fase B: migration aditiva. Linhas EXISTENTES foram gravadas pelo
            // codigo antigo, que so salvava DEPOIS de completar com 2xx -- por definicao,
            // toda linha ja existente representa uma operacao concluida. O default 0
            // (Pendente) das colunas abaixo e so pra satisfazer o NOT NULL durante o ADD;
            // o backfill logo em seguida corrige pra Concluido antes de qualquer INSERT novo.
            migrationBuilder.AddColumn<DateTime>(
                name: "LockedAtUtc",
                table: "idempotency_keys",
                type: "timestamp with time zone",
                nullable: false,
                defaultValue: new DateTime(1, 1, 1, 0, 0, 0, 0, DateTimeKind.Unspecified));

            migrationBuilder.AddColumn<string>(
                name: "PayloadHash",
                table: "idempotency_keys",
                type: "character varying(64)",
                maxLength: 64,
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "Status",
                table: "idempotency_keys",
                type: "integer",
                nullable: false,
                defaultValue: 0);

            // Backfill: toda linha pre-existente e' Concluido (1), com LockedAtUtc = CriadoEm
            // (valor sensato pra uma coluna que so importa pro lease de linhas Pendentes).
            migrationBuilder.Sql(
                """
                UPDATE idempotency_keys
                SET "Status" = 1, "LockedAtUtc" = "CriadoEm"
                WHERE "Status" = 0;
                """);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "LockedAtUtc",
                table: "idempotency_keys");

            migrationBuilder.DropColumn(
                name: "PayloadHash",
                table: "idempotency_keys");

            migrationBuilder.DropColumn(
                name: "Status",
                table: "idempotency_keys");
        }
    }
}
