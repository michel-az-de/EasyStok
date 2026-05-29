using EasyStock.Application.Ports.Output.Fiscal;
using EasyStock.Application.Ports.Output.Persistence;
using EasyStock.Application.Services.Fiscal;
using EasyStock.Application.UseCases.Fiscal.EmitirNfce;
using EasyStock.Domain.Fiscal;
using EasyStock.Domain.Integration;
using EasyStock.Domain.ValueObjects;
using Microsoft.Extensions.Logging;

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

    [Fact]
    public async Task ExecuteAsync_ComIdempotencyKeyJaUsada_RetornaNfeExistenteSemReservarNumero()
    {
        // issue #290: retry HTTP com mesma IdempotencyKey nao pode queimar
        // segundo numero fiscal. A consulta antes de reservar numero precisa
        // devolver o NfeDocumento ja emitido sem chamar numeracao nem gateway.
        var cmd = ValidCommand();
        var nfeExistente = NfeDocumento.Criar(
            empresaId: cmd.EmpresaId,
            pedidoId: cmd.PedidoId,
            serie: 1,
            numero: 42L,
            dadosEmitente: new DadosEmissor("Empresa Teste", "11444777000161"),
            dadosDestinatario: null,
            totalNota: Dinheiro.FromDecimal(100m),
            idempotencyKey: cmd.IdempotencyKey);
        nfeExistente.AdicionarItem("Produto", 1m, Dinheiro.FromDecimal(100m), "UN");
        nfeExistente.MarcarEnviada();
        nfeExistente.MarcarAutorizada(
            chaveAcesso: "12345678901234567890123456789012345678901234",
            protocoloAutorizacao: "PROTO-EXISTENTE");

        _nfeRepo.FindByIdempotencyKeyAsync(cmd.EmpresaId, cmd.IdempotencyKey)
            .Returns(nfeExistente);

        var result = await NewUseCase().ExecuteAsync(cmd);

        result.NfeId.Should().Be(nfeExistente.Id);
        result.ChaveAcesso.Should().Be("12345678901234567890123456789012345678901234");
        result.ProtocoloAutorizacao.Should().Be("PROTO-EXISTENTE");

        await _numeracao.DidNotReceive().ReservarProximoNumeroAsync(Arg.Any<Guid>());
        await _gateway.DidNotReceive().EmitirAsync(Arg.Any<NfeDocumento>(), Arg.Any<ConfigFiscalDto>());
        await _nfeRepo.DidNotReceive().AddAsync(Arg.Any<NfeDocumento>());
    }

    [Fact]
    public async Task ExecuteAsync_PrimeiraChamada_PersisteIdempotencyKeyNoDocumento()
    {
        // issue #290: primeira emissao bem-sucedida precisa armazenar IdempotencyKey
        // no agregado para que o retry consiga encontrar via FindByIdempotencyKeyAsync.
        var cmd = ValidCommand();
        var config = ValidConfig(cmd.EmpresaId);

        _nfeRepo.FindByIdempotencyKeyAsync(cmd.EmpresaId, cmd.IdempotencyKey).Returns((NfeDocumento?)null);
        _configResolver.ResolveAsync(cmd.EmpresaId).Returns(config);
        _numeracao.ReservarProximoNumeroAsync(cmd.EmpresaId).Returns(((short)1, 100L));
        _geradorChave
            .Gerar(Arg.Any<string>(), Arg.Any<string>(), Arg.Any<short>(), Arg.Any<long>(),
                Arg.Any<DateTime>(), Arg.Any<string>(), Arg.Any<byte>())
            .Returns("12345678901234567890123456789012345678901234");

        _uow.ExecuteInTransactionAsync(Arg.Any<Func<CancellationToken, Task<Guid>>>())
            .Returns(call => call.Arg<Func<CancellationToken, Task<Guid>>>().Invoke(CancellationToken.None));

        NfeDocumento? capturada = null;
        _nfeRepo.AddAsync(Arg.Do<NfeDocumento>(n => capturada = n))
            .Returns(Task.CompletedTask);

        _gateway.EmitirAsync(Arg.Any<NfeDocumento>(), Arg.Any<ConfigFiscalDto>())
            .Returns(new ResultadoEmissaoNfce(
                ChaveAcesso: "12345678901234567890123456789012345678901234",
                ProtocoloAutorizacao: "PROTO-NOVO",
                DataAutorizacao: DateTime.UtcNow,
                XmlAssinadoUrl: null,
                DanfeUrl: null));

        _nfeRepo.GetByIdWithDetailsAsync(cmd.EmpresaId, Arg.Any<Guid>())
            .Returns(call => capturada);
        _nfeRepo.GetByIdAsync(cmd.EmpresaId, Arg.Any<Guid>())
            .Returns(call => capturada);

        await NewUseCase().ExecuteAsync(cmd);

        capturada.Should().NotBeNull();
        capturada!.IdempotencyKey.Should().Be(cmd.IdempotencyKey);
    }

    private static ConfigFiscalDto ValidConfig(Guid empresaId) => new(
        EmpresaId: empresaId,
        Provedor: "focus",
        Ambiente: AmbienteIntegracao.Sandbox,
        RegimeTributario: RegimeTributario.Simples,
        Cnpj: "11444777000161",
        InscricaoEstadual: "ISENTO",
        InscricaoMunicipal: null,
        Endereco: new Endereco(Uf: "SP"),
        SerieNfce: 1,
        CredencialToken: "tok",
        CertificadoA1Bytes: null,
        CertificadoA1Senha: null,
        CscId: null,
        CscToken: null);

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
