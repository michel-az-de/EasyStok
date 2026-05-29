using EasyStock.Application.Ports.Output.Fiscal;
using EasyStock.Application.Ports.Output.Persistence;
using EasyStock.Application.Services.Fiscal;
using EasyStock.Application.UseCases.Fiscal.CancelarNfe;
using EasyStock.Domain.Fiscal;
using Microsoft.Extensions.Logging;

namespace EasyStock.Application.Tests.UseCases.Fiscal;

public class CancelarNfeUseCaseTests
{
    private readonly INfeRepository _nfeRepo = Substitute.For<INfeRepository>();
    private readonly IGatewayFiscal _gateway = Substitute.For<IGatewayFiscal>();
    private readonly IGatewayFiscalFactory _gatewayFactory = Substitute.For<IGatewayFiscalFactory>();
    private readonly IConfigFiscalResolver _configResolver = Substitute.For<IConfigFiscalResolver>();
    private readonly IUnitOfWork _uow = Substitute.For<IUnitOfWork>();
    private readonly ILogger<CancelarNfeUseCase> _logger = Substitute.For<ILogger<CancelarNfeUseCase>>();

    private CancelarNfeUseCase NewUseCase()
    {
        _gatewayFactory.ObterPara(Arg.Any<string>()).Returns(_gateway);
        return new(_nfeRepo, _gatewayFactory, _configResolver, _uow, _logger);
    }

    [Fact]
    public async Task ExecuteAsync_ComMotivoMenor15Chars_LancaUseCaseValidationException()
    {
        var cmd = new CancelarNfeCommand(
            EmpresaId: Guid.NewGuid(),
            NfeId: Guid.NewGuid(),
            Motivo: "muito curto");

        var act = async () => await NewUseCase().ExecuteAsync(cmd);

        await act.Should().ThrowAsync<UseCaseValidationException>()
            .WithMessage("*15 caracteres*");
    }

    [Fact]
    public async Task ExecuteAsync_NotaNaoEncontrada_LancaRegraDeDominioVioladaException()
    {
        var empresaId = Guid.NewGuid();
        var nfeId = Guid.NewGuid();
        _nfeRepo.GetByIdAsync(empresaId, nfeId, Arg.Any<CancellationToken>()).Returns((NfeDocumento?)null);

        var cmd = new CancelarNfeCommand(
            EmpresaId: empresaId,
            NfeId: nfeId,
            Motivo: "Cliente desistiu da compra apos pagamento.");

        var act = async () => await NewUseCase().ExecuteAsync(cmd);

        await act.Should().ThrowAsync<RegraDeDominioVioladaException>();
    }

    /// <summary>
    /// Regressao B-055: gateway tem que ser chamado ANTES do commit. Se gateway rejeitar,
    /// transacao nao roda — nota fica Autorizada.
    /// </summary>
    [Fact]
    public async Task ExecuteAsync_GatewayRejeita_NaoCommitaCancelamento()
    {
        var empresaId = Guid.NewGuid();
        var nfe = CriarNfeAutorizada(empresaId);
        _nfeRepo.GetByIdAsync(empresaId, nfe.Id, Arg.Any<CancellationToken>()).Returns(nfe);
        _configResolver.ResolveAsync(empresaId, Arg.Any<CancellationToken>())
            .Returns(NovaConfigMock(empresaId));
        _gateway.CancelarAsync(Arg.Any<NfeDocumento>(), Arg.Any<string>(), Arg.Any<ConfigFiscalDto>(), Arg.Any<CancellationToken>())
            .Returns<Task<ResultadoCancelamentoNfce>>(_ => throw new GatewayFiscalRejeitadaException("Prazo expirado", "218"));

        var cmd = new CancelarNfeCommand(
            EmpresaId: empresaId,
            NfeId: nfe.Id,
            Motivo: "Cliente desistiu da compra apos pagamento.");

        var act = async () => await NewUseCase().ExecuteAsync(cmd);

        await act.Should().ThrowAsync<GatewayFiscalRejeitadaException>();
        // Confirma que NAO commitou: uow nunca foi chamado
        await _uow.DidNotReceive().ExecuteInTransactionAsync(Arg.Any<Func<CancellationToken, Task>>(), Arg.Any<CancellationToken>());
        nfe.Status.Should().Be(StatusNfe.Autorizada);
    }

    private static ConfigFiscalDto NovaConfigMock(Guid empresaId) => new(
        EmpresaId: empresaId,
        Provedor: "mock",
        Ambiente: EasyStock.Domain.Integration.AmbienteIntegracao.Sandbox,
        RegimeTributario: EasyStock.Domain.Fiscal.RegimeTributario.Simples,
        Cnpj: "11444777000161",
        InscricaoEstadual: "ISENTO",
        InscricaoMunicipal: null,
        Endereco: null,
        SerieNfce: 1,
        CredencialToken: null,
        CertificadoA1Bytes: null,
        CertificadoA1Senha: null,
        CscId: null,
        CscToken: null);

    private static NfeDocumento CriarNfeAutorizada(Guid empresaId)
    {
        var nfe = NfeDocumento.Criar(
            empresaId: empresaId,
            pedidoId: Guid.NewGuid(),
            serie: 1,
            numero: 1L,
            dadosEmitente: new EasyStock.Domain.ValueObjects.DadosEmissor("Emp Teste", "11444777000161"),
            dadosDestinatario: null,
            totalNota: EasyStock.Domain.ValueObjects.Dinheiro.FromDecimal(100m));

        nfe.AdicionarItem(
            nomeSnapshot: "Produto", quantidade: 1m,
            precoUnitario: EasyStock.Domain.ValueObjects.Dinheiro.FromDecimal(100m),
            unidade: "UN");
        nfe.MarcarEnviada();
        nfe.MarcarAutorizada("12345678901234567890123456789012345678901234", "PROTO123");
        return nfe;
    }
}
