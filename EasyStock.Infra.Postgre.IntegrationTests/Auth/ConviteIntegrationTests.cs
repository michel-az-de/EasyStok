using EasyStock.Application.Ports.Output;
using EasyStock.Application.Ports.Output.Notifications;
using EasyStock.Application.Ports.Output.Persistence;
using EasyStock.Application.Services.Auth;
using EasyStock.Application.UseCases.AceitarConvite;
using EasyStock.Domain.Entities;
using EasyStock.Domain.Exceptions;
using EasyStock.Domain.Enums.Notifications;
using EasyStock.Domain.ValueObjects;
using EasyStock.Infra.Async;
using EasyStock.Infra.Postgre.Repositories;
using EasyStock.Infra.Postgre.Repositories.Notifications;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging.Abstractions;
using NSubstitute;

namespace EasyStock.Infra.Postgre.IntegrationTests.Auth;

/// <summary>
/// N9: o aceite do convite em Postgres real. Cada chamada usa o próprio <c>EasyStockDbContext</c> (uma conexão por
/// requisição concorrente), como a API, e o <c>UPDATE</c> condicional é o que decide o único vencedor.
/// </summary>
[Collection("PostgreSqlTestCollection")]
public sealed class ConviteIntegrationTests(PostgreSqlDatabaseFixture fixture)
{
    private const string NovaSenha = "Nova@Senha123";

    [SkippableFact]
    public async Task DoisAceitesSimultaneosUmSoVence()
    {
        Skip.If(!fixture.IsAvailable, fixture.UnavailableReason ?? "Docker/PostgreSQL indisponivel");
        var usuario = await SemearConvidadoAsync();
        var (email, _) = await SemearConvitesAsync(usuario);

        var resultados = await Task.WhenAll(Enumerable.Range(0, 2).Select(async _ =>
        {
            await using var db = fixture.CreateDbContext();
            try
            {
                await UseCase(db).ExecuteAsync(new AceitarConviteCommand(email, NovaSenha, "198.51.100.1", null));
                return true;
            }
            catch (RegraDeDominioVioladaException)
            {
                return false;
            }
        }));

        resultados.Count(r => r).Should().Be(1, "dois POST simultaneos com o mesmo convite: 1 sucesso e 1 recusa");
        var gravado = await LerUsuarioAsync(usuario.Id);
        gravado.ConvitePendente.Should().BeFalse();
        gravado.EmailConfirmado.Should().BeTrue();
        new BCryptPasswordHasher().Verify(NovaSenha, gravado.SenhaHash).Should().BeTrue();
    }

    [SkippableTheory]
    [InlineData("Email")]
    [InlineData("WhatsApp")]
    public async Task AceitarFixaEmailConfirmadoOuTelefoneVerificado(string canal)
    {
        Skip.If(!fixture.IsAvailable, fixture.UnavailableReason ?? "Docker/PostgreSQL indisponivel");
        var usuario = await SemearConvidadoAsync();
        var (email, whats) = await SemearConvitesAsync(usuario);

        await using (var db = fixture.CreateDbContext())
            await UseCase(db).ExecuteAsync(new AceitarConviteCommand(canal == "Email" ? email : whats, NovaSenha, "198.51.100.2", "Teste/1.0"));

        var gravado = await LerUsuarioAsync(usuario.Id);
        gravado.ConvitePendente.Should().BeFalse();
        gravado.ConviteAceitoEm.Should().NotBeNull();
        if (canal == "Email")
        {
            gravado.EmailConfirmado.Should().BeTrue();
            gravado.TelefoneVerificadoEm.Should().BeNull();
            gravado.ConviteAceitoVia.Should().Be("e-mail");
        }
        else
        {
            gravado.EmailConfirmado.Should().BeFalse();
            gravado.TelefoneVerificadoEm.Should().NotBeNull();
            gravado.ConviteAceitoVia.Should().Be("+55•••1234").And.NotContain("999991234");
        }

        await using var leitura = fixture.CreateDbContext();
        (await leitura.ResetTokens.AsNoTracking().Where(t => t.UsuarioId == usuario.Id).ToListAsync())
            .Should().OnlyContain(t => t.Usado, "o convite usado e o do outro canal morrem juntos");
        var consentimentos = await leitura.NotifConsentimentos.AsNoTracking().Where(c => c.UsuarioId == usuario.Id).ToListAsync();
        if (canal == "WhatsApp")
            consentimentos.Select(c => c.Categoria).Should().BeEquivalentTo(
                [CategoriaConteudoNotificacao.Seguranca, CategoriaConteudoNotificacao.Operacional]);
        else
            consentimentos.Should().BeEmpty();
    }

