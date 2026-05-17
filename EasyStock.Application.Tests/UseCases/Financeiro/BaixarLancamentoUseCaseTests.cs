using EasyStock.Application.Ports.Output.Events;
using EasyStock.Application.Ports.Output.Persistence;
using EasyStock.Application.UseCases.Common;
using EasyStock.Application.UseCases.Financeiro.BaixarLancamento;
using EasyStock.Domain.Financeiro;
using EasyStock.Domain.Financeiro.Events;
using FluentAssertions;
using Microsoft.Extensions.Logging;
using NSubstitute;

namespace EasyStock.Application.Tests.UseCases.Financeiro;

public class BaixarLancamentoUseCaseTests
{
    private readonly ILancamentoRepository _repo = Substitute.For<ILancamentoRepository>();
    private readonly IUnitOfWork _uow = Substitute.For<IUnitOfWork>();
    private readonly IPublicadorEventos _publisher = Substitute.For<IPublicadorEventos>();
    private readonly ILogger<BaixarLancamentoUseCase> _logger = Substitute.For<ILogger<BaixarLancamentoUseCase>>();

    public BaixarLancamentoUseCaseTests()
    {
        // ExecuteInTransactionAsync<T> e configurado para invocar o action passado
        // diretamente (sem realmente abrir transacao), permitindo o teste validar a
        // logica de coordenacao do UseCase sem dependencias de infra.
        _uow.ExecuteInTransactionAsync(
                Arg.Any<Func<CancellationToken, Task<BaixarLancamentoResult>>>(),
                Arg.Any<CancellationToken>())
            .Returns(call => call.Arg<Func<CancellationToken, Task<BaixarLancamentoResult>>>()(CancellationToken.None));
    }

    private static Lancamento NovoLancamento(Guid empresaId, decimal valor = 100m) =>
        Lancamento.Criar(
            empresaId: empresaId,
            tipo: TipoLancamento.Receber,
            descricao: "Venda",
            valor: valor,
            dataEmissao: DateTime.UtcNow,
            dataVencimento: DateTime.UtcNow.AddDays(7));

    private BaixarLancamentoUseCase MakeSut() =>
        new(_repo, _uow, _logger, _publisher);

    [Fact]
    public async Task DeveLancarValidation_QuandoEmpresaIdVazio()
    {
        var sut = MakeSut();

        var act = () => sut.ExecuteAsync(new BaixarLancamentoCommand(
            EmpresaId: Guid.Empty,
            LancamentoId: Guid.NewGuid(),
            Valor: 10m,
            DataBaixa: DateTime.UtcNow,
            MeioPagamento: "pix"));

        await act.Should().ThrowAsync<UseCaseValidationException>();
    }

    [Fact]
    public async Task DeveLancarValidation_QuandoLancamentoNaoEncontrado()
    {
        var empresaId = Guid.NewGuid();
        var lancamentoId = Guid.NewGuid();
        _repo.GetWithLockAsync(empresaId, lancamentoId, Arg.Any<CancellationToken>())
            .Returns((Lancamento?)null);

        var sut = MakeSut();
        var act = () => sut.ExecuteAsync(new BaixarLancamentoCommand(
            empresaId, lancamentoId, 10m, DateTime.UtcNow, "pix"));

        await act.Should().ThrowAsync<UseCaseValidationException>()
            .WithMessage("*nao encontrado*");
    }

    [Fact]
    public async Task DeveAplicarBaixaTotal_QuitarECommitarComEvento()
    {
        var empresaId = Guid.NewGuid();
        var lancamento = NovoLancamento(empresaId, 100m);
        _repo.GetWithLockAsync(empresaId, lancamento.Id, Arg.Any<CancellationToken>())
            .Returns(lancamento);

        var sut = MakeSut();
        var result = await sut.ExecuteAsync(new BaixarLancamentoCommand(
            empresaId, lancamento.Id, 100m, DateTime.UtcNow, "pix",
            ChaveExterna: "txid-1"));

        result.StatusResultante.Should().Be(StatusLancamento.Quitado);
        result.ValorRestante.Should().Be(0m);
        result.ValorBaixado.Should().Be(100m);
        result.LancamentoId.Should().Be(lancamento.Id);

        await _repo.Received(1).UpdateAsync(lancamento, Arg.Any<CancellationToken>());
        await _uow.Received(1).CommitAsync();
        await _publisher.Received(1).PublicarAsync(Arg.Is<LancamentoBaixadoEvent>(e =>
            e.LancamentoId == lancamento.Id &&
            e.ValorBaixado == 100m &&
            e.StatusResultante == StatusLancamento.Quitado));
    }

