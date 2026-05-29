using EasyStock.Application.Ports.Output.Persistence;
using EasyStock.Application.UseCases.CancelarPedido;
using EasyStock.Application.UseCases.CriarPedido;
using EasyStock.Application.UseCases.RegistrarPagamentoPedido;
using EasyStock.Domain.ValueObjects;
using Microsoft.Extensions.Logging;

namespace EasyStock.Application.Tests.UseCases;

/// <summary>
/// Cobertura para o ciclo de pedidos (Onda P2): CriarPedido,
/// CancelarPedido, RegistrarPagamentoPedido. Fluxo critico cliente→venda
/// com auditoria via PedidoEvento.
/// </summary>
public class PedidoUseCasesTests
{
    private readonly IPedidoRepository _pedidoRepo = Substitute.For<IPedidoRepository>();
    private readonly IClienteRepository _clienteRepo = Substitute.For<IClienteRepository>();
    private readonly IProdutoRepository _produtoRepo = Substitute.For<IProdutoRepository>();
    private readonly IUnitOfWork _uow = Substitute.For<IUnitOfWork>();

    private CriarPedidoUseCase CriarPedidoUC() => new(_pedidoRepo, _clienteRepo, _produtoRepo, _uow,
        Substitute.For<ILogger<CriarPedidoUseCase>>());
    private CancelarPedidoUseCase CancelarUC() => new(_pedidoRepo, _uow,
        Substitute.For<ILogger<CancelarPedidoUseCase>>());
    private RegistrarPagamentoPedidoUseCase PagamentoUC() => new(_pedidoRepo, _uow,
        Substitute.For<ILogger<RegistrarPagamentoPedidoUseCase>>());

    private static Cliente CriarCliente(Guid empresaId, string nome = "Maria") => new()
    {
        Id = Guid.NewGuid(), EmpresaId = empresaId, Nome = nome,
        Telefone = "11999990000", Apt = "12B",
        Ativo = true, CriadoEm = DateTime.UtcNow
    };

    // ════════════════════════════════════════════════════════════════════
    // CriarPedido
    // ════════════════════════════════════════════════════════════════════

    [Fact]
    public async Task CriarPedido_DeveLancarValidation_QuandoEmpresaIdVazio()
    {
        var act = () => CriarPedidoUC().ExecuteAsync(new CriarPedidoCommand(Guid.Empty));
        await act.Should().ThrowAsync<UseCaseValidationException>();
    }

    [Theory]
    [InlineData(0, "Pão", 5)]      // qty 0
    [InlineData(-1, "Pão", 5)]     // qty negativa
    public async Task CriarPedido_DeveLancarValidation_QuandoItemTemQuantidadeMenorOuIgualZero(
        decimal qty, string nome, decimal preco)
    {
        var empresaId = Guid.NewGuid();
        var act = () => CriarPedidoUC().ExecuteAsync(new CriarPedidoCommand(empresaId,
            Itens: new[] { new CriarPedidoItemInput(nome, qty, preco) }));

        await act.Should().ThrowAsync<UseCaseValidationException>();
        await _pedidoRepo.DidNotReceive().AddAsync(Arg.Any<Pedido>());
    }

    [Fact]
    public async Task CriarPedido_DeveLancarValidation_QuandoItemSemNome()
    {
        var act = () => CriarPedidoUC().ExecuteAsync(new CriarPedidoCommand(Guid.NewGuid(),
            Itens: new[] { new CriarPedidoItemInput("  ", 1, 10) }));

        await act.Should().ThrowAsync<UseCaseValidationException>();
    }

    [Fact]
    public async Task CriarPedido_DeveLancarValidation_QuandoItemTemPrecoNegativo()
    {
        var act = () => CriarPedidoUC().ExecuteAsync(new CriarPedidoCommand(Guid.NewGuid(),
            Itens: new[] { new CriarPedidoItemInput("Item", 1, -5) }));

        await act.Should().ThrowAsync<UseCaseValidationException>();
    }

    [Fact]
    public async Task CriarPedido_DeveLancarValidation_QuandoClienteNaoEncontrado()
    {
        var empresaId = Guid.NewGuid();
        var clienteId = Guid.NewGuid();
        _clienteRepo.GetByIdAsync(empresaId, clienteId).Returns((Cliente?)null);

        var act = () => CriarPedidoUC().ExecuteAsync(new CriarPedidoCommand(empresaId,
            ClienteId: clienteId));

        await act.Should().ThrowAsync<UseCaseValidationException>()
            .WithMessage("*não encontrado nesta empresa*");
    }

