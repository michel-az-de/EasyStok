using EasyStock.Application.Ports.Output.Persistence;
using EasyStock.Application.UseCases.CriarPedido;
using Microsoft.Extensions.Logging;

namespace EasyStock.Application.Tests.UseCases;

public class CriarPedidoUseCaseTests
{
    private readonly IPedidoRepository _pedidoRepo = Substitute.For<IPedidoRepository>();
    private readonly IClienteRepository _clienteRepo = Substitute.For<IClienteRepository>();
    private readonly IProdutoRepository _produtoRepo = Substitute.For<IProdutoRepository>();
    private readonly IUnitOfWork _uow = Substitute.For<IUnitOfWork>();

    private CriarPedidoUseCase Sut() => new(_pedidoRepo, _clienteRepo, _produtoRepo, _uow,
        Substitute.For<ILogger<CriarPedidoUseCase>>());

    [Fact]
    public async Task DeveNormalizarAgendadoParaEm_ParaUtc_QuandoClienteEnviaDataSemFuso()
    {
        // Regressão (estabilidade): a data agendada do cliente chega Kind=Unspecified
        // e o Postgres (timestamp with time zone) rejeita no save. O use case deve
        // normalizar para UTC antes de persistir.
        var empresaId = Guid.NewGuid();
        Pedido? salvo = null;
        _pedidoRepo.When(r => r.AddAsync(Arg.Any<Pedido>())).Do(ci => salvo = ci.Arg<Pedido>());

        var futuroSemFuso = DateTime.SpecifyKind(DateTime.UtcNow.AddDays(1), DateTimeKind.Unspecified);

        await Sut().ExecuteAsync(new CriarPedidoCommand(empresaId, AgendadoParaEm: futuroSemFuso));

        salvo.Should().NotBeNull();
        salvo!.AgendadoParaEm.Should().NotBeNull();
        salvo.AgendadoParaEm!.Value.Kind.Should().Be(DateTimeKind.Utc);
        await _uow.Received(1).CommitAsync();
    }

    [Fact]
    public async Task DeveCriarPedido_SemAgendamento_QuandoAgendadoParaEmNull()
    {
        var empresaId = Guid.NewGuid();

        var result = await Sut().ExecuteAsync(new CriarPedidoCommand(empresaId));

        result.AgendadoParaEm.Should().BeNull();
        await _uow.Received(1).CommitAsync();
    }

    [Fact]
    public async Task DeveLancarValidation_QuandoAgendadoNoPassado()
    {
        var empresaId = Guid.NewGuid();
        var passadoSemFuso = DateTime.SpecifyKind(DateTime.UtcNow.AddDays(-1), DateTimeKind.Unspecified);

        var act = () => Sut().ExecuteAsync(new CriarPedidoCommand(empresaId, AgendadoParaEm: passadoSemFuso));

        await act.Should().ThrowAsync<UseCaseValidationException>().WithMessage("*futuro*");
        await _uow.DidNotReceive().CommitAsync();
    }

    // ── Itens: cenários absurdos (não podem virar 500) ─────────────────────────

    [Fact]
    public async Task DeveLancarValidation_QuandoItemComQuantidadeZeroOuNegativa()
    {
        var empresaId = Guid.NewGuid();
        var cmd = new CriarPedidoCommand(empresaId, Itens: new[]
        {
            new CriarPedidoItemInput("Pão", Quantidade: 0m, PrecoUnitario: 5m),
        });

        var act = () => Sut().ExecuteAsync(cmd);

        await act.Should().ThrowAsync<UseCaseValidationException>();
        await _uow.DidNotReceive().CommitAsync();
    }

    [Fact]
    public async Task DeveLancarValidation_QuandoItemComPrecoNegativo()
    {
        var empresaId = Guid.NewGuid();
        var cmd = new CriarPedidoCommand(empresaId, Itens: new[]
        {
            new CriarPedidoItemInput("Pão", Quantidade: 1m, PrecoUnitario: -5m),
        });

        var act = () => Sut().ExecuteAsync(cmd);

        await act.Should().ThrowAsync<UseCaseValidationException>();
        await _uow.DidNotReceive().CommitAsync();
    }

