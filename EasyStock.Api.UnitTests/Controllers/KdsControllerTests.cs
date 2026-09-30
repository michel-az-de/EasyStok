using EasyStock.Api.Controllers;
using EasyStock.Api.Http;
using EasyStock.Application.Ports.Output;
using EasyStock.Application.Ports.Output.Atendimento;
using EasyStock.Application.Ports.Output.Integration;
using EasyStock.Application.Ports.Output.Persistence;
using EasyStock.Application.Services;
using EasyStock.Application.UseCases.AtualizarStatusPedido;
using EasyStock.Application.UseCases.Financeiro.ContasReceber;
using EasyStock.Application.UseCases.Financeiro.Integracao;
using EasyStock.Application.UseCases.Operacao.Kds;
using EasyStock.Domain.Entities;
using EasyStock.Domain.Enums;
using EasyStock.Domain.Sales;
using EasyStock.Infra.Postgre.Data;
using EasyStock.Infra.Postgre.Repositories;
using FluentAssertions;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using NSubstitute;

namespace EasyStock.Api.UnitTests.Controllers;

/// <summary>
/// S19 (#1155): KDS do console sobre <see cref="Pedido"/> com JWT. A leitura roda sobre o
/// <see cref="KdsPedidoQueries"/> real em EF InMemory com o filtro global de tenant desligado
/// (accessor SuperAdmin no DbContext), para provar o <c>EmpresaId</c> explícito no WHERE. A escrita
/// roda sobre o <see cref="AtualizarStatusPedidoUseCase"/> real com repositório substituto.
/// </summary>
public class KdsControllerTests : IDisposable
{
    private static readonly Guid EmpresaA = Guid.NewGuid();
    private static readonly Guid EmpresaB = Guid.NewGuid();

    private readonly EasyStockDbContext _db;
    private readonly IPedidoRepository _pedidoRepo = Substitute.For<IPedidoRepository>();
    private readonly IUnitOfWork _uow = Substitute.For<IUnitOfWork>();
    private readonly KdsController _controller;

    public KdsControllerTests()
    {
        var bypassTenant = Substitute.For<ICurrentUserAccessor>();
        bypassTenant.IsAuthenticated.Returns(true);
        bypassTenant.Nivel.Returns(NivelAcesso.SuperAdmin);
        _db = new EasyStockDbContext(
            new DbContextOptionsBuilder<EasyStockDbContext>()
                .UseInMemoryDatabase($"kds-tests-{Guid.NewGuid()}")
                .Options,
            bypassTenant);

        var operador = Substitute.For<ICurrentUserAccessor>();
        operador.IsAuthenticated.Returns(true);
        operador.Nivel.Returns(NivelAcesso.Operador);
        operador.EmpresaId.Returns(EmpresaA);
        operador.UsuarioId.Returns(Guid.NewGuid());

        var status = CriarAtualizarStatus();
        _controller = new KdsController(
            new ListarPedidosKdsUseCase(new KdsPedidoQueries(_db), TimeProvider.System),
            status,
            new AtualizarStatusPedidosEmLoteUseCase(status, _uow, NullLogger<AtualizarStatusPedidosEmLoteUseCase>.Instance),
            operador);
    }

    public void Dispose() => _db.Dispose();

    [Fact]
    public async Task ListaPorEmpresaComRotulo()
    {
        var daEmpresa = PedidoPersistido(EmpresaA, StatusPedidoMapper.Preparando, "paraServir", "prepararEmCasa");
        PedidoPersistido(EmpresaB, StatusPedidoMapper.Preparando, "paraServir");
        await _db.SaveChangesAsync();

        var result = await _controller.GetPedidos(status: null, linha: null, data: null, empresaId: null, CancellationToken.None);

        var cards = OkData<IReadOnlyList<KdsPedidoDto>>(result);
        var card = cards.Should().ContainSingle().Subject;
        card.Id.Should().Be(daEmpresa.Id);
        card.Status.Should().Be(StatusPedidoMapper.Preparando);
        card.StatusRotulo.Should().Be(StatusPedidoVocabulario.RotuloLojista(StatusPedido.Preparando));
        card.Linhas.Should().BeEquivalentTo("paraServir", "prepararEmCasa");
        card.Itens.Should().HaveCount(2);
    }

