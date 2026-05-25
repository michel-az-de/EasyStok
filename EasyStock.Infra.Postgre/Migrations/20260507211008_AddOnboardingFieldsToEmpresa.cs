using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace EasyStock.Infra.Postgre.Migrations
{
    /// <inheritdoc />
    public partial class AddOnboardingFieldsToEmpresa : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "NomeFantasia",
                table: "empresas",
                type: "character varying(150)",
                maxLength: 150,
                nullable: true);

            migrationBuilder.AddColumn<bool>(
                name: "OnboardingCompleto",
                table: "empresas",
                type: "boolean",
                nullable: false,
                defaultValue: false);

            migrationBuilder.AddColumn<DateTime>(
                name: "OnboardingCompletoEm",
                table: "empresas",
                type: "timestamp with time zone",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "Segmento",
                table: "empresas",
                type: "character varying(40)",
                maxLength: 40,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "Telefone",
                table: "empresas",
                type: "character varying(30)",
                maxLength: 30,
                nullable: true);

            // Backfill: empresas pre-existentes contam como ja onboarded.
            // Wizard so aparece pra signups novos depois deste deploy.
            migrationBuilder.Sql(
                "UPDATE empresas SET \"OnboardingCompleto\" = TRUE, \"OnboardingCompletoEm\" = \"CriadoEm\" WHERE \"CriadoEm\" < NOW();");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(name: "NomeFantasia", table: "empresas");
            migrationBuilder.DropColumn(name: "OnboardingCompleto", table: "empresas");
            migrationBuilder.DropColumn(name: "OnboardingCompletoEm", table: "empresas");
            migrationBuilder.DropColumn(name: "Segmento", table: "empresas");
            migrationBuilder.DropColumn(name: "Telefone", table: "empresas");
        }
    }
}
