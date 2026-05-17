using EasyStock.Application.Ports.Output.Persistence;
using EasyStock.Application.Ports.Output.Security;
using EasyStock.Application.UseCases.Fiscal.ProcessarWebhookFocusNFe;
using EasyStock.Domain.Fiscal;
using EasyStock.Domain.ValueObjects;
using FluentAssertions;
using Microsoft.Extensions.Logging;
using NSubstitute;
using Xunit;

namespace EasyStock.Application.Tests.UseCases.Fiscal;

public class ProcessarWebhookFocusNFeUseCaseTests
{
    private readonly INfeRepository _nfeRepo = Substitute.For<INfeRepository>();
    private readonly IRowLevelSecurityBypass _rlsBypass = Substitute.For<IRowLevelSecurityBypass>();
    private readonly IDisposable _bypassScope = Substitute.For<IDisposable>();
    private readonly IUnitOfWork _uow = Substitute.For<IUnitOfWork>();
    private readonly ILogger<ProcessarWebhookFocusNFeUseCase> _logger = Substitute.For<ILogger<ProcessarWebhookFocusNFeUseCase>>();

    public ProcessarWebhookFocusNFeUseCaseTests()
    {
        _rlsBypass.Begin().Returns(_bypassScope);
    }

    private ProcessarWebhookFocusNFeUseCase NewUseCase() =>
        new(_nfeRepo, _rlsBypass, _uow, _logger);

    /// <summary>
    /// Regressao B-054: webhook DEVE chamar rlsBypass.Begin() porque chega sem JWT.
    /// Sem isso, Global Query Filter nao acha a nota e ela fica presa em EnviadaAguardandoRetorno.
    /// </summary>
    [Fact]
    public async Task ExecuteAsync_SempreChamaBypassRLS()
    {
        var chave = new string('1', 44);
        _nfeRepo.FindByChaveAcessoAsync(chave, Arg.Any<CancellationToken>())
            .Returns((NfeDocumento?)null);

        var cmd = new ProcessarWebhookFocusNFeCommand(
            ChaveAcesso: chave,
            StatusGateway: "autorizado",
            ProtocoloAutorizacao: "PROTO",
            MotivoRejeicao: null,
            XmlAssinadoUrl: null,
            DanfeUrl: null,
            DataEvento: DateTime.UtcNow);

        await NewUseCase().ExecuteAsync(cmd);

        _rlsBypass.Received(1).Begin();
    }

    [Fact]
    public async Task ExecuteAsync_ChaveNaoEncontrada_RetornaNaoAplicado()
    {
        var chave = new string('1', 44);
        _nfeRepo.FindByChaveAcessoAsync(chave, Arg.Any<CancellationToken>())
            .Returns((NfeDocumento?)null);

        var cmd = new ProcessarWebhookFocusNFeCommand(
            ChaveAcesso: chave,
            StatusGateway: "autorizado",
            ProtocoloAutorizacao: "PROTO",
            MotivoRejeicao: null,
            XmlAssinadoUrl: null,
            DanfeUrl: null,
            DataEvento: DateTime.UtcNow);

        var resultado = await NewUseCase().ExecuteAsync(cmd);

        resultado.Aplicado.Should().BeFalse();
        resultado.NfeId.Should().BeNull();
        await _uow.DidNotReceive().ExecuteInTransactionAsync(Arg.Any<Func<CancellationToken, Task>>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task ExecuteAsync_ChaveCom43Digitos_LancaValidacao()
    {
        var chave43 = new string('1', 43);
        var cmd = new ProcessarWebhookFocusNFeCommand(
            ChaveAcesso: chave43,
            StatusGateway: "autorizado",
            ProtocoloAutorizacao: "PROTO",
            MotivoRejeicao: null,
            XmlAssinadoUrl: null,
            DanfeUrl: null,
            DataEvento: DateTime.UtcNow);

        var act = async () => await NewUseCase().ExecuteAsync(cmd);

        await act.Should().ThrowAsync<EasyStock.Application.UseCases.Common.UseCaseValidationException>()
            .WithMessage("*44 digitos*");
    }
}
