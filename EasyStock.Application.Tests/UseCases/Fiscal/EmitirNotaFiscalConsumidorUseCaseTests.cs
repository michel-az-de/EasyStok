using EasyStock.Application.Ports.Output.Fiscal;
using EasyStock.Application.Ports.Output.Integration;
using EasyStock.Application.Ports.Output.Persistence;
using EasyStock.Application.Services.Fiscal;
using EasyStock.Application.UseCases.Fiscal.EmitirNotaFiscalConsumidor;
using EasyStock.Domain.Entities.Fiscal;
using EasyStock.Domain.Enums.Fiscal;
using EasyStock.Domain.ValueObjects;
using Microsoft.Extensions.Logging.Abstractions;

namespace EasyStock.Application.Tests.UseCases.Fiscal;

public class EmitirNotaFiscalConsumidorUseCaseTests
{
    private static readonly Guid EmpresaId = Guid.Parse("11111111-1111-1111-1111-111111111111");
    private static readonly Guid LojaId = Guid.Parse("22222222-2222-2222-2222-222222222222");
    private static readonly Guid PedidoId = Guid.Parse("33333333-3333-3333-3333-333333333333");

    private static ConfigFiscalDto NovaConfig() => new(
        EmpresaId: EmpresaId,
        LojaId: LojaId,
        Ambiente: AmbienteSefaz.Homologacao,
        Serie: 1,
        CnpjEmitente: "12345678000190",
        InscricaoEstadualEmitente: "ISENTO",
        NomeEmitente: "Empresa Teste Ltda",
        UfEmitente: "SP",
        UfCodigoIbge: "35",
        CepEmitente: "01000000",
        LogradouroEmitente: "Rua X",
        NumeroEnderecoEmitente: "100",
        ComplementoEnderecoEmitente: null,
        BairroEmitente: "Centro",
        MunicipioEmitente: "São Paulo",
        MunicipioCodigoIbge: "3550308",
        RegimeTributario: RegimeTributario.SimplesNacional,
        TokenFocus: "tok-test",
        CscId: "001",
        Csc: "ABC",
        WebhookSecret: "secret");

    private static Pedido NovoPedido() => new()
    {
        Id = PedidoId,
        EmpresaId = EmpresaId,
        LojaId = LojaId,
        Status = "aguardando",
        Total = Dinheiro.FromDecimal(49.90m),
        Itens =
        [
            new PedidoItem
            {
                Id = Guid.NewGuid(),
                PedidoId = PedidoId,
                Nome = "Produto teste",
                Unidade = "UN",
                Quantidade = 1m,
                PrecoUnitario = 49.90m,
                Subtotal = 49.90m,
            },
        ],
    };

    private static EmitirNotaFiscalConsumidorCommand NovoCommand() => new(
        EmpresaId: EmpresaId,
        PedidoId: PedidoId,
        LojaId: LojaId,
        ClienteCpfCnpj: null,
        Pagamentos: [new EmitirNotaFiscalPagamentoInput(FormaPagamentoFiscal.Pix, 49.90m)],
        Origem: "test",
        UsuarioId: null);

    [Fact]
    public async Task Idempotente_pedido_ja_emitido_retorna_nota_existente_sem_chamar_gateway()
    {
        var notaRepo = Substitute.For<INotaFiscalRepository>();
        var pedidoRepo = Substitute.For<IPedidoRepository>();
        var gateway = Substitute.For<IGatewayFiscal>();
        var numeracao = Substitute.For<INumeracaoNotaFiscalService>();
        var gerador = Substitute.For<IGeradorChaveAcesso>();
        var configResolver = Substitute.For<IConfigFiscalResolver>();
        var eventos = Substitute.For<IPublicadorEventoIntegracao>();
        var uow = Substitute.For<IUnitOfWork>();

        var notaExistente = NotaFiscal.CriarParaEmissao(
            EmpresaId, LojaId, PedidoId, ModeloDocumentoFiscal.NFCe, 1, 1,
            EasyStock.Domain.ValueObjects.Fiscal.ChaveAcessoNFe.Construir(
                "35", DateTime.UtcNow, "12345678000190", ModeloDocumentoFiscal.NFCe,
                1, 1, TipoEmissao.Normal, "00000001"),
            TipoEmissao.Normal, AmbienteSefaz.Homologacao, DateTime.UtcNow,
            Dinheiro.FromDecimal(49.90m), null,
            $"{EmpresaId:N}:{LojaId:N}:{PedidoId:N}", "test", null);
        notaRepo.ObterPorIdempotencyKeyAsync(Arg.Any<Guid>(), Arg.Any<string>(), Arg.Any<CancellationToken>())
            .Returns(notaExistente);

        var sut = new EmitirNotaFiscalConsumidorUseCase(
            notaRepo, pedidoRepo, gateway, numeracao, gerador,
            configResolver, eventos, uow, NullLogger<EmitirNotaFiscalConsumidorUseCase>.Instance);

        var result = await sut.ExecuteAsync(NovoCommand());

        result.NotaFiscalId.Should().Be(notaExistente.Id);
        await gateway.DidNotReceive().EmitirNFCeAsync(default!, default!, default);
        await numeracao.DidNotReceive().ReservarProximoNumeroAsync(
            default, default, default, default, default);
    }

