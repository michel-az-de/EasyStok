using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace EasyStock.Infra.Postgre.Migrations
{
    /// <inheritdoc />
    public partial class AddTipoEmbalagemToProduto : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            // C2 (RDC 727/2022): peso obrigatorio na etiqueta SO para Embalado.
            // Default "Avulso" mantem comportamento atual (lotes existentes imunes)
            // ate triagem manual pelo operador no /produtos pos-deploy.
            migrationBuilder.AddColumn<string>(
                name: "TipoEmbalagem",
                table: "produtos",
                type: "character varying(20)",
                maxLength: 20,
                nullable: false,
                defaultValue: "Avulso");

            // NOTA: A geracao automatica do dotnet ef inclui tambem CreateTable
            //   "notif_web_push_subscriptions" pendente no modelo mas sem migration
            //   propria. Removido daqui para manter este commit focado em C2.
            //   Web Push fica para migration separada quando alguem investigar.
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "TipoEmbalagem",
                table: "produtos");
        }
    }
}
