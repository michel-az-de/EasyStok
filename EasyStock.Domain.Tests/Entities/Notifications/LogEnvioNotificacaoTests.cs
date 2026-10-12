using EasyStock.Domain.Entities.Notifications;
using EasyStock.Domain.Enums.Notifications;
using FluentAssertions;

namespace EasyStock.Domain.Tests.Entities.Notifications;

public class LogEnvioNotificacaoTests
{
    [Fact]
    public void RegistrarSimulado_guarda_o_provider_real_com_Sucesso_falso_e_erro_simulado()
    {
        // N2: o log não tem coluna nova para "simulado". Guarda o provider real, Sucesso falso e o texto "simulado",
        // para a auditoria não contar como entregue o que não saiu.
        var outboxId = Guid.NewGuid();

        var log = LogEnvioNotificacao.RegistrarSimulado(outboxId, tentativa: 1, CanalNotificacao.WhatsApp, "stub", duracaoMs: 3,
            bypassConsentimento: true);

        log.OutboxMensagemId.Should().Be(outboxId);
        log.Tentativa.Should().Be(1);
        log.Canal.Should().Be(CanalNotificacao.WhatsApp);
        log.Provider.Should().Be("stub");
        log.Sucesso.Should().BeFalse();
        log.ErroDetalhado.Should().Be("simulado").And.Be(LogEnvioNotificacao.ErroSimulado);
        log.DuracaoMs.Should().Be(3);
        log.BypassConsentimento.Should().BeTrue();
        log.StatusHttp.Should().BeNull();
    }

    [Fact]
    public void RegistrarSimulado_nao_ignora_o_consentimento_por_padrao()
    {
        LogEnvioNotificacao.RegistrarSimulado(Guid.NewGuid(), 1, CanalNotificacao.Email, "console", 0)
            .BypassConsentimento.Should().BeFalse();
    }
}
