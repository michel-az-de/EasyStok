using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace EasyStock.Infra.Postgre.Migrations
{
    /// <inheritdoc />
    public partial class AdicionaLeadsPublicos : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "leads_publicos",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    Nome = table.Column<string>(type: "character varying(150)", maxLength: 150, nullable: false),
                    Email = table.Column<string>(type: "character varying(255)", maxLength: 255, nullable: false),
                    Telefone = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: true),
                    Empresa = table.Column<string>(type: "character varying(150)", maxLength: 150, nullable: true),
                    Mensagem = table.Column<string>(type: "text", nullable: true),
                    Origem = table.Column<int>(type: "integer", nullable: false),
                    TipoNegocio = table.Column<string>(type: "character varying(80)", maxLength: 80, nullable: true),
                    ReceberNewsletter = table.Column<bool>(type: "boolean", nullable: false),
                    ConsentimentoLgpd = table.Column<bool>(type: "boolean", nullable: false),
                    IpOrigem = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: true),
                    UserAgent = table.Column<string>(type: "character varying(512)", maxLength: 512, nullable: true),
                    UtmSource = table.Column<string>(type: "character varying(120)", maxLength: 120, nullable: true),
                    UtmMedium = table.Column<string>(type: "character varying(120)", maxLength: 120, nullable: true),
                    UtmCampaign = table.Column<string>(type: "character varying(120)", maxLength: 120, nullable: true),
                    CriadoEm = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    ProcessadoEm = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    TicketGeradoId = table.Column<Guid>(type: "uuid", nullable: true),
                    EmpresaCriadaId = table.Column<Guid>(type: "uuid", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_leads_publicos", x => x.Id);
                });

            migrationBuilder.CreateIndex(
                name: "IX_leads_publicos_CriadoEm",
                table: "leads_publicos",
                column: "CriadoEm");

            migrationBuilder.CreateIndex(
                name: "IX_leads_publicos_IpOrigem",
                table: "leads_publicos",
                column: "IpOrigem");

            migrationBuilder.CreateIndex(
                name: "IX_leads_publicos_Origem",
                table: "leads_publicos",
                column: "Origem");

            migrationBuilder.CreateIndex(
                name: "IX_leads_publicos_ProcessadoEm",
                table: "leads_publicos",
                column: "ProcessadoEm");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "leads_publicos");
        }
    }
}
