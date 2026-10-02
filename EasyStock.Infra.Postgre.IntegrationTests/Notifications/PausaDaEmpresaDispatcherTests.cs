using EasyStock.Application.Ports.Output.Notifications;
using EasyStock.Application.Services.Notifications.Orchestrators;
using EasyStock.Domain.Entities.Notifications;
using EasyStock.Domain.Enums.Notifications;
using FluentAssertions;
using Microsoft.Extensions.DependencyInjection;

namespace EasyStock.Infra.Postgre.IntegrationTests.Notifications;

/// <summary>
/// N5: o kill switch que o dispatcher consulta antes de enviar vale também para a pausa da empresa, por canal ou geral,
/// e a pausa da empresa nunca segura <c>Seguranca</c>. A pausa de outra empresa não interfere.
/// </summary>
public class PausaDaEmpresaDispatcherTests(PostgreSqlDatabaseFixture fixture) : IClassFixture<PostgreSqlDatabaseFixture>
{
    private readonly MotorNotificacoesSuporte _s = new(fixture);

    private async Task PausarAsync(Guid empresaId, CanalNotificacao? canal = null)
    {
        await using var db = fixture.CreateDbContext();
        using var _ = db.UseRowLevelSecurityBypass();
        db.NotifBloqueios.Add(BloqueioNotificacao.Criar("pausa de teste N5", "teste", empresaId, canal));
        await db.SaveChangesAsync();
    }

    [SkippableFact]
    public async Task PausaDaEmpresaSuprimeOperacionalENaoSeguranca()
    {
        Skip.If(!fixture.IsAvailable, fixture.UnavailableReason ?? "Docker/PostgreSQL indisponivel");
        var pausada = await _s.SemearAsync(CanalNotificacao.Email);
        var outra = await _s.SemearAsync(CanalNotificacao.Email);
        var operacional = await _s.SemearMensagemAsync(pausada, CanalNotificacao.Email, CategoriaConteudoNotificacao.Operacional);
        var seguranca = await _s.SemearMensagemAsync(pausada, CanalNotificacao.Email, CategoriaConteudoNotificacao.Seguranca);
        var daOutra = await _s.SemearMensagemAsync(outra, CanalNotificacao.Email, CategoriaConteudoNotificacao.Operacional);
        await PausarAsync(pausada.EmpresaId);
        var email = new CanalFalso(CanalNotificacao.Email, _ => new ResultadoEnvio(true, "smtp"));
        await using var provider = _s.ConstruirProviderDoWorker(papelRls: true, canais: email);

        await provider.GetRequiredService<INotificacoesDispatcherOrchestrator>().ExecutarRodadaAsync(shardCount: 4, batchSize: 50);

        (await _s.LerMensagemAsync(operacional.Id)).Status.Should().Be(StatusOutbox.Suprimido);
        email.ChamadasDe(operacional.Id).Should().Be(0);
        (await _s.LerMensagemAsync(seguranca.Id)).Status.Should().Be(StatusOutbox.Enviado, "a pausa da empresa nunca segura Seguranca");
        (await _s.LerMensagemAsync(daOutra.Id)).Status.Should().Be(StatusOutbox.Enviado, "a pausa vale só para a empresa que a criou");
    }

    [SkippableFact]
    public async Task PausaPorCanalDaEmpresaSuprimeSoOCanal()
    {
        Skip.If(!fixture.IsAvailable, fixture.UnavailableReason ?? "Docker/PostgreSQL indisponivel");
        var s = await _s.SemearAsync(CanalNotificacao.Email);
        var email = await _s.SemearMensagemAsync(s, CanalNotificacao.Email, CategoriaConteudoNotificacao.Operacional);
        var whats = await _s.SemearMensagemAsync(s, CanalNotificacao.WhatsApp, CategoriaConteudoNotificacao.Operacional);
        await PausarAsync(s.EmpresaId, CanalNotificacao.Email);
        var canalEmail = new CanalFalso(CanalNotificacao.Email, _ => new ResultadoEnvio(true, "smtp"));
        var canalWhats = new CanalFalso(CanalNotificacao.WhatsApp, _ => new ResultadoEnvio(true, "meta"));
        await using var provider = _s.ConstruirProviderDoWorker(papelRls: true, canais: [canalEmail, canalWhats]);

        await provider.GetRequiredService<INotificacoesDispatcherOrchestrator>().ExecutarRodadaAsync(shardCount: 4, batchSize: 50);

        (await _s.LerMensagemAsync(email.Id)).Status.Should().Be(StatusOutbox.Suprimido);
        (await _s.LerMensagemAsync(whats.Id)).Status.Should().Be(StatusOutbox.Enviado);
    }
}
