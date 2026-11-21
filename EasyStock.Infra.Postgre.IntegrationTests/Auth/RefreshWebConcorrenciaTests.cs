using EasyStock.Application.Ports.Output;
using EasyStock.Application.UseCases.Common;
using EasyStock.Application.UseCases.RefreshToken;
using EasyStock.Domain.Entities;
using EasyStock.Domain.Exceptions;
using EasyStock.Infra.Postgre.Repositories;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using NSubstitute;

namespace EasyStock.Infra.Postgre.IntegrationTests.Auth;

/// <summary>
/// #2745: a renovação do navegador (sem sessão nativa) em Postgres real. Cada chamada usa o próprio
/// <c>EasyStockDbContext</c>, como requisições simultâneas na API; o mesmo refresh só pode render um par novo.
/// </summary>
[Collection("PostgreSqlTestCollection")]
public sealed class RefreshWebConcorrenciaTests(PostgreSqlDatabaseFixture fixture)
{
    private const string Segredo = "refresh-web-sintetico-2745";

    [SkippableFact]
    public async Task RenovacoesSimultaneasDoMesmoRefreshRendemUmSoSucessor()
    {
        Skip.If(!fixture.IsAvailable, fixture.UnavailableReason ?? "Docker/PostgreSQL indisponivel");
        await fixture.ResetDatabaseAsync();
        var usuarioId = await SemearAsync();

        var respostas = await Task.WhenAll(Enumerable.Range(0, 8).Select(_ => TentarAsync()));

        respostas.Count(ok => ok).Should().Be(1, "o mesmo refresh apresentado em paralelo só pode ser consumido uma vez");
        await using var leitura = fixture.CreateDbContext();
        (await leitura.RefreshTokens.CountAsync(t => t.UsuarioId == usuarioId && !t.Revogado))
            .Should().Be(1, "fica vivo só o sucessor da renovação que venceu");
    }

    private async Task<bool> TentarAsync()
    {
        await using var db = fixture.CreateDbContext();
        var renovar = new RefreshTokenUseCase(new RefreshTokenRepository(db), new UsuarioRepository(db),
            new AuditLogRepository(db), Substitute.For<IJwtTokenService>(), db, NullLogger<RefreshTokenUseCase>.Instance);
        try
        {
            await renovar.ExecuteAsync(new RefreshTokenCommand(Segredo));
            return true;
        }
        catch (CredenciaisInvalidasException)
        {
            return false;
        }
    }

    private async Task<Guid> SemearAsync()
    {
        await using var db = fixture.CreateDbContext();
        var empresa = Empresa.Criar("Laboratório 2745", null);
        var usuario = Usuario.Criar("Ana", "ana@2745.test", "hash");
        usuario.Empresas.Add(new UsuarioEmpresa { UsuarioId = usuario.Id, EmpresaId = empresa.Id, Ativo = true });
        var refresh = RefreshToken.Criar(usuario.Id, TokenHashHelper.ComputeSha256Hash(Segredo), DateTime.UtcNow.AddDays(7), null, null);
        db.AddRange(empresa, usuario, refresh);
        await db.SaveChangesAsync();
        return usuario.Id;
    }
}
