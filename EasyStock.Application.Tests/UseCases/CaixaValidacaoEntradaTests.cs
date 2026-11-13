using EasyStock.Application.Ports.Output.Persistence;
using EasyStock.Application.UseCases.AbrirCaixa;
using EasyStock.Application.UseCases.Caixa;
using EasyStock.Application.UseCases.ObterCaixaDia;
using EasyStock.Application.UseCases.RegistrarMovimentoCaixa;
using Microsoft.Extensions.Logging;

namespace EasyStock.Application.Tests.UseCases;

/// <summary>#2305 (CAIXA-1, 2, 3): método fora da lista, mais de duas casas e valor acima do teto não entram no caixa.</summary>
public class CaixaValidacaoEntradaTests
{
    private readonly ICaixaRepository _repo = Substitute.For<ICaixaRepository>();
    private readonly IUnitOfWork _uow = Substitute.For<IUnitOfWork>();

    private RegistrarMovimentoCaixaUseCase Registrar() => new(_repo, _uow,
        new ObterCaixaDiaUseCase(new CaixaSaldoCalculator(_repo)),
        Substitute.For<ILogger<RegistrarMovimentoCaixaUseCase>>());

    private AbrirCaixaUseCase Abrir() => new(_repo, _uow, Substitute.For<ILogger<AbrirCaixaUseCase>>());

    [Theory]
    [InlineData("XPTO")]
    [InlineData("cartao")]
    [InlineData("bitcoin")]
    public async Task RegistrarMovimento_RecusaMetodoForaDaLista(string metodo)
    {
        var act = () => Registrar().ExecuteAsync(
            new RegistrarMovimentoCaixaCommand(Guid.NewGuid(), "entrada", 10m, Metodo: metodo));

        await act.Should().ThrowAsync<UseCaseValidationException>().WithMessage("*Forma de pagamento*");
        await _repo.DidNotReceive().AddMovimentoAsync(Arg.Any<MovimentoCaixa>());
    }

    [Theory]
    [InlineData("Dinheiro ", "dinheiro")]
    [InlineData(" PIX", "pix")]
    public async Task RegistrarMovimento_GravaMetodoNormalizado(string enviado, string esperado)
    {
        await Registrar().ExecuteAsync(
            new RegistrarMovimentoCaixaCommand(Guid.NewGuid(), "entrada", 10m, Metodo: enviado));

        await _repo.Received(1).AddMovimentoAsync(Arg.Is<MovimentoCaixa>(m => m.Metodo == esperado));
    }

    [Theory]
    [InlineData("0.004")]
    [InlineData("20.005")]
    public async Task RegistrarMovimento_RecusaMaisDeDuasCasas(string valor)
    {
        var act = () => Registrar().ExecuteAsync(
            new RegistrarMovimentoCaixaCommand(Guid.NewGuid(), "entrada", decimal.Parse(valor, System.Globalization.CultureInfo.InvariantCulture)));

        await act.Should().ThrowAsync<UseCaseValidationException>().WithMessage("*duas casas*");
        await _repo.DidNotReceive().AddMovimentoAsync(Arg.Any<MovimentoCaixa>());
    }

    [Fact]
    public async Task RegistrarMovimento_MobileComTresCasasGravaArredondadoSemRecusar()
    {
        // A mutação do app é fato já gravado no aparelho: recusar vira perda permanente na fila de sync.
        await Registrar().ExecuteAsync(
            new RegistrarMovimentoCaixaCommand(Guid.NewGuid(), "entrada", 12.345m, Origem: "mobile"));

        await _repo.Received(1).AddMovimentoAsync(Arg.Is<MovimentoCaixa>(m => m.Valor == 12.35m));
    }

    [Fact]
    public async Task RegistrarMovimento_RecusaValorAcimaDoTeto()
    {
        var act = () => Registrar().ExecuteAsync(
            new RegistrarMovimentoCaixaCommand(Guid.NewGuid(), "entrada", 10_000_000_000_000m));

        await act.Should().ThrowAsync<UseCaseValidationException>().WithMessage("*999.999.999*");
        await _repo.DidNotReceive().AddMovimentoAsync(Arg.Any<MovimentoCaixa>());
    }

    [Fact]
    public async Task AbrirCaixa_RecusaMaisDeDuasCasas()
    {
        var act = () => Abrir().ExecuteAsync(new AbrirCaixaCommand(Guid.NewGuid(), 100.555m));

        await act.Should().ThrowAsync<UseCaseValidationException>().WithMessage("*duas casas*");
        await _repo.DidNotReceive().AddMovimentoAsync(Arg.Any<MovimentoCaixa>());
    }

