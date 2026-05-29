using EasyStock.Application.Ports.Output.Persistence;
using EasyStock.Application.UseCases.AbrirCaixa;
using EasyStock.Application.UseCases.EstornarMovimentoCaixa;
using EasyStock.Application.UseCases.FecharCaixa;
using EasyStock.Application.UseCases.RegistrarMovimentoCaixa;
using Microsoft.Extensions.Logging;

namespace EasyStock.Application.Tests.UseCases;

/// <summary>
/// Cobertura para os UCs do ciclo financeiro Caixa: Abrir, Fechar,
/// RegistrarMovimento, EstornarMovimento. Regressão aqui = inconsistência
/// financeira por tenant (alta gravidade).
/// </summary>
public class CaixaUseCasesTests
{
    private readonly ICaixaRepository _repo = Substitute.For<ICaixaRepository>();
    private readonly IUnitOfWork _uow = Substitute.For<IUnitOfWork>();

    // ════════════════════════════════════════════════════════════════════
    // AbrirCaixa
    // ════════════════════════════════════════════════════════════════════

    [Fact]
    public async Task AbrirCaixa_DeveLancarValidation_QuandoEmpresaIdVazio()
    {
        var useCase = new AbrirCaixaUseCase(_repo, _uow,
            Substitute.For<ILogger<AbrirCaixaUseCase>>());

        var act = () => useCase.ExecuteAsync(new AbrirCaixaCommand(Guid.Empty, 100m));

        await act.Should().ThrowAsync<UseCaseValidationException>();
    }

    [Fact]
    public async Task AbrirCaixa_DeveLancarValidation_QuandoSaldoInicialNegativo()
    {
        var useCase = new AbrirCaixaUseCase(_repo, _uow,
            Substitute.For<ILogger<AbrirCaixaUseCase>>());

        var act = () => useCase.ExecuteAsync(new AbrirCaixaCommand(Guid.NewGuid(), -1m));

        await act.Should().ThrowAsync<UseCaseValidationException>()
            .WithMessage("*negativo*");
    }

    [Fact]
    public async Task AbrirCaixa_DeveLancarValidation_QuandoCaixaJaFechadoNoMesmoDia()
    {
        var empresaId = Guid.NewGuid();
        var data = DateOnly.FromDateTime(DateTime.UtcNow);
        _repo.GetFechamentoDoDiaAsync(empresaId, data, null)
            .Returns(FechamentoCaixa.Criar(empresaId, data, 0, 0, 0, 0, 0));

        var useCase = new AbrirCaixaUseCase(_repo, _uow,
            Substitute.For<ILogger<AbrirCaixaUseCase>>());

        var act = () => useCase.ExecuteAsync(new AbrirCaixaCommand(empresaId, 100m));

        await act.Should().ThrowAsync<UseCaseValidationException>()
            .WithMessage("*já foi fechado*");
        await _repo.DidNotReceive().AddMovimentoAsync(Arg.Any<MovimentoCaixa>());
    }

    [Fact]
    public async Task AbrirCaixa_DeveCriarMovimentoAberturaECommitar_QuandoSucesso()
    {
        var empresaId = Guid.NewGuid();
        _repo.GetFechamentoDoDiaAsync(empresaId, Arg.Any<DateOnly>(), null)
            .Returns((FechamentoCaixa?)null);

        var useCase = new AbrirCaixaUseCase(_repo, _uow,
            Substitute.For<ILogger<AbrirCaixaUseCase>>());

        var result = await useCase.ExecuteAsync(new AbrirCaixaCommand(empresaId, 250m,
            RegistradoPorNome: "Operador X"));

        result.Tipo.Should().Be("abertura");
        result.Valor.Should().Be(250m);
        result.EmpresaId.Should().Be(empresaId);
        result.RegistradoPorNome.Should().Be("Operador X");
        await _repo.Received(1).AddMovimentoAsync(Arg.Is<MovimentoCaixa>(m =>
            m.Tipo == "abertura" && m.Valor == 250m && m.EmpresaId == empresaId));
        await _uow.Received(1).CommitAsync();
    }

    // ════════════════════════════════════════════════════════════════════
    // FecharCaixa
    // ════════════════════════════════════════════════════════════════════

    [Fact]
    public async Task FecharCaixa_DeveSerIdempotente_QuandoFechamentoJaExiste()
    {
        var empresaId = Guid.NewGuid();
        var data = DateOnly.FromDateTime(DateTime.UtcNow);
        var existente = FechamentoCaixa.Criar(empresaId, data, 100, 500, 0, 0, 0);
        _repo.GetFechamentoDoDiaAsync(empresaId, data, null).Returns(existente);

        var useCase = new FecharCaixaUseCase(_repo, _uow,
            Substitute.For<ILogger<FecharCaixaUseCase>>());

        var result = await useCase.ExecuteAsync(new FecharCaixaCommand(empresaId, data));

        result.Id.Should().Be(existente.Id);
        result.SaldoFinal.Should().Be(existente.SaldoFinal);
        await _repo.DidNotReceive().AddFechamentoAsync(Arg.Any<FechamentoCaixa>());
        await _uow.DidNotReceive().CommitAsync();
    }

