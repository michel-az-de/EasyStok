using EasyStock.Application.Ports.Output.Persistence;
using EasyStock.Application.UseCases.Atendimento;
using EasyStock.Application.UseCases.FeatureFlags;
using EasyStock.Domain.Entities.Atendimento;

namespace EasyStock.Application.Tests.UseCases.Atendimento;

/// <summary>#1474 B5: o status da integração mostra o que o webhook gravou em <see cref="ConfiguracaoAtendimento"/>.</summary>
public class ObterStatusIntegracaoWhatsAppUseCaseTests
{
    private readonly Guid _empresaId = Guid.NewGuid();
    private readonly ITenantFeatureFlagRepository _flags = Substitute.For<ITenantFeatureFlagRepository>();
    private readonly IEmpresaRepository _empresas = Substitute.For<IEmpresaRepository>();
    private readonly IConfiguracaoAtendimentoRepository _configuracoes = Substitute.For<IConfiguracaoAtendimentoRepository>();
    private readonly ObterStatusIntegracaoWhatsAppUseCase _useCase;

    public ObterStatusIntegracaoWhatsAppUseCaseTests()
    {
        _flags.ListarAtivasAsync(_empresaId, Arg.Any<CancellationToken>()).Returns(new[] { FeatureCatalogo.ModuloAtendimento });
        _useCase = new ObterStatusIntegracaoWhatsAppUseCase(_flags, _empresas, _configuracoes);
    }

    [Fact]
    public async Task DevolveAUltimaMensagemRecebidaEAVerificacaoDoWebhook()
    {
        var configuracao = ConfiguracaoAtendimento.CriarPadrao(_empresaId);
        var verificado = new DateTime(2026, 10, 1, 12, 0, 0, DateTimeKind.Utc);
        var recebida = new DateTime(2026, 10, 8, 14, 30, 0, DateTimeKind.Utc);
        configuracao.RegistrarWebhookVerificado(verificado);
        configuracao.RegistrarMensagemRecebida(recebida);
        _configuracoes.GetByEmpresaIdAsync(_empresaId).Returns(configuracao);

        var status = await _useCase.ExecuteAsync(new ObterStatusIntegracaoWhatsAppQuery(_empresaId));

        status!.UltimaMensagemRecebidaEm.Should().Be(recebida);
        status.WebhookVerificadoEm.Should().Be(verificado);
    }

    [Fact]
    public async Task SemConfiguracaoDevolveNulos()
    {
        var status = await _useCase.ExecuteAsync(new ObterStatusIntegracaoWhatsAppQuery(_empresaId));

        status!.UltimaMensagemRecebidaEm.Should().BeNull();
        status.WebhookVerificadoEm.Should().BeNull();
    }
}