    [Fact]
    public async Task CriarPedido_DeveBloquearProdutoCrossTenant()
    {
        var empresaId = Guid.NewGuid();
        var produtoId = Guid.NewGuid();
        // Produto de outra empresa: GetByIdAsync(empresaId, produtoId) retorna null
        _produtoRepo.GetByIdAsync(empresaId, produtoId).Returns((Produto?)null);

        var act = () => CriarPedidoUC().ExecuteAsync(new CriarPedidoCommand(empresaId,
            Itens: new[] { new CriarPedidoItemInput("Item alheio", 1, 10, ProdutoId: produtoId) }));

        await act.Should().ThrowAsync<UseCaseValidationException>()
            .WithMessage("*não pertence a esta empresa*");
        await _pedidoRepo.DidNotReceive().AddAsync(Arg.Any<Pedido>());
    }

    [Fact]
    public async Task CriarPedido_DeveCriarPedidoComSnapshotDoCliente_EAtualizarMetricas()
    {
        var empresaId = Guid.NewGuid();
        var cliente = CriarCliente(empresaId, "Joao");
        cliente.OrderCount = 2;
        var ordemAnterior = cliente.OrderCount;
        _clienteRepo.GetByIdAsync(empresaId, cliente.Id).Returns(cliente);

        Pedido? capturado = null;
        await _pedidoRepo.AddAsync(Arg.Do<Pedido>(p => capturado = p));

        var result = await CriarPedidoUC().ExecuteAsync(new CriarPedidoCommand(empresaId,
            ClienteId: cliente.Id,
            Itens: new[]
            {
                new CriarPedidoItemInput("Pão", 2, 5),
                new CriarPedidoItemInput("Bolo", 1, 30)
            },
            CriadoPorUserId: Guid.NewGuid(), CriadoPorNome: "Operador"));

        capturado.Should().NotBeNull();
        capturado!.ClienteId.Should().Be(cliente.Id);
        capturado.ClienteNome.Should().Be("Joao");
        capturado.ClienteTelefone.Should().Be(cliente.Telefone);
        capturado.ClienteApt.Should().Be(cliente.Apt);
        capturado.Itens.Should().HaveCount(2);
        capturado.Total.Should().Be(Dinheiro.FromDecimal(40m)); // 2*5 + 1*30
        capturado.Status.Should().Be("aguardando");
        capturado.Eventos.Should().ContainSingle(e => e.Tipo == "criado");

        // Metricas do cliente atualizadas
        cliente.OrderCount.Should().Be(ordemAnterior + 1);
        cliente.LastOrderAt.Should().NotBeNull();
        await _clienteRepo.Received(1).UpdateAsync(cliente);
        await _uow.Received(1).CommitAsync();
        result.Total.Should().Be(40m);
    }

    [Fact]
    public async Task CriarPedido_DeveCriarPedidoBalcao_QuandoSemClienteId()
    {
        var empresaId = Guid.NewGuid();
        Pedido? capturado = null;
        await _pedidoRepo.AddAsync(Arg.Do<Pedido>(p => capturado = p));

        await CriarPedidoUC().ExecuteAsync(new CriarPedidoCommand(empresaId,
            ClienteNomeAdHoc: "Walk-in", ClienteTelefoneAdHoc: "11000000000",
            Itens: new[] { new CriarPedidoItemInput("Café", 1, 8) }));

        capturado.Should().NotBeNull();
        capturado!.ClienteId.Should().BeNull();
        capturado.ClienteNome.Should().Be("Walk-in");
        capturado.Total.Should().Be(Dinheiro.FromDecimal(8m));
        // Sem cliente, não toca métrica
        await _clienteRepo.DidNotReceive().UpdateAsync(Arg.Any<Cliente>());
    }

    // ════════════════════════════════════════════════════════════════════
    // CriarPedido — Agendamento (F5)
    // ════════════════════════════════════════════════════════════════════

    [Fact]
    public async Task CriarPedido_DevePersistirSemAgendamento_QuandoAgendadoParaEmNulo()
    {
        var empresaId = Guid.NewGuid();
        Pedido? capturado = null;
        await _pedidoRepo.AddAsync(Arg.Do<Pedido>(p => capturado = p));

        await CriarPedidoUC().ExecuteAsync(new CriarPedidoCommand(empresaId,
            ClienteNomeAdHoc: "Teste",
            Itens: new[] { new CriarPedidoItemInput("Pão", 1, 5) }));

        capturado.Should().NotBeNull();
        capturado!.AgendadoParaEm.Should().BeNull();
    }

