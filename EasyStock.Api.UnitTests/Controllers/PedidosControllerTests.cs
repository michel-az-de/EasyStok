using EasyStock.Api.Controllers;
using EasyStock.Api.Http;
using EasyStock.Application.Ports.Output;
using EasyStock.Application.Ports.Output.Persistence;
using EasyStock.Application.Services;
using EasyStock.Application.UseCases.AdicionarItemPedido;
using EasyStock.Application.UseCases.AlterarAgendamentoPedido;
using EasyStock.Application.UseCases.AtualizarStatusPedido;
using EasyStock.Application.UseCases.CancelarPedido;
using EasyStock.Application.UseCases.CriarPedido;
using EasyStock.Application.UseCases.Financeiro.ContasReceber;
using EasyStock.Application.UseCases.Financeiro.Integracao;
using EasyStock.Application.UseCases.ListarPedidosCliente;
using EasyStock.Application.UseCases.ObterPedidoDetalhes;
using EasyStock.Application.UseCases.RegistrarPagamentoPedido;
using EasyStock.Application.UseCases.RemoverItemPedido;
using EasyStock.Application.UseCases.RemoverPagamentoPedido;
using EasyStock.Domain.Entities;
using EasyStock.Domain.Enums;
using EasyStock.Domain.Sales;
using FluentAssertions;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using NSubstitute;

namespace EasyStock.Api.UnitTests.Controllers;

public class PedidosControllerTests
{
    private readonly IPedidoRepository _pedidoRepo = Substitute.For<IPedidoRepository>();
    private readonly IClienteRepository _clienteRepo = Substitute.For<IClienteRepository>();
    private readonly IProdutoRepository _produtoRepo = Substitute.For<IProdutoRepository>();
    private readonly IUnitOfWork _uow = Substitute.For<IUnitOfWork>();
    private readonly ICurrentUserAccessor _currentUser = Substitute.For<ICurrentUserAccessor>();
    private readonly PedidosController _controller;

    private static readonly Guid _empresaId = Guid.NewGuid();

    public PedidosControllerTests()
    {
        _currentUser.Nivel.Returns(NivelAcesso.SuperAdmin);
        _currentUser.UsuarioId.Returns(Guid.Empty);

        var criar = new CriarPedidoUseCase(
            _pedidoRepo, _clienteRepo, _produtoRepo, _uow,
            NullLogger<CriarPedidoUseCase>.Instance);

        var estoqueIntegration = new PedidoEstoqueIntegrationService(
            Substitute.For<IItemEstoqueRepository>(),
            Substitute.For<IMovimentacaoEstoqueRepository>(),
            Options.Create(new PedidoEstoqueOptions()),
            NullLogger<PedidoEstoqueIntegrationService>.Instance);

        var criarContaReceber = new CriarContaReceberUseCase(
            Substitute.For<IContaReceberRepository>(),
            Substitute.For<ICategoriaFinanceiraRepository>(),
            Substitute.For<ICentroCustoRepository>(),
            _uow,
            NullLogger<CriarContaReceberUseCase>.Instance);

        var gerarContaReceber = new GerarContaReceberDePedidoUseCase(
            Substitute.For<IContaReceberRepository>(),
            Substitute.For<ICategoriaFinanceiraRepository>(),
            Substitute.For<IConfiguracaoLojaRepository>(),
            criarContaReceber,
            NullLogger<GerarContaReceberDePedidoUseCase>.Instance);

        var status = new AtualizarStatusPedidoUseCase(
            _pedidoRepo,
            estoqueIntegration,
            Substitute.For<IConfiguracaoLojaRepository>(),
            gerarContaReceber,
            _uow,
            NullLogger<AtualizarStatusPedidoUseCase>.Instance);

        var cancelar = new CancelarPedidoUseCase(
            _pedidoRepo, _uow, NullLogger<CancelarPedidoUseCase>.Instance);

        var agendamento = new AlterarAgendamentoPedidoUseCase(
            _pedidoRepo, _uow, NullLogger<AlterarAgendamentoPedidoUseCase>.Instance);

        var listar = new ListarPedidosUseCase(_pedidoRepo);

        var obter = new ObterPedidoDetalhesUseCase(_pedidoRepo);

        var addItem = new AdicionarItemPedidoUseCase(
            _pedidoRepo, _produtoRepo, _uow, NullLogger<AdicionarItemPedidoUseCase>.Instance);

        var removeItem = new RemoverItemPedidoUseCase(
            _pedidoRepo, _uow, NullLogger<RemoverItemPedidoUseCase>.Instance);

        var addPag = new RegistrarPagamentoPedidoUseCase(
            _pedidoRepo, _uow, NullLogger<RegistrarPagamentoPedidoUseCase>.Instance);

        var removePag = new RemoverPagamentoPedidoUseCase(
            _pedidoRepo, _uow, NullLogger<RemoverPagamentoPedidoUseCase>.Instance);

        _controller = new PedidosController(
            criar, status, cancelar, agendamento,
            listar, obter, addItem, removeItem, addPag, removePag,
            _currentUser);
    }

    // ── helpers ──────────────────────────────────────────────────────────────

    private static T OkData<T>(IActionResult result)
    {
        result.Should().BeOfType<OkObjectResult>();
        var envelope = ((OkObjectResult)result).Value.Should().BeOfType<ApiResponse<T>>().Subject;
        return envelope.Data;
    }

