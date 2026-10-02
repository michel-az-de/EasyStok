using EasyStock.Application.Services.Notifications.Orchestrators;
using EasyStock.Application.Ports.Output.Notifications;
using EasyStock.Domain.Entities.Notifications;
using EasyStock.Domain.Enums.Notifications;
using FluentAssertions;
using Microsoft.Extensions.DependencyInjection;

namespace EasyStock.Infra.Postgre.IntegrationTests.Notifications;

/// <summary>
/// N1 em Postgres real, o comportamento do motor: veneno, shards, concorrência, quarentena, kill switch, lease e
/// entrega por canal. Roda com o superusuário e o usuário de sistema do Worker, para a falha vir do comportamento e não
/// da RLS (que é assunto de <see cref="MotorNotificacoesRlsTests"/>). Os canais são falsos e contam as chamadas por
/// mensagem.
/// </summary>
public class MotorNotificacoesComportamentoTests(PostgreSqlDatabaseFixture fixture) : IClassFixture<PostgreSqlDatabaseFixture>
{
    private readonly MotorNotificacoesSuporte _s = new(fixture);

    private static Task<int> RodarDispatcherAsync(ServiceProvider provider, int shardCount = 4) =>
        provider.GetRequiredService<INotificacoesDispatcherOrchestrator>().ExecutarRodadaAsync(shardCount, batchSize: 50);

    // ----- dispatcher: veneno, shards e concorrência -----

    [SkippableFact]
    public async Task Dispatcher_mensagem_venenosa_nao_trava_as_demais_nem_os_outros_shards()
    {
        Skip.If(!fixture.IsAvailable, fixture.UnavailableReason ?? "Docker/PostgreSQL indisponivel");
        var s = await _s.SemearAsync(CanalNotificacao.Email);
        var venenosa = await _s.SemearMensagemAsync(s, CanalNotificacao.Email, ajustar: m => m.ShardKey = 0);
        var demais = new List<OutboxMensagemNotificacao>();
        for (var shard = 0; shard < 4; shard++)
            demais.Add(await _s.SemearMensagemAsync(s, CanalNotificacao.Email, ajustar: m => m.ShardKey = shard));
        // O provider com 60 caracteres estoura o varchar(40) no commit do resultado: a exceção sai do SaveChanges.
        var email = new CanalFalso(CanalNotificacao.Email,
            m => new ResultadoEnvio(true, m.OutboxId == venenosa.Id ? new string('p', 60) : "smtp"));
        await using var provider = _s.ConstruirProviderDoWorker(papelRls: false, canais: email);

        await RodarDispatcherAsync(provider);

        var gravadaVenenosa = await _s.LerMensagemAsync(venenosa.Id);
        gravadaVenenosa.Status.Should().Be(StatusOutbox.Falhado, "a venenosa termina Falhado, com o motivo, e não volta à fila");
        gravadaVenenosa.ErroUltimaTentativa.Should().NotBeNullOrWhiteSpace();
        foreach (var mensagem in demais)
            (await _s.LerMensagemAsync(mensagem.Id)).Status.Should().Be(StatusOutbox.Enviado, $"shard {mensagem.ShardKey}");

        // A rodada seguinte não reenvia a venenosa: ela já terminou.
        await RodarDispatcherAsync(provider);
        email.ChamadasDe(venenosa.Id).Should().Be(1);
    }

    [SkippableFact]
    public async Task Dispatcher_ignora_ShardKey_e_processa_mensagem_de_qualquer_shard()
    {
        Skip.If(!fixture.IsAvailable, fixture.UnavailableReason ?? "Docker/PostgreSQL indisponivel");
        var s = await _s.SemearAsync(CanalNotificacao.Email);
        var mensagens = new List<OutboxMensagemNotificacao>();
        for (var shard = 0; shard < 4; shard++)
            mensagens.Add(await _s.SemearMensagemAsync(s, CanalNotificacao.Email, ajustar: m => m.ShardKey = shard));
        var email = new CanalFalso(CanalNotificacao.Email, _ => new ResultadoEnvio(true, "smtp"));
        await using var provider = _s.ConstruirProviderDoWorker(papelRls: false, canais: email);

        // Com ShardCount = 2 os shards 2 e 3 nunca rodavam.
        await RodarDispatcherAsync(provider, shardCount: 2);

        foreach (var mensagem in mensagens)
            (await _s.LerMensagemAsync(mensagem.Id)).Status.Should().Be(StatusOutbox.Enviado, $"shard {mensagem.ShardKey}");
    }

    [SkippableFact]
    public async Task Dispatcher_duas_instancias_nao_enviam_a_mesma_mensagem()
    {
        Skip.If(!fixture.IsAvailable, fixture.UnavailableReason ?? "Docker/PostgreSQL indisponivel");
        var s = await _s.SemearAsync(CanalNotificacao.Email);
        var mensagens = new List<OutboxMensagemNotificacao>();
        for (var i = 0; i < 24; i++)
            mensagens.Add(await _s.SemearMensagemAsync(s, CanalNotificacao.Email));
        var email = new CanalFalso(CanalNotificacao.Email, _ => new ResultadoEnvio(true, "smtp"));
        await using var primeira = _s.ConstruirProviderDoWorker(papelRls: false, canais: email);
        await using var segunda = _s.ConstruirProviderDoWorker(papelRls: false, canais: email);

        // Réplicas concorrentes: o claim com SKIP LOCKED e o lease dão a exclusão, sem advisory lock.
        await Task.WhenAll(
            Task.Run(() => RodarDispatcherAsync(primeira)),
            Task.Run(() => RodarDispatcherAsync(segunda)),
            Task.Run(() => RodarDispatcherAsync(primeira)));

        foreach (var mensagem in mensagens)
        {
            email.ChamadasDe(mensagem.Id).Should().Be(1, "cada mensagem sai uma vez, mesmo com réplicas concorrentes");
            (await _s.LerMensagemAsync(mensagem.Id)).Status.Should().Be(StatusOutbox.Enviado);
        }
    }
}
