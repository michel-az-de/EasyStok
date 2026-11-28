using EasyStock.Application.Ports.Output.Atendimento;
using EasyStock.Application.Ports.Output.Persistence;
using EasyStock.Application.UseCases.Atendimento;
using EasyStock.Domain.Entities.Atendimento;
using EasyStock.Domain.Enums.Atendimento;
using EasyStock.TestHelpers;

namespace EasyStock.Application.Tests.UseCases.Atendimento;

/// <summary>#3415: o modelo de retomada só é gravado se existir no catálogo da loja, com uma variável (o primeiro nome).</summary>
public class DefinirModeloRetomadaUseCaseTests
{
    private readonly Guid _empresaId = Guid.NewGuid();
    private readonly IConfiguracaoAtendimentoRepository _repositorio = Substitute.For<IConfiguracaoAtendimentoRepository>();
    private readonly ICatalogoModelosAtendimento _catalogo = Substitute.For<ICatalogoModelosAtendimento>();
    private readonly FakeUnitOfWork _uow = new();
    private readonly ConfiguracaoAtendimento _configuracao;

    public DefinirModeloRetomadaUseCaseTests()
    {
        _configuracao = ConfiguracaoAtendimento.CriarPadrao(_empresaId);
        _repositorio.GetByEmpresaIdAsync(_empresaId).Returns(_configuracao);
        _catalogo.UsaModelosLocaisAsync(_empresaId, Arg.Any<CancellationToken>()).Returns(true);
        _catalogo.ListarAsync(_empresaId, Arg.Any<CancellationToken>()).Returns([
            new ModeloAtendimento("retomada_conversa", "pt_BR", "UTILITY", "Oi {{1}}, podemos continuar?", 1, Origem: OrigemModelo.Local),
            new ModeloAtendimento("pedido_saiu", "pt_BR", "UTILITY", "Oi {{1}}, o pedido {{2}} saiu.", 2, Origem: OrigemModelo.Local)]);
    }

    private DefinirModeloRetomadaUseCase UseCase() => new(_repositorio, _uow, _catalogo);

    [Fact]
    public async Task ModeloDoCatalogoEGravadoComOIdiomaPadraoQuandoOConsoleNaoManda()
    {
        await UseCase().ExecuteAsync(_empresaId, "retomada_conversa", null);

        _configuracao.ModeloRetomada.Should().Be(new ModeloRetomada("retomada_conversa", "pt_BR"));
        _uow.CommitCount.Should().Be(1);
    }

    [Fact]
    public async Task NomeQueNaoExisteNoCatalogoERecusado()
    {
        var acao = () => UseCase().ExecuteAsync(_empresaId, "retomar_conversa", "pt_BR");

        await acao.Should().ThrowAsync<UseCaseValidationException>().WithMessage("*retomar_conversa*");
        await _repositorio.DidNotReceiveWithAnyArgs().UpdateAsync(default!);
        _uow.CommitCount.Should().Be(0);
    }

    [Fact]
    public async Task ModeloComMaisDeUmaVariavelERecusado()
    {
        var acao = () => UseCase().ExecuteAsync(_empresaId, "pedido_saiu", "pt_BR");

        await acao.Should().ThrowAsync<UseCaseValidationException>().WithMessage("*2 parâmetros*");
        _uow.CommitCount.Should().Be(0);
    }

    [Fact]
    public async Task NomeVazioDesligaSemConsultarOCatalogo()
    {
        _configuracao.DefinirModeloRetomada("retomada_conversa", "pt_BR");

        await UseCase().ExecuteAsync(_empresaId, "", null);

        _configuracao.ModeloRetomada.Should().BeNull();
        await _catalogo.DidNotReceiveWithAnyArgs().ListarAsync(default, default);
    }

    [Fact]
    public async Task ModeloIndisponivelERecusado()
    {
        _catalogo.ListarAsync(_empresaId, Arg.Any<CancellationToken>()).Returns([
            new ModeloAtendimento("retomada_conversa", "pt_BR", "UTILITY", "Oi {{1}}", 1, Indisponivel: "Mensagem arquivada.", Origem: OrigemModelo.Local)]);

        var acao = () => UseCase().ExecuteAsync(_empresaId, "retomada_conversa", null);

        await acao.Should().ThrowAsync<UseCaseValidationException>().WithMessage("*arquivada*");
        _uow.CommitCount.Should().Be(0);
    }

    [Fact]
    public async Task EmpresaNaMetaGravaSemConsultarOCatalogo()
    {
        _catalogo.UsaModelosLocaisAsync(_empresaId, Arg.Any<CancellationToken>()).Returns(false);

        await UseCase().ExecuteAsync(_empresaId, "retomar_conversa", null);

        _configuracao.ModeloRetomada.Should().Be(new ModeloRetomada("retomar_conversa", "pt_BR"));
        await _catalogo.DidNotReceiveWithAnyArgs().ListarAsync(default, default);
    }
}
