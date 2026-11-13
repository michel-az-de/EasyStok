using EasyStock.Application.Ports.Output;
using EasyStock.Domain.Entities;
using EasyStock.Domain.Enums;
using EasyStock.Domain.ValueObjects;
using EasyStock.Infra.Postgre.Data;
using EasyStock.Infra.Postgre.Repositories;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using NSubstitute;

namespace EasyStock.Api.UnitTests.Repositories;

/// <summary>
/// #2318 (PROD-2): o filtro lojaId do estoque, dos contadores e dos lotes mora no repositorio.
/// Sem lojaId o resultado continua sendo o da empresa toda.
/// </summary>
public sealed class EstoqueELotesPorLojaRepositoryTests : IDisposable
{
    private readonly Guid _empresaId = Guid.NewGuid();
    private readonly Guid _lojaA = Guid.NewGuid();
    private readonly Guid _lojaB = Guid.NewGuid();
    private readonly EasyStockDbContext _db;

    public EstoqueELotesPorLojaRepositoryTests()
    {
        var currentUser = Substitute.For<ICurrentUserAccessor>();
        currentUser.IsAuthenticated.Returns(true);
        currentUser.EmpresaId.Returns(_empresaId);
        _db = new EasyStockDbContext(
            new DbContextOptionsBuilder<EasyStockDbContext>()
                .UseInMemoryDatabase($"estoque-lotes-loja-{Guid.NewGuid()}").Options,
            currentUser);
    }

    public void Dispose() => _db.Dispose();

    private void Item(Guid? lojaId, int qtd)
    {
        var produto = new Produto { Id = Guid.NewGuid(), EmpresaId = _empresaId, Nome = "Marmita", Status = StatusProduto.Ativo };
        _db.Add(produto);
        _db.Add(new ItemEstoque
        {
            Id = Guid.NewGuid(), EmpresaId = _empresaId, ProdutoId = produto.Id, LojaId = lojaId,
            QuantidadeInicial = Quantidade.From(qtd), QuantidadeAtual = Quantidade.From(qtd), CustoUnitario = Dinheiro.Zero,
        });
    }

    [Fact]
    public async Task Estoque_e_contadores_filtram_pela_loja_e_sem_loja_somam_a_empresa()
    {
        Item(_lojaA, 5);
        Item(_lojaA, 0);
        Item(_lojaB, 7);
        await _db.SaveChangesAsync();
        var repo = new ItemEstoqueRepository(_db);

        var (daLoja, total) = await repo.GetItensEstoquePaginadosAsync(_empresaId, lojaId: _lojaA);
        var (daEmpresa, totalEmpresa) = await repo.GetItensEstoquePaginadosAsync(_empresaId);
        var (cadastrados, comSaldo) = await repo.GetContadoresEstoqueAsync(_empresaId, lojaId: _lojaA);
        var (cadastradosEmpresa, _) = await repo.GetContadoresEstoqueAsync(_empresaId);

        total.Should().Be(2);
        daLoja.Should().HaveCount(2).And.OnlyContain(i => i.LojaId == _lojaA);
        totalEmpresa.Should().Be(3);
        daEmpresa.Should().HaveCount(3);
        cadastrados.Should().Be(2);
        comSaldo.Should().Be(1);
        cadastradosEmpresa.Should().Be(3);
    }

    [Fact]
    public async Task Item_sem_loja_aparece_na_listagem_e_nos_contadores_de_qualquer_loja_da_empresa()
    {
        Item(_lojaA, 5);
        Item(_lojaB, 7);
        Item(null, 3);
        await _db.SaveChangesAsync();
        var repo = new ItemEstoqueRepository(_db);

        var (daLojaA, totalA) = await repo.GetItensEstoquePaginadosAsync(_empresaId, lojaId: _lojaA);
        var (daLojaB, _) = await repo.GetItensEstoquePaginadosAsync(_empresaId, lojaId: _lojaB);
        var (cadastradosA, comSaldoA) = await repo.GetContadoresEstoqueAsync(_empresaId, lojaId: _lojaA);

        totalA.Should().Be(2);
        daLojaA.Should().Contain(i => i.LojaId == null).And.Contain(i => i.LojaId == _lojaA).And.NotContain(i => i.LojaId == _lojaB);
        daLojaB.Should().Contain(i => i.LojaId == null);
        cadastradosA.Should().Be(2);
        comSaldoA.Should().Be(2);
    }

    [Fact]
    public async Task Lote_sem_loja_aparece_na_visao_de_qualquer_loja_e_sem_lojaId_vem_a_empresa()
    {
        _db.Add(Lote.Criar(_empresaId, "LOT-A", lojaId: _lojaA));
        _db.Add(Lote.Criar(_empresaId, "LOT-B", lojaId: _lojaB));
        _db.Add(Lote.Criar(_empresaId, "LOT-SEM", lojaId: null));
        await _db.SaveChangesAsync();
        var repo = new LoteRepository(_db);

        var (daLoja, totalLoja) = await repo.ListAsync(_empresaId, 1, 20, lojaId: _lojaA);
        var (_, totalEmpresa) = await repo.ListAsync(_empresaId, 1, 20);

        totalLoja.Should().Be(2);
        daLoja.Select(l => l.Codigo).Should().BeEquivalentTo("LOT-A", "LOT-SEM");
        totalEmpresa.Should().Be(3);
    }

    [Fact]
    public async Task Lotes_filtram_pela_loja()
    {
        _db.Add(Lote.Criar(_empresaId, "LOT-A", lojaId: _lojaA));
        _db.Add(Lote.Criar(_empresaId, "LOT-B", lojaId: _lojaB));
        await _db.SaveChangesAsync();
        var repo = new LoteRepository(_db);

        var (daLoja, totalLoja) = await repo.ListAsync(_empresaId, 1, 20, lojaId: _lojaA);
        var (daEmpresa, totalEmpresa) = await repo.ListAsync(_empresaId, 1, 20);

        totalLoja.Should().Be(1);
        daLoja.Should().ContainSingle().Which.Codigo.Should().Be("LOT-A");
        totalEmpresa.Should().Be(2);
        daEmpresa.Should().HaveCount(2);
    }
}
