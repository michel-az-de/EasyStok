using EasyStock.Domain.Entities;
using EasyStock.Infra.Postgre.Repositories;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;

namespace EasyStock.Infra.Postgre.IntegrationTests.Auth;

/// <summary>
/// #1566: a limpeza em lote (<c>ExecuteDelete</c>) dos refresh tokens e das confirmações de e-mail em Postgres real. O
/// corte é o do chamador (<c>LimpezaTokensAcesso</c>: expirados há mais de 24 h), revogados e confirmados inclusive.
/// </summary>
[Collection("PostgreSqlTestCollection")]
public sealed class LimpezaTokensExpiradosIntegrationTests(PostgreSqlDatabaseFixture fixture)
{
    [SkippableFact]
    public async Task RefreshTokens_apaga_so_os_que_expiraram_antes_do_corte()
    {
        Skip.If(!fixture.IsAvailable, fixture.UnavailableReason ?? "Docker/PostgreSQL indisponivel");
        var agora = DateTime.UtcNow;
        var usuario = await SemearUsuarioAsync();
        RefreshToken Token(DateTime expiraEm) => RefreshToken.Criar(usuario.Id, $"hash-{Guid.NewGuid():N}", expiraEm, null, null);
        var expiradoHa25h = Token(agora.AddHours(-25));
        var revogadoExpiradoHa30h = Token(agora.AddHours(-30));
        revogadoExpiradoHa30h.Revogar();
        var expiradoHa23h = Token(agora.AddHours(-23));
        var vigente = Token(agora.AddDays(7));
        await using (var db = fixture.CreateDbContext())
        {
            db.RefreshTokens.AddRange(expiradoHa25h, revogadoExpiradoHa30h, expiradoHa23h, vigente);
            await db.SaveChangesAsync();
        }

        int apagados;
        await using (var db = fixture.CreateDbContext())
            apagados = await new RefreshTokenRepository(db).DeleteExpiredAsync(agora.AddHours(-24));

        apagados.Should().BeGreaterThanOrEqualTo(2);
        await using var leitura = fixture.CreateDbContext();
        var restantes = await leitura.RefreshTokens.AsNoTracking().Select(t => t.Id).ToListAsync();
        restantes.Should().NotContain([expiradoHa25h.Id, revogadoExpiradoHa30h.Id]);
        restantes.Should().Contain([expiradoHa23h.Id, vigente.Id]);
    }

    [SkippableFact]
    public async Task ConfirmacoesDeEmail_apaga_so_as_que_expiraram_antes_do_corte()
    {
        Skip.If(!fixture.IsAvailable, fixture.UnavailableReason ?? "Docker/PostgreSQL indisponivel");
        var agora = DateTime.UtcNow;
        var usuario = await SemearUsuarioAsync();
        EmailConfirmationToken Token(DateTime expiraEm)
        {
            var token = EmailConfirmationToken.Criar(usuario.Id, $"hash-{Guid.NewGuid():N}", null, null);
            token.ExpiraEm = expiraEm;
            return token;
        }
        var expiradoHa25h = Token(agora.AddHours(-25));
        var confirmadoExpiradoHa30h = Token(agora.AddHours(-30));
        confirmadoExpiradoHa30h.MarcarComoConfirmado();
        var expiradoHa23h = Token(agora.AddHours(-23));
        var vigente = Token(agora.AddHours(24));
        await using (var db = fixture.CreateDbContext())
        {
            db.EmailConfirmationTokens.AddRange(expiradoHa25h, confirmadoExpiradoHa30h, expiradoHa23h, vigente);
            await db.SaveChangesAsync();
        }

        int apagados;
        await using (var db = fixture.CreateDbContext())
            apagados = await new EmailConfirmationTokenRepository(db).DeleteExpiredAsync(agora.AddHours(-24));

        apagados.Should().BeGreaterThanOrEqualTo(2);
        await using var leitura = fixture.CreateDbContext();
        var restantes = await leitura.EmailConfirmationTokens.AsNoTracking().Select(t => t.Id).ToListAsync();
        restantes.Should().NotContain([expiradoHa25h.Id, confirmadoExpiradoHa30h.Id]);
        restantes.Should().Contain([expiradoHa23h.Id, vigente.Id]);
    }

    private async Task<Usuario> SemearUsuarioAsync()
    {
        var usuario = Usuario.Criar("Ana", $"ana-{Guid.NewGuid():N}@casadababa.com", "hash");
        await using var db = fixture.CreateDbContext();
        db.Usuarios.Add(usuario);
        await db.SaveChangesAsync();
        return usuario;
    }
}