    [Fact]
    public async Task CriarPedido_DevePersistirAgendamento_QuandoAgendadoParaEmFuturo()
    {
        var empresaId = Guid.NewGuid();
        var dataFutura = DateTime.UtcNow.AddHours(2);
        Pedido? capturado = null;
        await _pedidoRepo.AddAsync(Arg.Do<Pedido>(p => capturado = p));

        var result = await CriarPedidoUC().ExecuteAsync(new CriarPedidoCommand(empresaId,
            ClienteNomeAdHoc: "Teste",
            Itens: new[] { new CriarPedidoItemInput("Bolo", 1, 30) },
            AgendadoParaEm: dataFutura));

        capturado.Should().NotBeNull();
        capturado!.AgendadoParaEm.Should().Be(dataFutura);
        result.AgendadoParaEm.Should().Be(dataFutura);
    }

    [Fact]
    public async Task CriarPedido_DeveLancarValidation_QuandoAgendadoParaEmNoPassado()
    {
        var empresaId = Guid.NewGuid();
        var dataPassada = DateTime.UtcNow.AddMinutes(-10);

        var act = () => CriarPedidoUC().ExecuteAsync(new CriarPedidoCommand(empresaId,
            ClienteNomeAdHoc: "Teste",
            Itens: new[] { new CriarPedidoItemInput("Café", 1, 8) },
            AgendadoParaEm: dataPassada));

        await act.Should().ThrowAsync<UseCaseValidationException>()
            .WithMessage("*futuro*");
        await _pedidoRepo.DidNotReceive().AddAsync(Arg.Any<Pedido>());
    }

    // ════════════════════════════════════════════════════════════════════
    // CancelarPedido
    // ════════════════════════════════════════════════════════════════════

    [Fact]
    public async Task CancelarPedido_DeveRetornarNull_QuandoNaoEncontrado()
    {
        var empresaId = Guid.NewGuid();
        var pedidoId = Guid.NewGuid();
        _pedidoRepo.GetByIdAsync(empresaId, pedidoId).Returns((Pedido?)null);

        var result = await CancelarUC().ExecuteAsync(new CancelarPedidoCommand(empresaId, pedidoId));

        result.Should().BeNull();
        await _uow.DidNotReceive().CommitAsync();
    }

    [Fact]
    public async Task CancelarPedido_DeveSerIdempotente_QuandoJaCancelado()
    {
        var empresaId = Guid.NewGuid();
        var pedido = Pedido.Criar(empresaId);
        pedido.Cancelar();
        var canceladoEmOriginal = pedido.CanceladoEm;
        _pedidoRepo.GetByIdAsync(empresaId, pedido.Id).Returns(pedido);

        var result = await CancelarUC().ExecuteAsync(
            new CancelarPedidoCommand(empresaId, pedido.Id, Motivo: "tentativa nova"));

        result.Should().NotBeNull();
        pedido.CanceladoEm.Should().Be(canceladoEmOriginal); // não sobrescreve
        await _pedidoRepo.DidNotReceive().AddEventoAsync(Arg.Any<PedidoEvento>());
        await _pedidoRepo.DidNotReceive().UpdateAsync(Arg.Any<Pedido>());
        await _uow.DidNotReceive().CommitAsync();
    }

    [Fact]
    public async Task CancelarPedido_DeveRegistrarEventoEAtualizar_QuandoSucesso()
    {
        var empresaId = Guid.NewGuid();
        var pedido = Pedido.Criar(empresaId);
        _pedidoRepo.GetByIdAsync(empresaId, pedido.Id).Returns(pedido);

        PedidoEvento? evento = null;
        await _pedidoRepo.AddEventoAsync(Arg.Do<PedidoEvento>(e => evento = e));

        var result = await CancelarUC().ExecuteAsync(new CancelarPedidoCommand(empresaId, pedido.Id,
            Motivo: "cliente desistiu",
            UsuarioId: Guid.NewGuid(), UsuarioNome: "Op"));

        result.Should().NotBeNull();
        pedido.Status.Should().Be("cancelado");
        pedido.CanceladoEm.Should().NotBeNull();
        evento.Should().NotBeNull();
        evento!.Tipo.Should().Be("cancelado");
        evento.StatusAntigo.Should().Be("aguardando");
        evento.StatusNovo.Should().Be("cancelado");
        evento.Detalhes.Should().Be("cliente desistiu");
        await _pedidoRepo.Received(1).UpdateAsync(pedido);
        await _uow.Received(1).CommitAsync();
    }