    [Fact]
    public async Task FecharCaixa_DeveCalcularSaldoFinal_SomandoAberturaVendasEntradasMenosSaidas()
    {
        var empresaId = Guid.NewGuid();
        var data = DateOnly.FromDateTime(DateTime.UtcNow);

        var movimentos = new[]
        {
            MovimentoCaixa.Criar(empresaId, "abertura", 100m),
            MovimentoCaixa.Criar(empresaId, "entrada", 50m),
            MovimentoCaixa.Criar(empresaId, "entrada", 30m),
            MovimentoCaixa.Criar(empresaId, "saida", 20m),
        };
        _repo.GetFechamentoDoDiaAsync(empresaId, data, null).Returns((FechamentoCaixa?)null);
        _repo.GetMovimentosDoDiaAsync(empresaId, data, null).Returns(movimentos);
        _repo.GetTotalVendasDoDiaAsync(empresaId, data, null).Returns(500m);
        _repo.GetTotalPagamentosPedidosDoDiaAsync(empresaId, data, null).Returns(120m);

        var useCase = new FecharCaixaUseCase(_repo, _uow,
            Substitute.For<ILogger<FecharCaixaUseCase>>());

        var result = await useCase.ExecuteAsync(new FecharCaixaCommand(empresaId, data));

        // 100 abertura + 500 vendas + 120 pagamentos + 80 entradas - 20 saidas = 780
        result.SaldoInicial.Should().Be(100m);
        result.TotalVendas.Should().Be(500m);
        result.TotalPagamentosPedidos.Should().Be(120m);
        result.TotalEntradasExtras.Should().Be(80m);
        result.TotalSaidasExtras.Should().Be(20m);
        result.SaldoFinal.Should().Be(780m);

        await _repo.Received(1).AddFechamentoAsync(Arg.Any<FechamentoCaixa>());
        await _repo.Received(1).AddMovimentoAsync(Arg.Is<MovimentoCaixa>(m => m.Tipo == "fechamento"));
        await _uow.Received(1).CommitAsync();
    }

    // ════════════════════════════════════════════════════════════════════
    // RegistrarMovimentoCaixa
    // ════════════════════════════════════════════════════════════════════