    [SkippableFact]
    public async Task MigracaoMarcaTokensExistentesComoReset()
    {
        Skip.If(!fixture.IsAvailable, fixture.UnavailableReason ?? "Docker/PostgreSQL indisponivel");
        var usuario = await SemearConvidadoAsync();
        await using var db = fixture.CreateDbContext();

        // Insert como o codigo anterior a N8: sem Finalidade. O DEFAULT da migracao da N8 vale e o convite da N9 nao o toca.
        var id = Guid.NewGuid();
        var hash = $"hash-antigo-{id:N}";
        await db.Database.ExecuteSqlInterpolatedAsync($"""
            INSERT INTO reset_tokens ("Id", "UsuarioId", "TokenHash", "CriadoEm", "ExpiraEm", "Usado")
            VALUES ({id}, {usuario.Id}, {hash}, now(), now() + interval '30 minutes', false)
            """);
        var repo = new ResetTokenRepository(db);

        (await repo.InvalidarConvitesAbertosAsync(usuario.Id)).Should().Be(0, "a linha antiga e Reset, nao Convite");
        (await db.ResetTokens.AsNoTracking().SingleAsync(t => t.Id == id)).Should().Match<ResetToken>(
            t => t.Finalidade == FinalidadeResetToken.Reset && !t.Usado);

        // A N9 so acrescenta duas colunas nulas a usuarios; quem ja existia nao ganha convite nem via.
        var colunas = await db.Database.SqlQueryRaw<string>("""
            SELECT column_name || '|' || data_type || '|' || is_nullable || '|' || COALESCE(column_default, 'sem-default') AS "Value"
            FROM information_schema.columns
            WHERE table_schema = current_schema() AND table_name = 'usuarios'
              AND column_name IN ('ConviteAceitoEm', 'ConviteAceitoVia')
            ORDER BY column_name
            """).ToListAsync();
        colunas.Should().Equal(
            "ConviteAceitoEm|timestamp with time zone|YES|sem-default",
            "ConviteAceitoVia|character varying|YES|sem-default");
        var existente = await LerUsuarioAsync(usuario.Id);
        existente.ConviteAceitoEm.Should().BeNull();
        existente.ConviteAceitoVia.Should().BeNull();
    }

    [SkippableFact]
    public async Task InvalidarConvitesMataSoConviteDoUsuarioEContaUmaEmissaoPorEmail()
    {
        Skip.If(!fixture.IsAvailable, fixture.UnavailableReason ?? "Docker/PostgreSQL indisponivel");
        var usuario = await SemearConvidadoAsync();
        await SemearConvitesAsync(usuario);
        var reset = await SemearTokenAsync(usuario, FinalidadeResetToken.Reset, "Email");
        var deOutro = await SemearConvidadoAsync();
        await SemearConvitesAsync(deOutro);
        await using var db = fixture.CreateDbContext();
        var repo = new ResetTokenRepository(db);

        (await repo.ContarEmissoesDeConviteAsync(usuario.Id, DateTime.UtcNow.AddHours(-1))).Should().Be(1, "so a linha de e-mail conta, uma por emissao");
        (await repo.ContarEmissoesDeConviteAsync(usuario.Id, DateTime.UtcNow.AddMinutes(1))).Should().Be(0);

        (await repo.InvalidarConvitesAbertosAsync(usuario.Id)).Should().Be(2);

        await using var leitura = fixture.CreateDbContext();
        (await leitura.ResetTokens.AsNoTracking().SingleAsync(t => t.Id == reset.Id)).Usado.Should().BeFalse("reset nao e convite");
        (await leitura.ResetTokens.AsNoTracking().Where(t => t.UsuarioId == deOutro.Id).ToListAsync())
            .Should().OnlyContain(t => !t.Usado);
    }

    private AceitarConviteUseCase UseCase(EasyStock.Infra.Postgre.Data.EasyStockDbContext db)
    {
        var tokens = new ResetTokenRepository(db);
        var consentimentos = new ConsentimentoRepository(db);
        var cache = Substitute.For<ICacheService>();
        cache.IncrementAsync(Arg.Any<string>(), Arg.Any<long>()).Returns(1L);
        var convites = new ConvitesDeAcesso(
            tokens, Substitute.For<INotificadorService>(), consentimentos, Substitute.For<IEmpresaRepository>(),
            new ConfigurationBuilder().Build(), TimeProvider.System, NullLogger<ConvitesDeAcesso>.Instance);
        return new AceitarConviteUseCase(
            tokens, new UsuarioRepository(db), consentimentos, new AuditLogRepository(db), convites,
            new LimitePedidosAcesso(cache), new BCryptPasswordHasher(), db, TimeProvider.System,
            NullLogger<AceitarConviteUseCase>.Instance);
    }

    private async Task<Usuario> SemearConvidadoAsync()
    {
        var usuario = Usuario.CriarConvidado("Ana", $"ana-{Guid.NewGuid():N}@casadababa.com");
        usuario.DefinirTelefone(TelefoneE164.From("+5511999991234"));
        await using var db = fixture.CreateDbContext();
        db.Usuarios.Add(usuario);
        await db.SaveChangesAsync();
        return usuario;
    }

    /// <summary>Um convite por canal, como a emissão grava: devolve o texto de cada token (o banco guarda só o hash).</summary>
    private async Task<(string Email, string Whats)> SemearConvitesAsync(Usuario usuario)
    {
        var email = SegredosDeAcesso.GerarLink();
        var whats = SegredosDeAcesso.GerarLink();
        await using var db = fixture.CreateDbContext();
        db.ResetTokens.Add(ResetToken.Criar(
            usuario.Id, SegredosDeAcesso.HashDoLink(email), DateTime.UtcNow.AddHours(72), null, null,
            FinalidadeResetToken.Convite, canal: "Email"));
        db.ResetTokens.Add(ResetToken.Criar(
            usuario.Id, SegredosDeAcesso.HashDoLink(whats), DateTime.UtcNow.AddHours(72), null, null,
            FinalidadeResetToken.Convite, canal: "WhatsApp"));
        await db.SaveChangesAsync();
        return (email, whats);
    }

    private async Task<ResetToken> SemearTokenAsync(Usuario usuario, string finalidade, string canal)
    {
        var token = ResetToken.Criar(
            usuario.Id, $"hash-{Guid.NewGuid():N}", DateTime.UtcNow.AddMinutes(30), null, null, finalidade, canal);
        await using var db = fixture.CreateDbContext();
        db.ResetTokens.Add(token);
        await db.SaveChangesAsync();
        return token;
    }

    private async Task<Usuario> LerUsuarioAsync(Guid id)
    {
        await using var db = fixture.CreateDbContext();
        return await db.Usuarios.AsNoTracking().SingleAsync(u => u.Id == id);
    }
}
