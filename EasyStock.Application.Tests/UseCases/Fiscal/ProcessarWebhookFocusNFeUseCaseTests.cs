using EasyStock.Application.Ports.Output.Integration;
using EasyStock.Application.Ports.Output.Persistence;
using EasyStock.Application.UseCases.Fiscal.ProcessarWebhookFocusNFe;
using EasyStock.Domain.Entities.Fiscal;
using EasyStock.Domain.Enums.Fiscal;
using EasyStock.Domain.ValueObjects;
using EasyStock.Domain.ValueObjects.Fiscal;
using Microsoft.Extensions.Logging.Abstractions;

namespace EasyStock.Application.Tests.UseCases.Fiscal;

public class ProcessarWebhookFocusNFeUseCaseTests
{
    private static readonly Guid EmpresaId = Guid.Parse("11111111-1111-1111-1111-111111111111");

    private static NotaFiscal NotaEmEmissao() =>
        NotaFiscal.CriarParaEmissao(
            EmpresaId, Guid.NewGuid(), Guid.NewGuid(),
            ModeloDocumentoFiscal.NFCe, 1, 1,
            ChaveAcessoNFe.Construir("35", DateTime.UtcNow, "12345678000190",
                ModeloDocumentoFiscal.NFCe, 1, 1, TipoEmissao.Normal, "00000001"),
            TipoEmissao.Normal, AmbienteSefaz.Homologacao, DateTime.UtcNow,
            Dinheiro.FromDecimal(10m), null, "k1", "test", null);

    [Fact]
    public async Task Chave_inexistente_retorna_no_op()
    {
        var repo = Substitute.For<INotaFiscalRepository>();
        repo.ObterPorChaveAsync(Arg.Any<string>(), Arg.Any<CancellationToken>()).Returns((NotaFiscal?)null);
        var uow = Substitute.For<IUnitOfWork>();
        var eventos = Substitute.For<IPublicadorEventoIntegracao>();

        var sut = new ProcessarWebhookFocusNFeUseCase(
            repo, eventos, uow, NullLogger<ProcessarWebhookFocusNFeUseCase>.Instance);

        var nota = NotaEmEmissao();
        var result = await sut.ExecuteAsync(new ProcessarWebhookFocusNFeCommand(
            ChaveAcesso: nota.ChaveAcesso.Valor,
            Status: "autorizado",
            Protocolo: "P1", DhEvento: DateTime.UtcNow,
            XmlEvento: "<auth/>", Codigo: null, Motivo: null, CorrelationId: "x"));

        result.Processado.Should().BeFalse();
        await uow.DidNotReceive().ExecuteInTransactionAsync(Arg.Any<Func<CancellationToken, Task>>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Webhook_de_autorizada_quando_ja_autorizada_retorna_no_op()
    {
        var nota = NotaEmEmissao();
        nota.MarcarAutorizada("P1", "<x/>", DateTime.UtcNow);

        var repo = Substitute.For<INotaFiscalRepository>();
        repo.ObterPorChaveAsync(Arg.Any<string>(), Arg.Any<CancellationToken>()).Returns(nota);

        var uow = Substitute.For<IUnitOfWork>();
        var eventos = Substitute.For<IPublicadorEventoIntegracao>();

        var sut = new ProcessarWebhookFocusNFeUseCase(
            repo, eventos, uow, NullLogger<ProcessarWebhookFocusNFeUseCase>.Instance);

        var result = await sut.ExecuteAsync(new ProcessarWebhookFocusNFeCommand(
            nota.ChaveAcesso.Valor, "autorizado", "P1", DateTime.UtcNow,
            "<x/>", null, null, "x"));

        result.Processado.Should().BeTrue();
        await uow.DidNotReceive().ExecuteInTransactionAsync(Arg.Any<Func<CancellationToken, Task>>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Webhook_status_invalido_retorna_no_op()
    {
        var nota = NotaEmEmissao();
        var repo = Substitute.For<INotaFiscalRepository>();
        repo.ObterPorChaveAsync(Arg.Any<string>(), Arg.Any<CancellationToken>()).Returns(nota);

        var uow = Substitute.For<IUnitOfWork>();
        var eventos = Substitute.For<IPublicadorEventoIntegracao>();

        var sut = new ProcessarWebhookFocusNFeUseCase(
            repo, eventos, uow, NullLogger<ProcessarWebhookFocusNFeUseCase>.Instance);

        var result = await sut.ExecuteAsync(new ProcessarWebhookFocusNFeCommand(
            nota.ChaveAcesso.Valor, "status_xpto", null, null, null, null, null, null));

        result.Processado.Should().BeFalse();
    }

    [Fact]
    public async Task Webhook_chave_vazia_retorna_no_op()
    {
        var repo = Substitute.For<INotaFiscalRepository>();
        var uow = Substitute.For<IUnitOfWork>();
        var eventos = Substitute.For<IPublicadorEventoIntegracao>();

        var sut = new ProcessarWebhookFocusNFeUseCase(
            repo, eventos, uow, NullLogger<ProcessarWebhookFocusNFeUseCase>.Instance);

        var result = await sut.ExecuteAsync(new ProcessarWebhookFocusNFeCommand(
            "", "autorizado", null, null, null, null, null, null));

        result.Processado.Should().BeFalse();
    }
}