    [Fact]
    public async Task AbrirCaixa_RecusaValorAcimaDoTeto()
    {
        var act = () => Abrir().ExecuteAsync(new AbrirCaixaCommand(Guid.NewGuid(), 10_000_000_000_000m));

        await act.Should().ThrowAsync<UseCaseValidationException>().WithMessage("*999.999.999*");
    }

    [Theory]
    [InlineData("entrada")]
    [InlineData("saida")]
    public async Task RegistrarMovimento_Manual_SemCaixaAberto_PedeParaAbrir(string tipo)
    {
        var empresaId = Guid.NewGuid();
        _repo.GetAberturaPendenteAsync(empresaId, Arg.Any<Guid?>()).Returns((MovimentoCaixa?)null);

        var act = () => Registrar().ExecuteAsync(new RegistrarMovimentoCaixaCommand(
            empresaId, tipo, 10m, "teste", Metodo: "dinheiro", ExigirCaixaAberto: true));

        await act.Should().ThrowAsync<UseCaseValidationException>().WithMessage("Abra o caixa antes de lançar*");
        await _repo.DidNotReceive().AddMovimentoAsync(Arg.Any<MovimentoCaixa>());
    }

    [Fact]
    public async Task RegistrarMovimento_Manual_ComCaixaAberto_Grava()
    {
        var empresaId = Guid.NewGuid();
        _repo.GetAberturaPendenteAsync(empresaId, Arg.Any<Guid?>())
            .Returns(MovimentoCaixa.Criar(empresaId, "abertura", 50m, DateTime.UtcNow, null));

        await Registrar().ExecuteAsync(new RegistrarMovimentoCaixaCommand(
            empresaId, "entrada", 10m, Metodo: "dinheiro", ExigirCaixaAberto: true));

        await _repo.Received(1).AddMovimentoAsync(Arg.Any<MovimentoCaixa>());
    }

    // #2440: a abertura da empresa (sem loja) vale para o lançamento com loja, como já vale no fechamento (#1562) e no pix (#2341).
    [Fact]
    public async Task RegistrarMovimento_Manual_ComLoja_AceitaAberturaDaEmpresaSemLoja()
    {
        var empresaId = Guid.NewGuid();
        var lojaId = Guid.NewGuid();
        _repo.GetAberturaPendenteAsync(empresaId, lojaId).Returns((MovimentoCaixa?)null);
        _repo.GetAberturaPendenteAsync(empresaId, null)
            .Returns(MovimentoCaixa.Criar(empresaId, "abertura", 50m, DateTime.UtcNow, null));

        await Registrar().ExecuteAsync(new RegistrarMovimentoCaixaCommand(
            empresaId, "entrada", 10m, LojaId: lojaId, Metodo: "dinheiro", ExigirCaixaAberto: true));

        await _repo.Received(1).AddMovimentoAsync(Arg.Any<MovimentoCaixa>());
    }

    [Fact]
    public async Task RegistrarMovimento_Manual_ComLoja_NaoAceitaAberturaDeOutraLoja()
    {
        var empresaId = Guid.NewGuid();
        var lojaId = Guid.NewGuid();
        _repo.GetAberturaPendenteAsync(empresaId, lojaId).Returns((MovimentoCaixa?)null);
        // Com lojaId nulo o repositório não filtra por loja: pode devolver a abertura de outra loja.
        _repo.GetAberturaPendenteAsync(empresaId, null)
            .Returns(MovimentoCaixa.Criar(empresaId, "abertura", 50m, DateTime.UtcNow, Guid.NewGuid()));

        var act = () => Registrar().ExecuteAsync(new RegistrarMovimentoCaixaCommand(
            empresaId, "entrada", 10m, LojaId: lojaId, Metodo: "dinheiro", ExigirCaixaAberto: true));

        await act.Should().ThrowAsync<UseCaseValidationException>().WithMessage("Abra o caixa antes de lançar*");
        await _repo.DidNotReceive().AddMovimentoAsync(Arg.Any<MovimentoCaixa>());
    }

    [Theory]
    [InlineData(-4)]
    [InlineData(-2)]
    public async Task RegistrarMovimento_Manual_RecusaDiaForaDoFechamentoEDoDiaAtual(int dias)
    {
        var empresaId = Guid.NewGuid();
        var lojaId = Guid.NewGuid();
        var agora = DateTime.UtcNow;
        _repo.GetAberturaPendenteAsync(empresaId, lojaId)
            .Returns(MovimentoCaixa.Criar(empresaId, "abertura", 50m, agora.AddDays(-3), lojaId));

        var act = () => Registrar().ExecuteAsync(new RegistrarMovimentoCaixaCommand(
            empresaId, "entrada", 10m, LojaId: lojaId, Metodo: "dinheiro",
            DataMovimento: agora.AddDays(dias), ExigirCaixaAberto: true));

        await act.Should().ThrowAsync<UseCaseValidationException>().WithMessage("*dia da abertura*hoje*");
        await _repo.DidNotReceive().AddMovimentoAsync(Arg.Any<MovimentoCaixa>());
    }