    [Fact]
    public async Task PatchInvalido400()
    {
        var pedido = PedidoEmMemoria(StatusPedidoMapper.Entregue);
        _pedidoRepo.GetByIdWithDetailsAsync(EmpresaA, pedido.Id).Returns(pedido);
        var esperado = Record.Exception(() =>
            PedidoStateMachine.EnsureTransicaoValida(StatusPedido.Entregue, StatusPedido.Preparando))!.Message;

        var result = await _controller.AtualizarStatus(pedido.Id, new KdsAtualizarStatusRequest("preparando"));

        var bad = result.Should().BeOfType<BadRequestObjectResult>().Subject;
        bad.Value.Should().BeOfType<ApiErrorResponse>().Which.Error.Message.Should().Be(esperado);
        pedido.Status.Should().Be(StatusPedidoMapper.Entregue);
        await _uow.DidNotReceive().CommitAsync();
    }

    [Fact]
    public async Task LoteContinuaAposErro()
    {
        var primeiro = PedidoEmMemoria(StatusPedidoMapper.Aguardando);
        var invalido = PedidoEmMemoria(StatusPedidoMapper.Entregue);
        var terceiro = PedidoEmMemoria(StatusPedidoMapper.Aguardando);
        foreach (var p in new[] { primeiro, invalido, terceiro })
            _pedidoRepo.GetByIdWithDetailsAsync(EmpresaA, p.Id).Returns(p);

        var result = await _controller.AtualizarStatusEmLote(
        [
            new KdsStatusLoteItem(primeiro.Id, "preparando"),
            new KdsStatusLoteItem(invalido.Id, "preparando"),
            new KdsStatusLoteItem(terceiro.Id, "preparando"),
        ]);

        var itens = OkData<IReadOnlyList<KdsStatusLoteResultado>>(result);
        itens.Select(i => i.Id).Should().Equal(primeiro.Id, invalido.Id, terceiro.Id);
        itens.Select(i => i.Sucesso).Should().Equal(true, false, true);
        itens[1].Erro.Should().NotBeNullOrWhiteSpace();
        primeiro.Status.Should().Be(StatusPedidoMapper.Preparando);
        invalido.Status.Should().Be(StatusPedidoMapper.Entregue);
        terceiro.Status.Should().Be(StatusPedidoMapper.Preparando);
        await _uow.Received(2).CommitAsync();
    }

    [Fact]
    public async Task FiltraPorLinha()
    {
        var emCasa = PedidoPersistido(EmpresaA, StatusPedidoMapper.Aguardando, "paraServir", "prepararEmCasa");
        PedidoPersistido(EmpresaA, StatusPedidoMapper.Aguardando, "paraServir");
        await _db.SaveChangesAsync();

        var result = await _controller.GetPedidos(status: null, linha: "preparar_em_casa", data: null, empresaId: null, CancellationToken.None);

        var cards = OkData<IReadOnlyList<KdsPedidoDto>>(result);
        cards.Should().ContainSingle().Which.Id.Should().Be(emCasa.Id);
    }

    [Fact]
    public async Task AtrasadoPeloInicioPrevisto()
    {
        // S21: atrasado = aguardando com o início previsto vencido; preparo começado não atrasa.
        var agora = DateTime.UtcNow;
        var vencido = PedidoPersistido(EmpresaA, StatusPedidoMapper.Aguardando, "paraServir");
        vencido.DefinirInicioPrevisto(agora.AddMinutes(-10));
        var noPrazo = PedidoPersistido(EmpresaA, StatusPedidoMapper.Aguardando, "paraServir");
        noPrazo.DefinirInicioPrevisto(agora.AddMinutes(60));
        var comecado = PedidoPersistido(EmpresaA, StatusPedidoMapper.Preparando, "paraServir");
        comecado.DefinirInicioPrevisto(agora.AddMinutes(-10));
        await _db.SaveChangesAsync();

        var result = await _controller.GetPedidos(status: null, linha: null, data: null, empresaId: null, CancellationToken.None);

        var cards = OkData<IReadOnlyList<KdsPedidoDto>>(result).ToDictionary(c => c.Id);
        cards[vencido.Id].Atrasado.Should().BeTrue();
        cards[vencido.Id].InicioPrevistoEm.Should().Be(vencido.InicioPrevistoEm);
        cards[noPrazo.Id].Atrasado.Should().BeFalse();
        cards[comecado.Id].Atrasado.Should().BeFalse("o preparo já começou");
    }