    [Fact]
    public async Task DeveLancarValidation_QuandoItemSemNome()
    {
        var empresaId = Guid.NewGuid();
        var cmd = new CriarPedidoCommand(empresaId, Itens: new[]
        {
            new CriarPedidoItemInput("   ", Quantidade: 1m, PrecoUnitario: 5m),
        });

        var act = () => Sut().ExecuteAsync(cmd);

        await act.Should().ThrowAsync<UseCaseValidationException>();
        await _uow.DidNotReceive().CommitAsync();
    }

    [Fact]
    public async Task DeveLancarValidation_QuandoProdutoDoItemNaoPertenceAEmpresa()
    {
        // Cross-tenant: item referencia ProdutoId que não existe nesta empresa.
        var empresaId = Guid.NewGuid();
        // _produtoRepo.GetByIdAsync(...) não configurado → null → deve barrar.
        var cmd = new CriarPedidoCommand(empresaId, Itens: new[]
        {
            new CriarPedidoItemInput("Farinha", Quantidade: 1m, PrecoUnitario: 5m, ProdutoId: Guid.NewGuid()),
        });

        var act = () => Sut().ExecuteAsync(cmd);

        await act.Should().ThrowAsync<UseCaseValidationException>().WithMessage("*empresa*");
        await _uow.DidNotReceive().CommitAsync();
    }

    [Fact]
    public async Task DeveLancarValidation_QuandoClienteInexistente()
    {
        var empresaId = Guid.NewGuid();
        // _clienteRepo.GetByIdAsync(empresaId, id) não configurado → null.
        var cmd = new CriarPedidoCommand(empresaId, ClienteId: Guid.NewGuid());

        var act = () => Sut().ExecuteAsync(cmd);

        await act.Should().ThrowAsync<UseCaseValidationException>().WithMessage("*liente*");
        await _uow.DidNotReceive().CommitAsync();
    }

    [Fact]
    public async Task DeveRecalcularTotal_ComMultiplosItens()
    {
        var empresaId = Guid.NewGuid();
        Pedido? salvo = null;
        _pedidoRepo.When(r => r.AddAsync(Arg.Any<Pedido>())).Do(ci => salvo = ci.Arg<Pedido>());

        var cmd = new CriarPedidoCommand(empresaId, Itens: new[]
        {
            new CriarPedidoItemInput("Pão",   Quantidade: 2m, PrecoUnitario: 10m), // 20
            new CriarPedidoItemInput("Leite", Quantidade: 3m, PrecoUnitario: 5m),  // 15
        });

        await Sut().ExecuteAsync(cmd);

        salvo.Should().NotBeNull();
        salvo!.Itens.Should().HaveCount(2);
        ((decimal)salvo.Total).Should().Be(35m);
        await _uow.Received(1).CommitAsync();
    }

    [Fact]
    public async Task DeveAplicarSnapshotAdHoc_QuandoSemCliente()
    {
        var empresaId = Guid.NewGuid();
        Pedido? salvo = null;
        _pedidoRepo.When(r => r.AddAsync(Arg.Any<Pedido>())).Do(ci => salvo = ci.Arg<Pedido>());

        var cmd = new CriarPedidoCommand(empresaId,
            ClienteNomeAdHoc: "Maria", ClienteAptAdHoc: "101", ClienteTelefoneAdHoc: "11999990000");

        await Sut().ExecuteAsync(cmd);

        salvo.Should().NotBeNull();
        salvo!.ClienteId.Should().BeNull();
        salvo.ClienteNome.Should().Be("Maria");
        salvo.ClienteApt.Should().Be("101");
        salvo.ClienteTelefone.Should().Be("11999990000");
        await _uow.Received(1).CommitAsync();
    }
}
