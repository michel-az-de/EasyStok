using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace EasyStock.Infra.Postgre.Migrations
{
    /// <summary>
    /// #1781: grava o e-mail de quem já existe como o cadastro e o login passaram a gravar e buscar
    /// (<c>Usuario.NormalizarEmail</c>: trim + minúsculas). Só dados: o índice único de <c>Email</c> fica como está,
    /// porque com todo e-mail gravado normalizado ele já recusa <c>Ana@x.com</c> ao lado de <c>ana@x.com</c>; um
    /// índice único em <c>lower("Email")</c> falharia na criação se houvesse duplicata e derrubaria o deploy.
    /// </summary>
    public partial class NormalizarEmailUsuarios : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            // Linha cujo e-mail normalizado colide com o de outra conta (Ana@x.com e ana@x.com) NÃO é alterada: o
            // índice único recusaria o UPDATE e a migração inteira cairia. As duas contas seguem como estão (a que
            // já está normalizada entra pelo login; a outra fica sem login por senha) até alguém decidir qual vale.
            // Para achar as duplicatas: SELECT lower(btrim("Email")), count(*) FROM usuarios GROUP BY 1 HAVING count(*) > 1;
            migrationBuilder.Sql("""
                UPDATE usuarios u
                SET "Email" = lower(btrim(u."Email", E' \t\r\n'))
                WHERE u."Email" <> lower(btrim(u."Email", E' \t\r\n'))
                  AND NOT EXISTS (
                      SELECT 1 FROM usuarios o
                      WHERE o."Id" <> u."Id"
                        AND lower(btrim(o."Email", E' \t\r\n')) = lower(btrim(u."Email", E' \t\r\n')));
                """);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            // Sem volta: a grafia antiga não é recuperável, e o e-mail normalizado vale também para o código anterior.
        }
    }
}
