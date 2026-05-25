using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace EasyStock.Infra.Postgre.Migrations
{
    /// <summary>
    /// Onda 2.2 — cria tabela notif_web_push_subscriptions para Web Push (PWA).
    /// Migration escrita a mao porque o build agregado de Application estava quebrado
    /// por outros WIPs quando o "dotnet ef migrations add" rodou. Schema reflete o
    /// WebPushSubscriptionConfiguration.cs.
    /// </summary>
    public partial class AddWebPushSubscription : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "notif_web_push_subscriptions",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    EmpresaId = table.Column<Guid>(type: "uuid", nullable: true),
                    UsuarioId = table.Column<Guid>(type: "uuid", nullable: true),
                    Endpoint = table.Column<string>(type: "character varying(2000)", maxLength: 2000, nullable: false),
                    P256dh = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    Auth = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    UserAgent = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: true),
                    CriadoEm = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    UltimoUso = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    Ativo = table.Column<bool>(type: "boolean", nullable: false, defaultValue: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_notif_web_push_subscriptions", x => x.Id);
                    table.ForeignKey(
                        name: "FK_notif_web_push_subscriptions_Empresas_EmpresaId",
                        column: x => x.EmpresaId,
                        principalTable: "empresas",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_notif_web_push_subscriptions_Usuarios_UsuarioId",
                        column: x => x.UsuarioId,
                        principalTable: "usuarios",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "ux_web_push_endpoint",
                table: "notif_web_push_subscriptions",
                column: "Endpoint",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_web_push_usuario_ativo",
                table: "notif_web_push_subscriptions",
                columns: new[] { "UsuarioId", "Ativo" });

            migrationBuilder.CreateIndex(
                name: "ix_web_push_empresa_ativo",
                table: "notif_web_push_subscriptions",
                columns: new[] { "EmpresaId", "Ativo" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "notif_web_push_subscriptions");
        }
    }
}