    private static (IEnumerable<T> Items, PagedMeta Meta) OkPaged<T>(IActionResult result)
    {
        result.Should().BeOfType<OkObjectResult>();
        var envelope = ((OkObjectResult)result).Value.Should().BeOfType<ApiResponse<IEnumerable<T>>>().Subject;
        var meta = envelope.Meta.Should().BeOfType<PagedMeta>().Subject;
        return (envelope.Data, meta);
    }

    private static Pedido MakePedido(Guid empresaId, string status = "aguardando") => new()
    {
        Id = Guid.NewGuid(),
        EmpresaId = empresaId,
        Status = status,
        Total = EasyStock.Domain.ValueObjects.Dinheiro.FromDecimal(100m),
        CriadoEm = DateTime.UtcNow,
        AlteradoEm = DateTime.UtcNow,
    };

    // ── GetAll ────────────────────────────────────────────────────────────────

    [Fact]
    public async Task GetAll_DeveRetornarOkPaginado()
    {
        var pedido = MakePedido(_empresaId);
        _pedidoRepo.GetByEmpresaAsync(_empresaId, 1, 20, null, null, null, null, null, "criadoem", "desc")
            .Returns(((IEnumerable<Pedido>)[pedido], 1));

        var result = await _controller.GetAll(_empresaId);

        var (items, meta) = OkPaged<EasyStock.Application.UseCases.Pedidos.PedidoResult>(result);
        items.Should().ContainSingle();
        meta.Total.Should().Be(1);
    }

    [Fact]
    public async Task GetAll_DevePassarPaginacaoCorretaParaRepositorio()
    {
        _pedidoRepo.GetByEmpresaAsync(_empresaId, 2, 10, null, null, null, null, null, "criadoem", "desc")
            .Returns(((IEnumerable<Pedido>)[], 0));

        await _controller.GetAll(_empresaId, page: 2, pageSize: 10);

        await _pedidoRepo.Received(1).GetByEmpresaAsync(_empresaId, 2, 10, null, null, null, null, null, "criadoem", "desc");
    }

    [Fact]
    public async Task GetAll_DeveRetornarBadRequest_QuandoEmpresaIdVazioEhSuperAdmin()
    {
        var result = await _controller.GetAll(Guid.Empty);

        result.Should().BeOfType<BadRequestObjectResult>();
    }

    // ── GetById ───────────────────────────────────────────────────────────────

    [Fact]
    public async Task GetById_DeveRetornarOk_QuandoPedidoExiste()
    {
        var pedido = MakePedido(_empresaId);
        _pedidoRepo.GetByIdWithDetailsAsync(_empresaId, pedido.Id)
            .Returns(pedido);
        _pedidoRepo.GetEventosAsync(pedido.Id, Arg.Any<int>())
            .Returns(Task.FromResult<IEnumerable<PedidoEvento>>([]));

        var result = await _controller.GetById(pedido.Id, _empresaId);

        var data = OkData<EasyStock.Application.UseCases.Pedidos.PedidoDetalheResult>(result);
        data.Pedido.Id.Should().Be(pedido.Id);
    }

    [Fact]
    public async Task GetById_DeveRetornarNotFound_QuandoPedidoNaoExiste()
    {
        var id = Guid.NewGuid();
        _pedidoRepo.GetByIdWithDetailsAsync(_empresaId, id)
            .Returns((Pedido?)null);

        var result = await _controller.GetById(id, _empresaId);

        result.Should().BeOfType<NotFoundObjectResult>();
    }

    // ── Cancelar ──────────────────────────────────────────────────────────────

    [Fact]
    public async Task Cancelar_DeveRetornarOk_QuandoPedidoCancelado()
    {
        var pedido = MakePedido(_empresaId);
        _pedidoRepo.GetByIdAsync(_empresaId, pedido.Id)
            .Returns(pedido);

        var result = await _controller.Cancelar(pedido.Id, new CancelarPedidoCommand(_empresaId, pedido.Id));

        result.Should().BeOfType<OkObjectResult>();
        await _pedidoRepo.Received(1).AddEventoAsync(Arg.Any<PedidoEvento>());
        await _uow.Received(1).CommitAsync();
    }

    [Fact]
    public async Task Cancelar_DeveRetornarNotFound_QuandoPedidoNaoExiste()
    {
        var id = Guid.NewGuid();
        _pedidoRepo.GetByIdAsync(_empresaId, id)
            .Returns((Pedido?)null);

        var result = await _controller.Cancelar(id, new CancelarPedidoCommand(_empresaId, id));

        result.Should().BeOfType<NotFoundObjectResult>();
    }

    // ── Create ────────────────────────────────────────────────────────────────

    [Fact]
    public async Task Create_DeveRetornarCreated_QuandoPedidoCriado()
    {
        _clienteRepo.SearchAsync(Arg.Any<Guid>(), Arg.Any<string>(), Arg.Any<int>())
            .Returns(Task.FromResult<IEnumerable<Cliente>>([]));

        var command = new CriarPedidoCommand(_empresaId, ClienteNomeAdHoc: "João");

        var result = await _controller.Create(command);

        result.Should().BeOfType<CreatedResult>();
        await _pedidoRepo.Received(1).AddAsync(Arg.Any<Pedido>());
        await _uow.Received(1).CommitAsync();
    }
}
