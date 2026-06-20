using EasyStock.Application.Ports.Output;
using EasyStock.Application.Ports.Output.Persistence;
using EasyStock.Application.UseCases.AdicionarItemPedido;
using EasyStock.Application.UseCases.Common;
using EasyStock.Domain.Entities;
using EasyStock.Domain.ValueObjects;
using EasyStock.Infra.Postgre.Data;
using EasyStock.Infra.Postgre.Repositories;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using NSubstitute;

namespace EasyStock.Api.UnitTests.UseCases;

/// <summary>
/// Regressao do QA v1.10 BUG-001 (issue #595): o total do pedido inflava ao
/// adicionar item porque o item entrava 2x em <c>pedido.Itens</c> (relationship
/// fixup do EF + <c>pedido.Itens.Add</c> manual) antes do <c>RecalcularTotal</c>.
///
/// O teste usa <see cref="EasyStockDbContext"/> com provider InMemory (fixup REAL
/// do change tracker) + <see cref="PedidoRepository"/> real. Mock de
/// <c>IPedidoRepository</c> NAO reproduz o bug porque nao faz fixup — por isso
/// este teste vive aqui (DbContext de verdade) e nao em Application.Tests.
/// Antes da fix este teste falharia: Total = 1019 (333 + 343 + 343).
/// </summary>
public sealed class AdicionarItemPedidoUseCaseTests : IDisposable
{
    private readonly EasyStockDbContext _db;
    private readonly Guid _empresaId = Guid.NewGuid();

    public AdicionarItemPedidoUseCaseTests()
    {
        // Tenant autenticado p/ o HasQueryFilter global enxergar o pedido semeado.
        var currentUser = Substitute.For<ICurrentUserAccessor>();
        currentUser.IsAuthenticated.Returns(true);
        currentUser.EmpresaId.Returns(_empresaId);

        _db = new EasyStockDbContext(
            new DbContextOptionsBuilder<EasyStockDbContext>()
                .UseInMemoryDatabase($"adicionar-item-pedido-{Guid.NewGuid()}")
                .Options,
            currentUser);
    }

    [Fact]
    public async Task AdicionarItem_DeveContarItemAdicionadoUmaVez_NaoDobrarOTotal()
    {
        // Arrange: pedido ja com 1 item de 333 (total 333), persistido.
        var pedido = Pedido.Criar(_empresaId);
        var itemInicial = new PedidoItem
        {
            Id = Guid.NewGuid(),
            PedidoId = pedido.Id,
            Nome = "inicial",
            Quantidade = 1m,
            PrecoUnitario = 333m,
            CriadoEm = DateTime.UtcNow
        };
        itemInicial.RecalcularSubtotal();
        pedido.Itens.Add(itemInicial);
        pedido.RecalcularTotal();

        _db.Pedidos.Add(pedido);
        await _db.SaveChangesAsync();
        _db.ChangeTracker.Clear(); // simula nova request: pedido recarregado tracked

        var uc = new AdicionarItemPedidoUseCase(
            new PedidoRepository(_db),
            Substitute.For<IProdutoRepository>(),
            _db, // EasyStockDbContext É o IUnitOfWork (CommitAsync = SaveChangesAsync)
            NullLogger<AdicionarItemPedidoUseCase>.Instance);

        // Act: adiciona 1 item de 343.
        var result = await uc.ExecuteAsync(
            new AdicionarItemPedidoCommand(_empresaId, pedido.Id, "teste", 1m, 343m));

        // Assert: total = 333 + 343 = 676 (nao 1019).
        result.Should().NotBeNull();
        result!.Total.Should().Be(676m,
            "o item adicionado deve contar 1x; 1019 significaria double-count (BUG-001)");

        // E o estado persistido: 2 itens, Total 676.
        _db.ChangeTracker.Clear();
        var persistido = await _db.Pedidos.Include(p => p.Itens).FirstAsync(p => p.Id == pedido.Id);
        persistido.Itens.Should().HaveCount(2);
        persistido.Total.Should().Be(Dinheiro.FromDecimal(676m));
    }

    [Fact]
    public async Task AdicionarItem_RejeitaPrecoZero()
    {
        // PED-01: item com preço 0 é rejeitado na origem (impede pedido entregue com
        // Total R$0). A validação ocorre antes de tocar o pedido — nem precisa existir.
        var uc = new AdicionarItemPedidoUseCase(
            new PedidoRepository(_db),
            Substitute.For<IProdutoRepository>(),
            _db,
            NullLogger<AdicionarItemPedidoUseCase>.Instance);

        var act = () => uc.ExecuteAsync(
            new AdicionarItemPedidoCommand(_empresaId, Guid.NewGuid(), "grátis", 1m, 0m));

        await act.Should().ThrowAsync<UseCaseValidationException>();
    }

    public void Dispose() => _db.Dispose();
}
