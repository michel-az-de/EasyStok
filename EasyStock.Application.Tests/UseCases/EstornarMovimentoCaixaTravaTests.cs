using EasyStock.Application.Ports.Output.Persistence;
using EasyStock.Application.Tests.Helpers;
using EasyStock.Application.Common;
using EasyStock.Application.UseCases.Caixa;
using EasyStock.Application.UseCases.EstornarMovimentoCaixa;
using EasyStock.Application.UseCases.ObterCaixaDia;
using Microsoft.Extensions.Logging;

namespace EasyStock.Application.Tests.UseCases;

/// <summary>
/// #2736 (CXF-03): o fechamento toma a mesma trava do estorno. A guarda de dia fechado só vale lida atrás da trava,
/// senão um fechamento entre a guarda e a gravação deixa o estorno entrar num dia já retratado. A saída também
/// toma a trava, para a corrida ser serializada.
/// </summary>
public class EstornarMovimentoCaixaTravaTests
{
    private readonly ICaixaRepository _repo = Substitute.For<ICaixaRepository>();
    private readonly IUnitOfWork _uow = Substitute.For<IUnitOfWork>();
    private static readonly Guid Empresa = Guid.NewGuid();

    public EstornarMovimentoCaixaTravaTests() => _uow.SetupExecuteInTransactionSemRetry<MovimentoCaixaResult>();

    private EstornarMovimentoCaixaUseCase Novo() => new(_repo, _uow,
        new ObterCaixaDiaUseCase(new CaixaSaldoCalculator(_repo)),
        Substitute.For<ILogger<EstornarMovimentoCaixaUseCase>>());

    // O fechamento só "acontece" depois que o estorno toma a trava: simula o fechamento concorrente.
    private void FechamentoChegaDepoisDaTrava(MovimentoCaixa mov)
    {
        var travou = false;
        _repo.When(r => r.TravarSaidasAsync(Arg.Any<Guid>(), Arg.Any<Guid?>(), Arg.Any<CancellationToken>()))
            .Do(_ => travou = true);
        var dia = HorarioBrasil.DataOperacional(mov.DataMovimento);
        _repo.GetFechamentoDoDiaAsync(Empresa, dia, mov.LojaId)
            .Returns(_ => travou ? FechamentoCaixa.Criar(Empresa, dia, 0, 0, 0, 0, 0) : null);
    }

    [Fact]
    public async Task Estorno_de_entrada_recusa_o_dia_que_fechou_depois_da_trava()
    {
        var mov = MovimentoCaixa.Criar(Empresa, "entrada", 100m);
        mov.Metodo = "dinheiro";
        _repo.GetMovimentoAsync(Empresa, mov.Id).Returns(mov);
        _repo.GetMovimentosDoDiaAsync(Empresa, Arg.Any<DateOnly>(), Arg.Any<Guid?>()).Returns(new[] { mov });
        FechamentoChegaDepoisDaTrava(mov);

        var acao = () => Novo().ExecuteAsync(new EstornarMovimentoCaixaCommand(Empresa, mov.Id));

        await acao.Should().ThrowAsync<UseCaseValidationException>().WithMessage("*dia já fechado*");
        mov.EstornadoEm.Should().BeNull();
        await _repo.DidNotReceive().UpdateMovimentoAsync(Arg.Any<MovimentoCaixa>());
    }

    [Fact]
    public async Task Estorno_de_saida_toma_a_trava_e_recusa_o_dia_que_fechou_depois_dela()
    {
        var mov = MovimentoCaixa.Criar(Empresa, "saida", 30m);
        _repo.GetMovimentoAsync(Empresa, mov.Id).Returns(mov);
        FechamentoChegaDepoisDaTrava(mov);

        var acao = () => Novo().ExecuteAsync(new EstornarMovimentoCaixaCommand(Empresa, mov.Id));

        await acao.Should().ThrowAsync<UseCaseValidationException>().WithMessage("*dia já fechado*");
        await _repo.Received(1).TravarSaidasAsync(Empresa, mov.LojaId, Arg.Any<CancellationToken>());
        mov.EstornadoEm.Should().BeNull();
        await _repo.DidNotReceive().UpdateMovimentoAsync(Arg.Any<MovimentoCaixa>());
    }

    [Fact]
    public async Task Estorno_de_saida_toma_a_trava_antes_de_gravar()
    {
        var mov = MovimentoCaixa.Criar(Empresa, "saida", 30m);
        _repo.GetMovimentoAsync(Empresa, mov.Id).Returns(mov);

        var r = await Novo().ExecuteAsync(new EstornarMovimentoCaixaCommand(Empresa, mov.Id));

        r!.EstornadoEm.Should().NotBeNull();
        Received.InOrder(() =>
        {
            _repo.TravarSaidasAsync(Empresa, mov.LojaId, Arg.Any<CancellationToken>());
            _repo.UpdateMovimentoAsync(mov);
        });
    }

    [Fact]
    public async Task Estorno_que_outra_tela_fez_enquanto_esperava_a_trava_responde_com_ele()
    {
        // #2763: o movimento foi lido antes da trava; atrás dela, a releitura mostra o estorno de quem chegou antes.
        var mov = MovimentoCaixa.Criar(Empresa, "saida", 30m);
        _repo.GetMovimentoAsync(Empresa, mov.Id).Returns(mov);
        _repo.When(r => r.RecarregarMovimentoAsync(mov, Arg.Any<CancellationToken>()))
            .Do(_ => mov.Estornar(Guid.NewGuid(), "Ana", "primeiro"));

        var r = await Novo().ExecuteAsync(new EstornarMovimentoCaixaCommand(Empresa, mov.Id, "segundo", Guid.NewGuid(), "Bia"));

        (r!.EstornadoPorNome, r.MotivoEstorno).Should().Be(("Ana", "primeiro"));
        await _repo.DidNotReceive().UpdateMovimentoAsync(Arg.Any<MovimentoCaixa>());
        await _uow.DidNotReceive().CommitAsync();
        Received.InOrder(() =>
        {
            _repo.TravarSaidasAsync(Empresa, mov.LojaId, Arg.Any<CancellationToken>());
            _repo.RecarregarMovimentoAsync(mov, Arg.Any<CancellationToken>());
        });
    }
}
