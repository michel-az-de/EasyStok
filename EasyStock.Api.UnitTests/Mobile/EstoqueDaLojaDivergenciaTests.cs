using EasyStock.Api.Mobile.Controllers;
using EasyStock.Api.Services.Operacao;
using EasyStock.Application.Ports.Output;
using EasyStock.Domain.Entities;
using EasyStock.Domain.Entities.Mobile;
using EasyStock.Domain.ValueObjects;
using EasyStock.Infra.Postgre.Data;
using FluentAssertions;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging.Abstractions;
using NSubstitute;

namespace EasyStock.Api.UnitTests.Mobile;

/// <summary>
/// #2318 (PROD-4): a divergencia, o contador do painel e a reconciliacao do app comparam a MESMA soma
/// do estoque da loja (ProdutoId, LojaId). Item sem loja nao entra na soma de loja nenhuma.
/// </summary>
public sealed class EstoqueDaLojaDivergenciaTests : IDisposable
{
    private readonly Guid _empresaId = Guid.NewGuid();
    private readonly Guid _lojaA = Guid.NewGuid();
    private readonly Guid _lojaB = Guid.NewGuid();
    private readonly Guid _erpProdutoId = Guid.NewGuid();
    private readonly ICurrentUserAccessor _currentUser = Substitute.For<ICurrentUserAccessor>();
    private readonly EasyStockDbContext _db;

    public EstoqueDaLojaDivergenciaTests()
    {
        _currentUser.IsAuthenticated.Returns(true);
        _currentUser.EmpresaId.Returns(_empresaId);
        _db = new EasyStockDbContext(
            new DbContextOptionsBuilder<EasyStockDbContext>()
                .UseInMemoryDatabase($"estoque-loja-{Guid.NewGuid()}").Options,
            _currentUser);
    }

    public void Dispose() => _db.Dispose();

    private Product Mobile(Guid? lojaId, int stock)
    {
        var p = new Product
        {
            Id = Guid.NewGuid().ToString(), EmpresaId = _empresaId, LojaId = lojaId, Name = "Marmita",
            Stock = stock, ErpProductId = _erpProdutoId,
        };
        _db.Add(p);
        return p;
    }

    private void Item(Guid? lojaId, int qtd) => _db.Add(new ItemEstoque
    {
        Id = Guid.NewGuid(), EmpresaId = _empresaId, ProdutoId = _erpProdutoId, LojaId = lojaId,
        QuantidadeInicial = Quantidade.From(qtd), QuantidadeAtual = Quantidade.From(qtd), CustoUnitario = Dinheiro.Zero,
    });

    private MobileProductsController Produtos() =>
        new(_db, _currentUser, NullLogger<MobileProductsController>.Instance);

    private OperationController Operacao() =>
        new(_db, new OperacaoEventBroker(NullLogger<OperacaoEventBroker>.Instance), _currentUser,
            Substitute.For<IConfiguration>(), NullLogger<OperationController>.Instance);

    private async Task<StockDivergence[]> Divergencias() =>
        (StockDivergence[])((OkObjectResult)await Produtos().ListDivergences(_empresaId, default)).Value!;

    private async Task<int> ContadorDoPainel() =>
        ((OperationDashboard)((OkObjectResult)(await Operacao().GetDashboard(_empresaId, null, default)).Result!).Value!).DivergenciasEstoque;

    [Fact]
    public async Task Lista_e_contador_somam_os_lotes_da_loja()
    {
        Mobile(_lojaA, stock: 10);
        Item(_lojaA, 6);
        Item(_lojaA, 4);
        await _db.SaveChangesAsync();

        (await Divergencias()).Should().BeEmpty();
        (await ContadorDoPainel()).Should().Be(0);
    }

    [Fact]
    public async Task Contador_bate_com_a_lista_quando_ha_divergencia_na_soma()
    {
        Mobile(_lojaA, stock: 6);
        Item(_lojaA, 6);
        Item(_lojaA, 4);
        await _db.SaveChangesAsync();

        var lista = await Divergencias();
        lista.Should().ContainSingle().Which.ErpStock.Should().Be(10);
        (await ContadorDoPainel()).Should().Be(lista.Length);
    }

    [Fact]
    public async Task Item_sem_loja_nao_entra_na_soma_da_loja()
    {
        Mobile(_lojaA, stock: 10);
        Mobile(_lojaB, stock: 5);
        Item(_lojaA, 10);
        Item(_lojaB, 5);
        Item(null, 7);
        await _db.SaveChangesAsync();

        (await Divergencias()).Should().BeEmpty();
        (await ContadorDoPainel()).Should().Be(0);
    }

    [Fact]
    public async Task Reconciliar_leva_o_app_a_soma_da_loja()
    {
        var p = Mobile(_lojaA, stock: 3);
        Item(_lojaA, 6);
        Item(_lojaA, 4);
        Item(_lojaB, 9);
        await _db.SaveChangesAsync();

        var r = await Produtos().ReconcileStock(p.Id, _empresaId, default);

        r.Should().BeOfType<OkObjectResult>();
        (await _db.Set<Product>().AsNoTracking().SingleAsync(x => x.Id == p.Id)).Stock.Should().Be(10);
        (await Divergencias()).Should().BeEmpty();
    }
}