    [Fact]
    public async Task AprovacaoTrazMotivoEEnderecoSoDaEmpresa()
    {
        // F04 (#1221): a gaveta de Entregas lê do KDS o endereço do cliente (S14) e o motivo da
        // aprovação manual (S12). Pedido e cliente de outra empresa não aparecem.
        var cliente = Cliente.Criar(EmpresaA, "Ana");
        cliente.Endereco = "Rua das Flores, 10";
        cliente.Bairro = "Centro";
        cliente.Cidade = "Niterói";
        _db.Clientes.Add(cliente);
        var foraDeArea = PedidoPersistido(EmpresaA, StatusPedidoMapper.AguardandoAprovacaoBaba, "paraServir");
        foraDeArea.ClienteId = cliente.Id;
        foraDeArea.MarcarRequerAprovacao("fora_de_area");
        var outra = PedidoPersistido(EmpresaB, StatusPedidoMapper.AguardandoAprovacaoBaba, "paraServir");
        outra.MarcarRequerAprovacao("fora_de_area");
        await _db.SaveChangesAsync();

        var result = await _controller.GetPedidos(
            status: StatusPedidoMapper.AguardandoAprovacaoBaba, linha: null, data: null, empresaId: null, CancellationToken.None);

        var card = OkData<IReadOnlyList<KdsPedidoDto>>(result).Should().ContainSingle().Subject;
        card.Id.Should().Be(foraDeArea.Id);
        card.RequerAprovacao.Should().BeTrue();
        card.MotivoRequerAprovacao.Should().Be("fora_de_area");
        card.Endereco.Should().Be("Rua das Flores, 10, Centro, Niterói");
    }

    [Fact]
    public async Task SemClienteEnderecoNulo()
    {
        PedidoPersistido(EmpresaA, StatusPedidoMapper.Pronto, "paraServir");
        await _db.SaveChangesAsync();

        var result = await _controller.GetPedidos(status: null, linha: null, data: null, empresaId: null, CancellationToken.None);

        var card = OkData<IReadOnlyList<KdsPedidoDto>>(result).Should().ContainSingle().Subject;
        card.Endereco.Should().BeNull();
        card.RequerAprovacao.Should().BeFalse();
    }

    // ── helpers ──────────────────────────────────────────────────────────────

    private static T OkData<T>(IActionResult result)
    {
        var ok = result.Should().BeOfType<OkObjectResult>().Subject;
        return ok.Value.Should().BeOfType<ApiResponse<T>>().Subject.Data;
    }

    private Pedido PedidoPersistido(Guid empresaId, string status, params string[] linhas)
    {
        var pedido = PedidoEmMemoria(status, empresaId);
        foreach (var linha in linhas)
        {
            pedido.Itens.Add(new PedidoItem
            {
                Id = Guid.NewGuid(),
                PedidoId = pedido.Id,
                Nome = $"Item {linha}",
                Quantidade = 1,
                LinhaSnapshot = linha,
                CriadoEm = DateTime.UtcNow,
            });
        }
        _db.Pedidos.Add(pedido);
        return pedido;
    }

    private static Pedido PedidoEmMemoria(string status, Guid? empresaId = null) => new()
    {
        Id = Guid.NewGuid(),
        EmpresaId = empresaId ?? EmpresaA,
        Status = status,
        ClienteNome = "Cliente KDS",
        Total = EasyStock.Domain.ValueObjects.Dinheiro.FromDecimal(50m),
        CriadoEm = DateTime.UtcNow,
        AlteradoEm = DateTime.UtcNow,
    };

    private AtualizarStatusPedidoUseCase CriarAtualizarStatus()
    {
        var estoque = new PedidoEstoqueIntegrationService(
            Substitute.For<IItemEstoqueRepository>(),
            Substitute.For<IMovimentacaoEstoqueRepository>(),
            Substitute.For<EasyStock.Application.Ports.Output.Integration.IPublicadorEventoIntegracao>(),
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
        return new AtualizarStatusPedidoUseCase(
            _pedidoRepo,
            estoque,
            Substitute.For<IConfiguracaoLojaRepository>(),
            gerarContaReceber,
            Substitute.For<IPublicadorEventoIntegracao>(),
            Substitute.For<IOperacaoEventPublisher>(),
            _uow,
            NullLogger<AtualizarStatusPedidoUseCase>.Instance);
    }
}
