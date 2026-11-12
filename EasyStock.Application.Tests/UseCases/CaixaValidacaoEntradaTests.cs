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
}