    [Theory]
    [InlineData("invalido")]
    [InlineData("abertura")]    // só o UC AbrirCaixa pode criar
    [InlineData("fechamento")]  // só o UC FecharCaixa pode criar
    [InlineData("")]
    public async Task RegistrarMovimento_DeveLancarValidation_QuandoTipoNaoEEntradaNemSaida(string tipo)
    {
        var useCase = new RegistrarMovimentoCaixaUseCase(_repo, _uow,
            Substitute.For<ILogger<RegistrarMovimentoCaixaUseCase>>());

        var act = () => useCase.ExecuteAsync(
            new RegistrarMovimentoCaixaCommand(Guid.NewGuid(), tipo, 50m));

        await act.Should().ThrowAsync<UseCaseValidationException>()
            .WithMessage("*entrada*saida*");
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-10)]
    public async Task RegistrarMovimento_DeveLancarValidation_QuandoValorMenorOuIgualAZero(decimal valor)
    {
        var useCase = new RegistrarMovimentoCaixaUseCase(_repo, _uow,
            Substitute.For<ILogger<RegistrarMovimentoCaixaUseCase>>());

        var act = () => useCase.ExecuteAsync(
            new RegistrarMovimentoCaixaCommand(Guid.NewGuid(), "entrada", valor));

        await act.Should().ThrowAsync<UseCaseValidationException>()
            .WithMessage("*maior que zero*");
    }

    [Fact]
    public async Task RegistrarMovimento_DeveLancarValidation_QuandoCaixaJaFechado()
    {
        var empresaId = Guid.NewGuid();
        var data = DateOnly.FromDateTime(DateTime.UtcNow);
        _repo.GetFechamentoDoDiaAsync(empresaId, data, null)
            .Returns(FechamentoCaixa.Criar(empresaId, data, 0, 0, 0, 0, 0));

        var useCase = new RegistrarMovimentoCaixaUseCase(_repo, _uow,
            Substitute.For<ILogger<RegistrarMovimentoCaixaUseCase>>());

        var act = () => useCase.ExecuteAsync(
            new RegistrarMovimentoCaixaCommand(empresaId, "entrada", 50m));

        await act.Should().ThrowAsync<UseCaseValidationException>()
            .WithMessage("*já foi fechado*");
        await _repo.DidNotReceive().AddMovimentoAsync(Arg.Any<MovimentoCaixa>());
    }

    [Fact]
    public async Task RegistrarMovimento_DeveCommitarMovimento_QuandoEntradaValida()
    {
        var empresaId = Guid.NewGuid();
        _repo.GetFechamentoDoDiaAsync(empresaId, Arg.Any<DateOnly>(), null)
            .Returns((FechamentoCaixa?)null);

        var useCase = new RegistrarMovimentoCaixaUseCase(_repo, _uow,
            Substitute.For<ILogger<RegistrarMovimentoCaixaUseCase>>());

        var result = await useCase.ExecuteAsync(
            new RegistrarMovimentoCaixaCommand(empresaId, "ENTRADA", 75m,
                Descricao: "Reforço", Categoria: "Suprimento"));

        result.Tipo.Should().Be("entrada"); // case-insensitive
        result.Valor.Should().Be(75m);
        await _repo.Received(1).AddMovimentoAsync(Arg.Any<MovimentoCaixa>());
        await _uow.Received(1).CommitAsync();
    }

    // ════════════════════════════════════════════════════════════════════
    // EstornarMovimentoCaixa
    // ════════════════════════════════════════════════════════════════════

    [Fact]
    public async Task EstornarMovimento_DeveRetornarNull_QuandoNaoEncontrado()
    {
        var empresaId = Guid.NewGuid();
        var movId = Guid.NewGuid();
        _repo.GetMovimentoAsync(empresaId, movId).Returns((MovimentoCaixa?)null);

        var useCase = new EstornarMovimentoCaixaUseCase(_repo, _uow,
            Substitute.For<ILogger<EstornarMovimentoCaixaUseCase>>());

        var result = await useCase.ExecuteAsync(
            new EstornarMovimentoCaixaCommand(empresaId, movId));

        result.Should().BeNull();
        await _uow.DidNotReceive().CommitAsync();
    }

    [Fact]
    public async Task EstornarMovimento_DeveSerIdempotente_QuandoJaEstornado()
    {
        var empresaId = Guid.NewGuid();
        var mov = MovimentoCaixa.Criar(empresaId, "entrada", 50m);
        mov.Estornar(Guid.NewGuid(), "Operador A", "duplicado");
        _repo.GetMovimentoAsync(empresaId, mov.Id).Returns(mov);

        var useCase = new EstornarMovimentoCaixaUseCase(_repo, _uow,
            Substitute.For<ILogger<EstornarMovimentoCaixaUseCase>>());

        var result = await useCase.ExecuteAsync(
            new EstornarMovimentoCaixaCommand(empresaId, mov.Id, Motivo: "tentativa nova"));

        result.Should().NotBeNull();
        result!.EstornadoEm.Should().NotBeNull();
        result.MotivoEstorno.Should().Be("duplicado"); // motivo original preservado
        await _repo.DidNotReceive().UpdateMovimentoAsync(Arg.Any<MovimentoCaixa>());
        await _uow.DidNotReceive().CommitAsync();
    }

    [Fact]
    public async Task EstornarMovimento_DeveLancarValidation_QuandoDiaJaFechado()
    {
        var empresaId = Guid.NewGuid();
        var mov = MovimentoCaixa.Criar(empresaId, "entrada", 100m);
        var data = DateOnly.FromDateTime(mov.DataMovimento);
        _repo.GetMovimentoAsync(empresaId, mov.Id).Returns(mov);
        _repo.GetFechamentoDoDiaAsync(empresaId, data, mov.LojaId)
            .Returns(FechamentoCaixa.Criar(empresaId, data, 0, 0, 0, 0, 0));

        var useCase = new EstornarMovimentoCaixaUseCase(_repo, _uow,
            Substitute.For<ILogger<EstornarMovimentoCaixaUseCase>>());

        var act = () => useCase.ExecuteAsync(
            new EstornarMovimentoCaixaCommand(empresaId, mov.Id));

        await act.Should().ThrowAsync<UseCaseValidationException>()
            .WithMessage("*dia já fechado*");
        await _repo.DidNotReceive().UpdateMovimentoAsync(Arg.Any<MovimentoCaixa>());
    }

    [Fact]
    public async Task EstornarMovimento_DeveEstornarERegistrarMotivo_QuandoSucesso()
    {
        var empresaId = Guid.NewGuid();
        var operadorId = Guid.NewGuid();
        var mov = MovimentoCaixa.Criar(empresaId, "saida", 30m);
        var data = DateOnly.FromDateTime(mov.DataMovimento);
        _repo.GetMovimentoAsync(empresaId, mov.Id).Returns(mov);
        _repo.GetFechamentoDoDiaAsync(empresaId, data, mov.LojaId).Returns((FechamentoCaixa?)null);

        var useCase = new EstornarMovimentoCaixaUseCase(_repo, _uow,
            Substitute.For<ILogger<EstornarMovimentoCaixaUseCase>>());

        var result = await useCase.ExecuteAsync(
            new EstornarMovimentoCaixaCommand(empresaId, mov.Id,
                Motivo: "lançado errado", UsuarioId: operadorId, UsuarioNome: "Maria"));

        result.Should().NotBeNull();
        result!.EstornadoEm.Should().NotBeNull();
        result.MotivoEstorno.Should().Be("lançado errado");
        result.EstornadoPorUserId.Should().Be(operadorId);
        result.EstornadoPorNome.Should().Be("Maria");
        await _repo.Received(1).UpdateMovimentoAsync(mov);
        await _uow.Received(1).CommitAsync();
    }
}
