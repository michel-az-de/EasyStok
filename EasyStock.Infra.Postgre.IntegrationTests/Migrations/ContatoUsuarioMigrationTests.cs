using System.Reflection;
using EasyStock.Domain.Entities;
using EasyStock.Infra.Postgre.Migrations;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Migrations;
using Microsoft.EntityFrameworkCore.Migrations.Operations;

namespace EasyStock.Infra.Postgre.IntegrationTests.Migrations;

/// <summary>N4: a migração <c>AddContatoUsuario</c> é aditiva (três colunas nulas em <c>usuarios</c>) e o <c>Down</c> remove só elas.</summary>
[Collection("PostgreSqlTestCollection")]
public sealed class ContatoUsuarioMigrationTests(PostgreSqlDatabaseFixture fixture)
{
    private static readonly string[] ColunasNovas = ["EmailPendente", "Telefone", "TelefoneVerificadoEm"];

    [SkippableFact]
    public async Task MigracaoPreservaUsuariosExistentes()
    {
        Skip.If(!fixture.IsAvailable, fixture.UnavailableReason ?? "Docker/PostgreSQL indisponivel");
        var ana = Usuario.Criar("Ana", $"ana-{Guid.NewGuid():N}@casadababa.com", "hash");
        ana.EmailConfirmado = true;
        await using (var escrita = fixture.CreateDbContext())
        {
            escrita.Usuarios.Add(ana);
            await escrita.SaveChangesAsync();
        }

        await using var db = fixture.CreateDbContext();
        var colunas = await db.Database.SqlQueryRaw<string>("""
            SELECT column_name || '|' || data_type || '|' || is_nullable || '|' || COALESCE(column_default, 'sem-default') AS "Value"
            FROM information_schema.columns
            WHERE table_schema = current_schema() AND table_name = 'usuarios'
              AND column_name IN ('EmailPendente', 'Telefone', 'TelefoneVerificadoEm')
            ORDER BY column_name
            """).ToListAsync();

        colunas.Should().Equal(
            "EmailPendente|character varying|YES|sem-default",
            "Telefone|character varying|YES|sem-default",
            "TelefoneVerificadoEm|timestamp with time zone|YES|sem-default");

        var lido = await db.Usuarios.AsNoTracking().SingleAsync(u => u.Id == ana.Id);
        lido.Email.Should().Be(ana.Email);
        lido.EmailConfirmado.Should().BeTrue();
        lido.Telefone.Should().BeNull();
        lido.TelefoneVerificadoEm.Should().BeNull();
        lido.EmailPendente.Should().BeNull();
    }

    [Fact]
    public void DownRemoveSoAsColunasNovas()
    {
        var migracao = new AddContatoUsuario();
        var builder = new MigrationBuilder("Npgsql.EntityFrameworkCore.PostgreSQL");
        typeof(Migration).GetMethod("Down", BindingFlags.Instance | BindingFlags.NonPublic)!
            .Invoke(migracao, [builder]);

        builder.Operations.Should().OnlyContain(o => o is DropColumnOperation);
        builder.Operations.Cast<DropColumnOperation>()
            .Select(o => (o.Table, o.Name))
            .Should().BeEquivalentTo(ColunasNovas.Select(c => ("usuarios", c)));
    }
}