    [Fact]
    public async Task DeveAplicarBaixaParcial_MantendoStatusParcial()
    {
        var empresaId = Guid.NewGuid();
        var lancamento = NovoLancamento(empresaId, 200m);
        _repo.GetWithLockAsync(empresaId, lancamento.Id, Arg.Any<CancellationToken>())
            .Returns(lancamento);

        var sut = MakeSut();
        var result = await sut.ExecuteAsync(new BaixarLancamentoCommand(
            empresaId, lancamento.Id, 80m, DateTime.UtcNow, "dinheiro"));

        result.StatusResultante.Should().Be(StatusLancamento.Parcial);
        result.ValorRestante.Should().Be(120m);
    }

    [Fact]
    public async Task DeveSerIdempotente_QuandoMesmaChaveExternaReapresentada()
    {
        var empresaId = Guid.NewGuid();
        var lancamento = NovoLancamento(empresaId, 100m);
        _repo.GetWithLockAsync(empresaId, lancamento.Id, Arg.Any<CancellationToken>())
            .Returns(lancamento);

        var sut = MakeSut();
        var primeira = await sut.ExecuteAsync(new BaixarLancamentoCommand(
            empresaId, lancamento.Id, 50m, DateTime.UtcNow, "pix",
            ChaveExterna: "abc"));
        var segunda = await sut.ExecuteAsync(new BaixarLancamentoCommand(
            empresaId, lancamento.Id, 50m, DateTime.UtcNow, "pix",
            ChaveExterna: "abc"));

        primeira.BaixaId.Should().Be(segunda.BaixaId);
        lancamento.Baixas.Should().ContainSingle();
        lancamento.TotalBaixado.Should().Be(50m);
    }

    [Fact]
    public async Task DeveLancarValidation_QuandoValorExcedeRestante()
    {
        var empresaId = Guid.NewGuid();
        var lancamento = NovoLancamento(empresaId, 100m);
        lancamento.RegistrarBaixa(60m, DateTime.UtcNow, "pix");
        _repo.GetWithLockAsync(empresaId, lancamento.Id, Arg.Any<CancellationToken>())
            .Returns(lancamento);

        var sut = MakeSut();
        var act = () => sut.ExecuteAsync(new BaixarLancamentoCommand(
            empresaId, lancamento.Id, 50m, DateTime.UtcNow, "dinheiro"));

        await act.Should().ThrowAsync<UseCaseValidationException>()
            .WithMessage("*excede*");
    }

    [Fact]
    public async Task DeveLancarValidation_QuandoLancamentoCancelado()
    {
        var empresaId = Guid.NewGuid();
        var lancamento = NovoLancamento(empresaId, 100m);
        lancamento.Cancelar("teste");
        _repo.GetWithLockAsync(empresaId, lancamento.Id, Arg.Any<CancellationToken>())
            .Returns(lancamento);

        var sut = MakeSut();
        var act = () => sut.ExecuteAsync(new BaixarLancamentoCommand(
            empresaId, lancamento.Id, 50m, DateTime.UtcNow, "pix"));

        await act.Should().ThrowAsync<UseCaseValidationException>();
    }

    [Fact]
    public async Task DeveValidarTenantIsolation_QuandoLancamentoDeOutraEmpresa()
    {
        var empresaA = Guid.NewGuid();
        var empresaB = Guid.NewGuid();
        var lancamento = NovoLancamento(empresaB, 100m);
        // GetWithLockAsync cumpre o filtro de tenant. Para validar a defesa
        // em profundidade, simulamos um repo que devolveu por engano.
        _repo.GetWithLockAsync(empresaA, lancamento.Id, Arg.Any<CancellationToken>())
            .Returns(lancamento);

        var sut = MakeSut();
        var act = () => sut.ExecuteAsync(new BaixarLancamentoCommand(
            empresaA, lancamento.Id, 50m, DateTime.UtcNow, "pix"));

        await act.Should().ThrowAsync<UseCaseValidationException>()
            .WithMessage("*nao pertence*");
    }

    [Fact]
    public async Task NaoDeveCommitar_QuandoValidacaoFalha()
    {
        var empresaId = Guid.NewGuid();
        var lancamento = NovoLancamento(empresaId, 100m);
        lancamento.Cancelar("teste");
        _repo.GetWithLockAsync(empresaId, lancamento.Id, Arg.Any<CancellationToken>())
            .Returns(lancamento);

        var sut = MakeSut();
        try { await sut.ExecuteAsync(new BaixarLancamentoCommand(
            empresaId, lancamento.Id, 50m, DateTime.UtcNow, "pix")); }
        catch (UseCaseValidationException) { /* esperado */ }

        await _uow.DidNotReceive().CommitAsync();
        await _publisher.DidNotReceive().PublicarAsync(Arg.Any<LancamentoBaixadoEvent>());
    }
}