    [Fact]
    public async Task Pagamentos_vazios_lanca_validation()
    {
        var notaRepo = Substitute.For<INotaFiscalRepository>();
        var pedidoRepo = Substitute.For<IPedidoRepository>();
        var gateway = Substitute.For<IGatewayFiscal>();
        var numeracao = Substitute.For<INumeracaoNotaFiscalService>();
        var gerador = Substitute.For<IGeradorChaveAcesso>();
        var configResolver = Substitute.For<IConfigFiscalResolver>();
        var eventos = Substitute.For<IPublicadorEventoIntegracao>();
        var uow = Substitute.For<IUnitOfWork>();

        var sut = new EmitirNotaFiscalConsumidorUseCase(
            notaRepo, pedidoRepo, gateway, numeracao, gerador,
            configResolver, eventos, uow, NullLogger<EmitirNotaFiscalConsumidorUseCase>.Instance);

        var cmd = NovoCommand() with { Pagamentos = [] };

        var act = () => sut.ExecuteAsync(cmd);
        await act.Should().ThrowAsync<UseCaseValidationException>().WithMessage("*pagamento*");
    }

    [Fact]
    public async Task Pedido_inexistente_lanca_validation()
    {
        var notaRepo = Substitute.For<INotaFiscalRepository>();
        var pedidoRepo = Substitute.For<IPedidoRepository>();
        var gateway = Substitute.For<IGatewayFiscal>();
        var numeracao = Substitute.For<INumeracaoNotaFiscalService>();
        var gerador = Substitute.For<IGeradorChaveAcesso>();
        var configResolver = Substitute.For<IConfigFiscalResolver>();
        var eventos = Substitute.For<IPublicadorEventoIntegracao>();
        var uow = Substitute.For<IUnitOfWork>();

        notaRepo.ObterPorIdempotencyKeyAsync(Arg.Any<Guid>(), Arg.Any<string>(), Arg.Any<CancellationToken>())
            .Returns((NotaFiscal?)null);
        pedidoRepo.GetByIdWithDetailsAsync(EmpresaId, PedidoId).Returns((Pedido?)null);

        var sut = new EmitirNotaFiscalConsumidorUseCase(
            notaRepo, pedidoRepo, gateway, numeracao, gerador,
            configResolver, eventos, uow, NullLogger<EmitirNotaFiscalConsumidorUseCase>.Instance);

        var act = () => sut.ExecuteAsync(NovoCommand());
        await act.Should().ThrowAsync<UseCaseValidationException>().WithMessage("*nao encontrado*");
    }

    [Fact]
    public async Task Pedido_de_outra_empresa_lanca_forbidden()
    {
        var notaRepo = Substitute.For<INotaFiscalRepository>();
        var pedidoRepo = Substitute.For<IPedidoRepository>();
        var gateway = Substitute.For<IGatewayFiscal>();
        var numeracao = Substitute.For<INumeracaoNotaFiscalService>();
        var gerador = Substitute.For<IGeradorChaveAcesso>();
        var configResolver = Substitute.For<IConfigFiscalResolver>();
        var eventos = Substitute.For<IPublicadorEventoIntegracao>();
        var uow = Substitute.For<IUnitOfWork>();

        var pedidoAlheio = NovoPedido();
        pedidoAlheio.EmpresaId = Guid.NewGuid();

        notaRepo.ObterPorIdempotencyKeyAsync(Arg.Any<Guid>(), Arg.Any<string>(), Arg.Any<CancellationToken>())
            .Returns((NotaFiscal?)null);
        pedidoRepo.GetByIdWithDetailsAsync(EmpresaId, PedidoId).Returns(pedidoAlheio);

        var sut = new EmitirNotaFiscalConsumidorUseCase(
            notaRepo, pedidoRepo, gateway, numeracao, gerador,
            configResolver, eventos, uow, NullLogger<EmitirNotaFiscalConsumidorUseCase>.Instance);

        var act = () => sut.ExecuteAsync(NovoCommand());
        await act.Should().ThrowAsync<UseCaseValidationException>();
    }
}