    [Theory]
    [InlineData(-3)]
    [InlineData(0)]
    public async Task RegistrarMovimento_Manual_PreservaRetroativoDaAberturaEHoje(int dias)
    {
        var empresaId = Guid.NewGuid();
        var agora = DateTime.UtcNow;
        var instante = agora.AddDays(dias);
        _repo.GetAberturaPendenteAsync(empresaId, Arg.Any<Guid?>())
            .Returns(MovimentoCaixa.Criar(empresaId, "abertura", 50m, agora.AddDays(-3), null));

        await Registrar().ExecuteAsync(new RegistrarMovimentoCaixaCommand(
            empresaId, "entrada", 10m, Metodo: "dinheiro", DataMovimento: instante, ExigirCaixaAberto: true));

        await _repo.Received(1).AddMovimentoAsync(Arg.Is<MovimentoCaixa>(m => m.DataMovimento == instante));
    }

    [Theory]
    [InlineData(2, 59, true)]
    [InlineData(3, 0, false)]
    public async Task RegistrarMovimento_Manual_UsaLimiteDoDiaEmBrasilia(int horaUtc, int minuto, bool permitido)
    {
        var empresaId = Guid.NewGuid();
        var abertura = DateTime.UtcNow.Date.AddDays(-3).AddHours(12);
        var instante = abertura.Date.AddDays(1).AddHours(horaUtc).AddMinutes(minuto);
        _repo.GetAberturaPendenteAsync(empresaId, Arg.Any<Guid?>())
            .Returns(MovimentoCaixa.Criar(empresaId, "abertura", 50m, abertura, null));
        var act = () => Registrar().ExecuteAsync(new RegistrarMovimentoCaixaCommand(
            empresaId, "entrada", 10m, Metodo: "dinheiro", DataMovimento: instante, ExigirCaixaAberto: true));

        if (permitido)
        {
            await act();
            await _repo.Received(1).AddMovimentoAsync(Arg.Is<MovimentoCaixa>(m => m.DataMovimento == instante));
        }
        else
        {
            await act.Should().ThrowAsync<UseCaseValidationException>().WithMessage("*dia da abertura*hoje*");
            await _repo.DidNotReceive().AddMovimentoAsync(Arg.Any<MovimentoCaixa>());
        }
    }

    [Theory]
    [InlineData("2026-06-01T23:30:00Z")]
    [InlineData("2026-06-01T20:30:00-03:00")]
    [InlineData("2026-06-02T01:30:00+02:00")]
    [InlineData("2026-06-01T23:30:00")]
    public async Task RegistrarMovimento_Manual_NormalizaInstanteJsonAntesDeValidarEGravar(string dataJson)
    {
        var empresaId = Guid.NewGuid();
        var instante = System.Text.Json.JsonSerializer.Deserialize<DateTime>($"\"{dataJson}\"");
        var esperado = new DateTime(2026, 6, 1, 23, 30, 0, DateTimeKind.Utc);
        _repo.GetAberturaPendenteAsync(empresaId, Arg.Any<Guid?>())
            .Returns(MovimentoCaixa.Criar(empresaId, "abertura", 50m, esperado.Date.AddHours(12), null));

        await Registrar().ExecuteAsync(new RegistrarMovimentoCaixaCommand(
            empresaId, "entrada", 10m, Metodo: "dinheiro", DataMovimento: instante, ExigirCaixaAberto: true));

        await _repo.Received(1).AddMovimentoAsync(Arg.Is<MovimentoCaixa>(m =>
            m.DataMovimento.Kind == DateTimeKind.Utc && m.DataMovimento == esperado));
    }

    [Fact]
    public async Task RegistrarMovimento_Automatico_SemCaixaAberto_ContinuaGravando()
    {
        var empresaId = Guid.NewGuid();
        _repo.GetAberturaPendenteAsync(empresaId, Arg.Any<Guid?>()).Returns((MovimentoCaixa?)null);

        await Registrar().ExecuteAsync(new RegistrarMovimentoCaixaCommand(empresaId, "entrada", 10m, Origem: "mobile"));

        await _repo.Received(1).AddMovimentoAsync(Arg.Any<MovimentoCaixa>());
    }
}
