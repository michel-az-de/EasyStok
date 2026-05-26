using EasyStock.Application.Ports.Output.Fiscal;
using EasyStock.Application.Ports.Output.Persistence;
using EasyStock.Application.Services.Fiscal;
using EasyStock.Application.UseCases.Common;
using EasyStock.Application.UseCases.Fiscal.EmitirNfce;
using EasyStock.Domain.Fiscal;
using EasyStock.Domain.Integration;
using EasyStock.Domain.ValueObjects;
using FluentAssertions;
using Microsoft.Extensions.Logging;
using NSubstitute;
using Xunit;

namespace EasyStock.Application.Tests.UseCases.Fiscal;

public class EmitirNfceUseCaseTests
{
    private readonly INfeRepository _nfeRepo = Substitute.For<INfeRepository>();
    private readonly INumeracaoNfeService _numeracao = Substitute.For<INumeracaoNfeService>();
    private readonly IGeradorChaveAcesso _geradorChave = Substitute.For<IGeradorChaveAcesso>();
    private readonly IGatewayFiscal _gateway = Substitute.For<IGatewayFiscal>();
    private readonly IGatewayFiscalFactory _gatewayFactory = Substitute.For<IGatewayFiscalFactory>();
    private readonly IConfigFiscalResolver _configResolver = Substitute.For<IConfigFiscalResolver>();
    private readonly IUnitOfWork _uow = Substitute.For<IUnitOfWork>();
    private readonly ILogger<EmitirNfceUseCase> _logger = Substitute.For<ILogger<EmitirNfceUseCase>>();

    private EmitirNfceUseCase NewUseCase()
    {
        _gatewayFactory.ObterPara(Arg.Any<string>()).Returns(_gateway);
        return new(_nfeRepo, _numeracao, _geradorChave, _gatewayFactory, _configResolver, _uow, _logger);
    }

    [Fact]
    public async Task ExecuteAsync_ComEmpresaIdVazio_LancaUseCaseValidationException()
    {
        var cmd = ValidCommand() with { EmpresaId = Guid.Empty };

        var act = async () => await NewUseCase().ExecuteAsync(cmd);

        await act.Should().ThrowAsync<UseCaseValidationException>();
    }

    [Fact]
    public async Task ExecuteAsync_ComTotalZero_LancaUseCaseValidationException()
    {
        var cmd = ValidCommand() with { TotalNota = 0m };

        var act = async () => await NewUseCase().ExecuteAsync(cmd);

        await act.Should().ThrowAsync<UseCaseValidationException>()
            .WithMessage("*Total*maior que zero*");
    }

    [Fact]
    public async Task ExecuteAsync_SemItens_LancaUseCaseValidationException()
    {
        var cmd = ValidCommand() with { Itens = new() };

        var act = async () => await NewUseCase().ExecuteAsync(cmd);

        await act.Should().ThrowAsync<UseCaseValidationException>()
            .WithMessage("*Itens*");
    }

    [Fact]
    public async Task ExecuteAsync_ComIdempotencyKeyVazia_LancaUseCaseValidationException()
    {
        var cmd = ValidCommand() with { IdempotencyKey = "   " };

        var act = async () => await NewUseCase().ExecuteAsync(cmd);

        await act.Should().ThrowAsync<UseCaseValidationException>()
            .WithMessage("*IdempotencyKey*");
    }

    private static EmitirNfceCommand ValidCommand() => new(
        EmpresaId: Guid.NewGuid(),
        PedidoId: Guid.NewGuid(),
        IdempotencyKey: "test-idempotency-12345",
        TotalNota: 100m,
        Emitente: new DadosEmitenteInput("11444777000161", "Empresa Teste", null, null, null),
        Destinatario: null,
        Itens: new List<EmitirNfceItemInput>
        {
            new(NomeSnapshot: "Produto X", Quantidade: 1m, PrecoUnitario: 100m, Unidade: "UN",
                Ncm: "12345678", Cfop: "5102", ProdutoIdSnapshot: null, OrigemMercadoria: 0, CstOuCsosn: null)
        });
}
