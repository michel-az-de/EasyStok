using EasyStock.Application.Services.Notifications.Orchestrators;
using EasyStock.Application.Ports.Output.Notifications;
using EasyStock.Domain.Entities.Notifications;
using EasyStock.Domain.Enums.Notifications;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
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

    // ----- avaliador: veneno e idempotência -----

    [SkippableFact]
    public async Task Avaliador_evento_cujo_commit_falha_nao_impede_o_proximo()
    {
        Skip.If(!fixture.IsAvailable, fixture.UnavailableReason ?? "Docker/PostgreSQL indisponivel");
        await _s.SemearCatalogoGlobalAsync(TipoEventoNotificacao.AlertaEstoqueCritico, CanalNotificacao.Email, CategoriaConteudoNotificacao.Transacional);
        // O assunto sai da variável do payload: com 600 caracteres estoura o varchar(500) do outbox no INSERT, no commit.
        await _s.SemearCatalogoGlobalAsync(TipoEventoNotificacao.TicketCriado, CanalNotificacao.Email, CategoriaConteudoNotificacao.Transacional,
            assuntoTemplate: "{{ token }}");
        var empresa = await _s.SemearEmpresaAsync();
        var agora = DateTime.UtcNow.AddMinutes(-10);
        var primeiro = await _s.SemearEventoPendenteAsync(empresa, TipoEventoNotificacao.AlertaEstoqueCritico, ocorridoEm: agora);
        var venenoso = await _s.SemearEventoPendenteAsync(empresa, TipoEventoNotificacao.TicketCriado,
            $$"""{"email":"maria@example.com","token":"{{new string('a', 600)}}"}""", ocorridoEm: agora.AddSeconds(1));
        var terceiro = await _s.SemearEventoPendenteAsync(empresa, TipoEventoNotificacao.AlertaEstoqueCritico, ocorridoEm: agora.AddSeconds(2));
        // 23505: o evento já tem a mensagem do canal no outbox (mesma IdempotencyKey = evento + usuário + canal).
        var jaEnfileirado = await _s.SemearEventoPendenteAsync(empresa, TipoEventoNotificacao.AlertaEstoqueCritico, ocorridoEm: agora.AddSeconds(3));
        var modelo = await _s.SemearAsync(CanalNotificacao.Email, empresaId: empresa);
        await using (var db = fixture.CreateDbContext())
        {
            db.NotifOutboxMensagens.Add(OutboxMensagemNotificacao.Criar(jaEnfileirado.Id, modelo.TemplateId, empresa,
                CanalNotificacao.Email, "maria@example.com", "Assunto", "corpo", CategoriaConteudoNotificacao.Transacional));
            await db.SaveChangesAsync();
        }
        var quinto = await _s.SemearEventoPendenteAsync(empresa, TipoEventoNotificacao.AlertaEstoqueCritico, ocorridoEm: agora.AddSeconds(4));
        await using var provider = _s.ConstruirProviderDoWorker(papelRls: false);

        await using (var scope = provider.CreateAsyncScope())
            await scope.ServiceProvider.GetRequiredService<INotificacoesAvaliadorOrchestrator>().ExecutarRodadaAsync(TimeSpan.FromMinutes(2));

        (await _s.LerEventoAsync(primeiro.Id)).Status.Should().Be(StatusEventoNotificacao.Processado);
        var gravadoVenenoso = await _s.LerEventoAsync(venenoso.Id);
        gravadoVenenoso.Status.Should().Be(StatusEventoNotificacao.Falhado, "o veneno termina Falhado, com o motivo");
        gravadoVenenoso.ErroProcessamento.Should().NotBeNullOrWhiteSpace();
        (await _s.LerMensagensDoEventoAsync(venenoso.Id)).Should().BeEmpty();
        (await _s.LerEventoAsync(terceiro.Id)).Status.Should().Be(StatusEventoNotificacao.Processado, "o DbContext sujo do veneno não derruba os commits seguintes");
        (await _s.LerMensagensDoEventoAsync(terceiro.Id)).Should().ContainSingle();
        (await _s.LerEventoAsync(jaEnfileirado.Id)).Status.Should().Be(StatusEventoNotificacao.Processado, "23505 na IdempotencyKey = já enfileirado");
        (await _s.LerMensagensDoEventoAsync(jaEnfileirado.Id)).Should().ContainSingle();
        (await _s.LerEventoAsync(quinto.Id)).Status.Should().Be(StatusEventoNotificacao.Processado);
    }

    // ----- entrega por canal: EmEnvio, id do provider, Indeterminado e lease -----

    private StatusOutbox StatusNoBanco(Guid outboxId)
    {
        using var db = fixture.CreateDbContext();
        return db.NotifOutboxMensagens.AsNoTracking().IgnoreQueryFilters().Single(m => m.Id == outboxId).Status;
    }

    [SkippableFact]
    public async Task Dispatcher_whatsapp_grava_EmEnvio_e_o_id_do_provider()
    {
        Skip.If(!fixture.IsAvailable, fixture.UnavailableReason ?? "Docker/PostgreSQL indisponivel");
        var s = await _s.SemearAsync(CanalNotificacao.WhatsApp);
        var mensagem = await _s.SemearMensagemAsync(s, CanalNotificacao.WhatsApp);
        StatusOutbox? statusDuranteOEnvio = null;
        var whatsapp = new CanalFalso(CanalNotificacao.WhatsApp, m =>
        {
            statusDuranteOEnvio = StatusNoBanco(m.OutboxId);
            return new ResultadoEnvio(true, "meta", DuracaoMs: 5) { IdExterno = "wamid.HBgM-N1" };
        });
        await using var provider = _s.ConstruirProviderDoWorker(papelRls: false, canais: whatsapp);

        await RodarDispatcherAsync(provider);

        statusDuranteOEnvio.Should().Be(StatusOutbox.EmEnvio, "no máximo uma vez: o EmEnvio é commitado antes de o provider ser chamado");
        var gravada = await _s.LerMensagemAsync(mensagem.Id);
        gravada.Status.Should().Be(StatusOutbox.Enviado);
        gravada.ProviderMensagemId.Should().Be("wamid.HBgM-N1", "o id do provider vai no mesmo commit do resultado, para a N6 casar o webhook");
    }

    [SkippableFact]
    public async Task Dispatcher_whatsapp_com_timeout_vira_Indeterminado_e_nao_reenvia()
    {
        Skip.If(!fixture.IsAvailable, fixture.UnavailableReason ?? "Docker/PostgreSQL indisponivel");
        var s = await _s.SemearAsync(CanalNotificacao.WhatsApp);
        var mensagem = await _s.SemearMensagemAsync(s, CanalNotificacao.WhatsApp);
        var whatsapp = new CanalFalso(CanalNotificacao.WhatsApp, _ => ResultadoEnvio.Indeterminado("meta", "timeout na chamada"));
        await using var provider = _s.ConstruirProviderDoWorker(papelRls: false, canais: whatsapp);

        await RodarDispatcherAsync(provider);
        await RodarDispatcherAsync(provider);

        var gravada = await _s.LerMensagemAsync(mensagem.Id);
        gravada.Status.Should().Be(StatusOutbox.Indeterminado);
        gravada.Tentativas.Should().Be(1);
        whatsapp.ChamadasDe(mensagem.Id).Should().Be(1, "Indeterminado é terminal: nunca volta a Pendente nem reenvia");
    }

    [SkippableFact]
    public async Task Dispatcher_lease_vencido_volta_a_Pendente_no_email_e_vira_Indeterminado_no_whatsapp()
    {
        Skip.If(!fixture.IsAvailable, fixture.UnavailableReason ?? "Docker/PostgreSQL indisponivel");
        var s = await _s.SemearAsync(CanalNotificacao.Email);
        Action<OutboxMensagemNotificacao> leaseVencido = m =>
        {
            m.Status = StatusOutbox.EmEnvio;
            m.ProximaTentativaEm = DateTime.UtcNow.AddMinutes(-1);
        };
        var email = await _s.SemearMensagemAsync(s, CanalNotificacao.Email, ajustar: leaseVencido);
        var whatsapp = await _s.SemearMensagemAsync(s, CanalNotificacao.WhatsApp, ajustar: leaseVencido);
        var whatsappDentroDoLease = await _s.SemearMensagemAsync(s, CanalNotificacao.WhatsApp, ajustar: m =>
        {
            m.Status = StatusOutbox.EmEnvio;
            m.ProximaTentativaEm = DateTime.UtcNow.AddMinutes(4);
        });
        var canalEmail = new CanalFalso(CanalNotificacao.Email, _ => new ResultadoEnvio(true, "smtp"));
        var canalWhatsApp = new CanalFalso(CanalNotificacao.WhatsApp, _ => new ResultadoEnvio(true, "meta"));
        await using var provider = _s.ConstruirProviderDoWorker(papelRls: false, canais: [canalEmail, canalWhatsApp]);

        await RodarDispatcherAsync(provider);

        var gravadaEmail = await _s.LerMensagemAsync(email.Id);
        gravadaEmail.Tentativas.Should().Be(1, "o lease vencido voltou a mensagem a Pendente contando a tentativa");
        gravadaEmail.Status.Should().Be(StatusOutbox.Enviado, "e, elegível na hora, ela saiu na mesma rodada");
        canalEmail.ChamadasDe(email.Id).Should().Be(1);

        var gravadaWhatsApp = await _s.LerMensagemAsync(whatsapp.Id);
        gravadaWhatsApp.Status.Should().Be(StatusOutbox.Indeterminado, "pode ter saído antes da queda: nunca volta a Pendente");
        gravadaWhatsApp.Tentativas.Should().Be(1);
        canalWhatsApp.ChamadasDe(whatsapp.Id).Should().Be(0);

        (await _s.LerMensagemAsync(whatsappDentroDoLease.Id)).Status.Should().Be(StatusOutbox.EmEnvio, "o lease ainda vale");
    }

    // ----- quarentena e kill switch -----

    [SkippableFact]
    public async Task Quarentena_expira_evento_e_outbox_alem_do_prazo_do_tipo_antes_da_reserva()
    {
        Skip.If(!fixture.IsAvailable, fixture.UnavailableReason ?? "Docker/PostgreSQL indisponivel");
        var agora = DateTime.UtcNow;
        const string payloadComToken = """{"email":"maria@example.com","token":"482913"}""";

        // Eventos pendentes: aviso do pedido há 3 h (prazo 2 h), demais há 2 dias (24 h), reset há 31 min (30 min,
        // com segredo no payload) e um aviso recente, que segue o fluxo normal.
        var empresa = await _s.SemearEmpresaAsync();
        var avisoVelho = await _s.SemearEventoPendenteAsync(empresa, TipoEventoNotificacao.PedidoPagoConfirmado, ocorridoEm: agora.AddHours(-3));
        var demaisVelho = await _s.SemearEventoPendenteAsync(empresa, TipoEventoNotificacao.ProdutoVencendo, ocorridoEm: agora.AddDays(-2));
        var resetVelho = await _s.SemearEventoPendenteAsync(empresa, TipoEventoNotificacao.ResetSenha, payloadComToken, agora.AddMinutes(-31));
        var avisoRecente = await _s.SemearEventoPendenteAsync(empresa, TipoEventoNotificacao.PedidoPagoConfirmado, ocorridoEm: agora.AddMinutes(-10));

        // Mensagens do outbox: o prazo vem do tipo do evento e conta de ProximaTentativaEm.
        Func<TimeSpan, Action<OutboxMensagemNotificacao>> envelhecida = idade => m => m.ProximaTentativaEm = agora - idade;
        var sAviso = await _s.SemearAsync(CanalNotificacao.WhatsApp, tipo: TipoEventoNotificacao.PedidoPagoConfirmado);
        var msgAviso = await _s.SemearMensagemAsync(sAviso, CanalNotificacao.WhatsApp, ajustar: envelhecida(TimeSpan.FromHours(3)));
        var sCampanha = await _s.SemearAsync(CanalNotificacao.WhatsApp, tipo: TipoEventoNotificacao.CampanhaMarketing);
        var msgCampanha = await _s.SemearMensagemAsync(sCampanha, CanalNotificacao.WhatsApp, CategoriaConteudoNotificacao.Marketing,
            ajustar: envelhecida(TimeSpan.FromHours(2)));
        var sDemais = await _s.SemearAsync(CanalNotificacao.WhatsApp, tipo: TipoEventoNotificacao.ProdutoVencendo);
        var msgDemais = await _s.SemearMensagemAsync(sDemais, CanalNotificacao.WhatsApp, ajustar: envelhecida(TimeSpan.FromDays(2)));
        var sReset = await _s.SemearAsync(CanalNotificacao.Email, payloadComToken, TipoEventoNotificacao.ResetSenha);
        var msgReset = await _s.SemearMensagemAsync(sReset, CanalNotificacao.Email, CategoriaConteudoNotificacao.Seguranca,
            ajustar: envelhecida(TimeSpan.FromMinutes(31)));
        var sRecente = await _s.SemearAsync(CanalNotificacao.WhatsApp, tipo: TipoEventoNotificacao.PedidoPagoConfirmado);
        var msgRecente = await _s.SemearMensagemAsync(sRecente, CanalNotificacao.WhatsApp);

        var whatsapp = new CanalFalso(CanalNotificacao.WhatsApp, _ => new ResultadoEnvio(true, "meta"));
        var emailCanal = new CanalFalso(CanalNotificacao.Email, _ => new ResultadoEnvio(true, "smtp"));
        await using var provider = _s.ConstruirProviderDoWorker(papelRls: false, canais: [whatsapp, emailCanal]);

        // Uma rodada do Worker: o avaliador expira os eventos e o dispatcher expira o outbox antes de reservar.
        await using (var scope = provider.CreateAsyncScope())
            await scope.ServiceProvider.GetRequiredService<INotificacoesAvaliadorOrchestrator>().ExecutarRodadaAsync(TimeSpan.FromMinutes(2));
        await RodarDispatcherAsync(provider);

        foreach (var expirado in new[] { avisoVelho, demaisVelho, resetVelho })
            (await _s.LerEventoAsync(expirado.Id)).Status.Should().Be(StatusEventoNotificacao.Expirado, expirado.Tipo.ToString());
        (await _s.LerEventoAsync(resetVelho.Id)).PayloadJson.Should().Be("{}", "o token do reset sai do banco ao expirar");
        (await _s.LerEventoAsync(avisoRecente.Id)).Status.Should().NotBe(StatusEventoNotificacao.Expirado);

        foreach (var expirada in new[] { msgAviso, msgCampanha, msgDemais, msgReset })
        {
            var gravada = await _s.LerMensagemAsync(expirada.Id);
            gravada.Status.Should().Be(StatusOutbox.Expirado, $"{gravada.Canal} {expirada.Id}");
            gravada.EnviadoEm.Should().BeNull();
            whatsapp.ChamadasDe(expirada.Id).Should().Be(0, "o canal não é chamado para o que expirou");
            emailCanal.ChamadasDe(expirada.Id).Should().Be(0);
        }
        (await _s.LerMensagemAsync(msgReset.Id)).CorpoRenderizado.Should()
            .Be(OutboxMensagemNotificacao.CorpoApagado, "segurança apaga o corpo em todo desfecho terminal");

        (await _s.LerMensagemAsync(msgRecente.Id)).Status.Should().Be(StatusOutbox.Enviado, "dentro do prazo, segue o fluxo normal");
        whatsapp.ChamadasDe(msgRecente.Id).Should().Be(1);
    }

    [SkippableFact]
    public async Task Dispatcher_respeita_kill_switch_global_e_da_empresa_no_envio()
    {
        Skip.If(!fixture.IsAvailable, fixture.UnavailableReason ?? "Docker/PostgreSQL indisponivel");
        var global = await _s.SemearAsync(CanalNotificacao.Sms);
        var daEmpresa = await _s.SemearAsync(CanalNotificacao.Email);
        var livre = await _s.SemearAsync(CanalNotificacao.Email);
        var msgGlobal = await _s.SemearMensagemAsync(global, CanalNotificacao.Sms);
        var msgEmpresa = await _s.SemearMensagemAsync(daEmpresa, CanalNotificacao.Email);
        var msgLivre = await _s.SemearMensagemAsync(livre, CanalNotificacao.Email);
        var motivoGlobal = $"manutenção do provedor {Guid.NewGuid():N}";
        await using (var db = fixture.CreateDbContext())
        {
            // Global só para o SMS e da empresa para todos os canais dela: o kill switch vale também para o que já está no outbox.
            db.NotifBloqueios.Add(BloqueioNotificacao.Criar(motivoGlobal, "teste", canal: CanalNotificacao.Sms));
            db.NotifBloqueios.Add(BloqueioNotificacao.Criar("empresa em pausa", "teste", empresaId: daEmpresa.EmpresaId));
            await db.SaveChangesAsync();
        }
        var sms = new CanalFalso(CanalNotificacao.Sms, _ => new ResultadoEnvio(true, "twilio"));
        var email = new CanalFalso(CanalNotificacao.Email, _ => new ResultadoEnvio(true, "smtp"));
        await using var provider = _s.ConstruirProviderDoWorker(papelRls: false, canais: [sms, email]);

        try
        {
            await RodarDispatcherAsync(provider);
        }
        finally
        {
            await using var limpeza = fixture.CreateDbContext();
            await limpeza.NotifBloqueios.IgnoreQueryFilters().Where(b => b.Motivo == motivoGlobal).ExecuteDeleteAsync();
        }

        var gravadaGlobal = await _s.LerMensagemAsync(msgGlobal.Id);
        gravadaGlobal.Status.Should().Be(StatusOutbox.Suprimido);
        gravadaGlobal.ErroUltimaTentativa.Should().Contain(motivoGlobal);
        sms.ChamadasDe(msgGlobal.Id).Should().Be(0);

        var gravadaEmpresa = await _s.LerMensagemAsync(msgEmpresa.Id);
        gravadaEmpresa.Status.Should().Be(StatusOutbox.Suprimido);
        gravadaEmpresa.ErroUltimaTentativa.Should().Contain("empresa em pausa");
        email.ChamadasDe(msgEmpresa.Id).Should().Be(0);

        (await _s.LerMensagemAsync(msgLivre.Id)).Status.Should().Be(StatusOutbox.Enviado, "o bloqueio de outra empresa não atinge esta");
    }
}
