using System.Reflection;
using EasyStock.Domain.Entities;
using EasyStock.Infra.Postgre.Migrations;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Migrations;
using Microsoft.EntityFrameworkCore.Migrations.Operations;

namespace EasyStock.Infra.Postgre.IntegrationTests.Migrations;

/// <summary>
/// #1781: a migração <c>NormalizarEmailUsuarios</c> grava o e-mail de quem já existe em trim + minúsculas, como o
/// cadastro e o login passaram a fazer. Linha cujo e-mail normalizado colide com o de outra conta não é tocada:
/// o índice único recusaria o UPDATE e derrubaria a migração inteira; a duplicata fica para decisão manual.
/// </summary>
[Collection("PostgreSqlTestCollection")]
public sealed class NormalizarEmailUsuariosMigrationTests(PostgreSqlDatabaseFixture fixture)
{
    [SkippableFact]
    public async Task NormalizaOsEmailsENaoTocaOsQueColidem()
    {
        Skip.If(!fixture.IsAvailable, fixture.UnavailableReason ?? "Docker/PostgreSQL indisponivel");
        var g = Guid.NewGuid().ToString("N");
        var solto = await SemearComEmailAsync($"  Solto-{g}@CasaDaBaba.com ");
        var jaNormal = await SemearComEmailAsync($"dup-{g}@casadababa.com");
        var colideComNormal = await SemearComEmailAsync($"Dup-{g}@CasaDaBaba.com");
        var colideA = await SemearComEmailAsync($"Par-{g}@casadababa.com");
        var colideB = await SemearComEmailAsync($"PAR-{g}@casadababa.com ");

        await using (var db = fixture.CreateDbContext())
        {
            foreach (var sql in SqlDoUp())
                await db.Database.ExecuteSqlRawAsync(sql);
        }

        await using var leitura = fixture.CreateDbContext();
        var emails = await leitura.Usuarios.AsNoTracking()
            .Where(u => new[] { solto, jaNormal, colideComNormal, colideA, colideB }.Contains(u.Id))
            .ToDictionaryAsync(u => u.Id, u => u.Email);
        emails[solto].Should().Be($"solto-{g}@casadababa.com");
        emails[jaNormal].Should().Be($"dup-{g}@casadababa.com");
        emails[colideComNormal].Should().Be($"Dup-{g}@CasaDaBaba.com", "colide com uma conta já normalizada");
        emails[colideA].Should().Be($"Par-{g}@casadababa.com", "as duas do par colidem entre si");
        emails[colideB].Should().Be($"PAR-{g}@casadababa.com ");
    }

    [Fact]
    public void DownNaoMexeEmNada()
    {
        // A grafia antiga não é recuperável e o e-mail normalizado é válido para o código anterior.
        Operacoes("Down").Should().BeEmpty();
    }

    private static IEnumerable<string> SqlDoUp() =>
        Operacoes("Up").Should().NotBeEmpty("o Up normaliza os dados existentes").And.Subject
            .Cast<SqlOperation>().Select(o => o.Sql);

    private static IReadOnlyList<MigrationOperation> Operacoes(string metodo)
    {
        var builder = new MigrationBuilder("Npgsql.EntityFrameworkCore.PostgreSQL");
        typeof(Migration).GetMethod(metodo, BindingFlags.Instance | BindingFlags.NonPublic)!
            .Invoke(new NormalizarEmailUsuarios(), [builder]);
        return builder.Operations;
    }

    private async Task<Guid> SemearComEmailAsync(string emailBruto)
    {
        var usuario = Usuario.Criar("Pessoa", $"semente-{Guid.NewGuid():N}@casadababa.com", "hash");
        await using var db = fixture.CreateDbContext();
        db.Usuarios.Add(usuario);
        await db.SaveChangesAsync();
        // Grafia de antes do #1781 (o cadastro só aparava): gravada direto, por fora do domínio.
        await db.Database.ExecuteSqlInterpolatedAsync($"UPDATE usuarios SET \"Email\" = {emailBruto} WHERE \"Id\" = {usuario.Id}");
        return usuario.Id;
    }
}