    // ════════════════════════════════════════════════════════════════════
    // RegistrarPagamentoPedido
    // ════════════════════════════════════════════════════════════════════

    [Theory]
    [InlineData(0)]
    [InlineData(-50)]
    public async Task RegistrarPagamento_DeveLancarValidation_QuandoValorMenorOuIgualZero(decimal valor)
    {
        var act = () => PagamentoUC().ExecuteAsync(
            new RegistrarPagamentoPedidoCommand(Guid.NewGuid(), Guid.NewGuid(), "pix", valor));

        await act.Should().ThrowAsync<UseCaseValidationException>()
            .WithMessage("*maior que zero*");
    }

    [Theory]
    [InlineData("ouro")]
    [InlineData("")]
    [InlineData("bitcoin")]
    public async Task RegistrarPagamento_DeveLancarValidation_QuandoMetodoInvalido(string metodo)
    {
        var act = () => PagamentoUC().ExecuteAsync(
            new RegistrarPagamentoPedidoCommand(Guid.NewGuid(), Guid.NewGuid(), metodo, 50m));

        await act.Should().ThrowAsync<UseCaseValidationException>()
            .WithMessage("*Método inválido*");
    }

    [Fact]
    public async Task RegistrarPagamento_DeveRetornarNull_QuandoPedidoNaoEncontrado()
    {
        var empresaId = Guid.NewGuid();
        var pedidoId = Guid.NewGuid();
        _pedidoRepo.GetByIdWithDetailsAsync(empresaId, pedidoId).Returns((Pedido?)null);

        var result = await PagamentoUC().ExecuteAsync(
            new RegistrarPagamentoPedidoCommand(empresaId, pedidoId, "pix", 25m));

        result.Should().BeNull();
        await _uow.DidNotReceive().CommitAsync();
    }

    [Fact]
    public async Task RegistrarPagamento_DeveAdicionarPagamentoEEvento_QuandoSucesso()
    {
        var empresaId = Guid.NewGuid();
        var pedido = Pedido.Criar(empresaId);
        pedido.Total = Dinheiro.FromDecimal(100m);
        _pedidoRepo.GetByIdWithDetailsAsync(empresaId, pedido.Id).Returns(pedido);

        PedidoPagamento? pagCapturado = null;
        await _pedidoRepo.AddPagamentoAsync(Arg.Do<PedidoPagamento>(p => pagCapturado = p));
        PedidoEvento? evento = null;
        await _pedidoRepo.AddEventoAsync(Arg.Do<PedidoEvento>(e => evento = e));

        var result = await PagamentoUC().ExecuteAsync(
            new RegistrarPagamentoPedidoCommand(empresaId, pedido.Id, "PIX", 60m,
                Referencia: "txid-abc", RegistradoPorNome: "Operador"));

        result.Should().NotBeNull();
        pagCapturado.Should().NotBeNull();
        pagCapturado!.Metodo.Should().Be("pix"); // case-insensitive
        pagCapturado.Valor.Should().Be(60m);
        pagCapturado.Referencia.Should().Be("txid-abc");
        pedido.Pagamentos.Should().Contain(pagCapturado);
        pedido.TotalPago.Should().Be(60m);
        evento.Should().NotBeNull();
        evento!.Tipo.Should().Be("pagamento");
        await _uow.Received(1).CommitAsync();
    }

    [Fact]
    public async Task RegistrarPagamento_DevePermitirPagamentoParcialEAcumular()
    {
        var empresaId = Guid.NewGuid();
        var pedido = Pedido.Criar(empresaId);
        pedido.Total = Dinheiro.FromDecimal(100m);
        pedido.Pagamentos.Add(new PedidoPagamento { Valor = 30m, Metodo = "dinheiro" });
        _pedidoRepo.GetByIdWithDetailsAsync(empresaId, pedido.Id).Returns(pedido);

        await PagamentoUC().ExecuteAsync(
            new RegistrarPagamentoPedidoCommand(empresaId, pedido.Id, "credito", 25m));

        pedido.TotalPago.Should().Be(55m); // 30 + 25
        pedido.Total.Should().Be(Dinheiro.FromDecimal(100m));    // total não muda
    }
}
