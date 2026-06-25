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
/// Regressao do QA v1.10 (issue #685):
/// - BUG-002: saida natureza=Ajuste inflava "Receita total" e o card "Perdas / Ajustes"
///   so contava Perda (ignorava Ajuste/Prejuizo/Vencimento).
/// - BUG-003: saida estornada (EstornadaEm preenchido, nao deletada) seguia agregada,
///   entao o estorno nao revertia Receita/Unidades/registros.
///
/// Usa <see cref="EasyStockDbContext"/> InMemory + <see cref="MovimentacaoEstoqueRepository"/>
/// real porque a regra de agregacao mora no repositorio (LINQ puro, traduzivel em memoria).
/// </summary>
public sealed class MovimentacaoEstoqueRepositoryKpisTests : IDisposable
{
    private readonly EasyStockDbContext _db;
    private readonly MovimentacaoEstoqueRepository _repo;
    private readonly Guid _empresaId = Guid.NewGuid();

    public MovimentacaoEstoqueRepositoryKpisTests()
    {
        var currentUser = Substitute.For<ICurrentUserAccessor>();
        currentUser.IsAuthenticated.Returns(true);
        currentUser.EmpresaId.Returns(_empresaId);

        _db = new EasyStockDbContext(
            new DbContextOptionsBuilder<EasyStockDbContext>()
                .UseInMemoryDatabase($"mov-kpis-{Guid.NewGuid()}")
                .Options,
            currentUser);

        _repo = new MovimentacaoEstoqueRepository(_db);
    }

    private MovimentacaoEstoque Saida(
        NaturezaMovimentacaoEstoque natureza, int qtd, decimal valorTotal, bool estornada = false) =>
        new()
        {
            Id = Guid.NewGuid(),
            EmpresaId = _empresaId,
            ItemEstoqueId = Guid.NewGuid(),
            ProdutoId = Guid.NewGuid(),
            Tipo = TipoMovimentacaoEstoque.Saida,
            Natureza = natureza,
            Quantidade = Quantidade.From(qtd),
            ValorTotal = Dinheiro.FromDecimal(valorTotal),
            DataMovimentacao = DateTime.UtcNow,
            CriadoEm = DateTime.UtcNow,
            EstornadaEm = estornada ? DateTime.UtcNow : null
        };

    [Fact]
    public async Task GetKpis_ReceitaSoVenda_AjusteNaoInfla_PerdasAgrupa()
    {
        _db.MovimentacoesEstoque.AddRange(
            Saida(NaturezaMovimentacaoEstoque.Venda, 5, 500m),
            Saida(NaturezaMovimentacaoEstoque.Ajuste, 2, 60m),
            Saida(NaturezaMovimentacaoEstoque.Perda, 1, 30m),
            Saida(NaturezaMovimentacaoEstoque.Vencimento, 1, 10m));
        await _db.SaveChangesAsync();
        _db.ChangeTracker.Clear();

        var kpis = await _repo.GetKpisAsync(_empresaId, tipo: TipoMovimentacaoEstoque.Saida);

        kpis.ReceitaTotal.Should().Be(500m); // Ajuste/Perda/Vencimento NAO entram na receita
        kpis.TotalPerdas.Should().Be(3);     // Perda + Ajuste + Vencimento
        kpis.TotalVendas.Should().Be(1);
        kpis.TotalUnidades.Should().Be(9);   // 5 + 2 + 1 + 1
    }

    [Fact]
    public async Task GetKpis_ExcluiEstornadas()
    {
        _db.MovimentacoesEstoque.AddRange(
            Saida(NaturezaMovimentacaoEstoque.Venda, 5, 500m),
            Saida(NaturezaMovimentacaoEstoque.Venda, 2, 60m, estornada: true));
        await _db.SaveChangesAsync();
        _db.ChangeTracker.Clear();

        var kpis = await _repo.GetKpisAsync(_empresaId, tipo: TipoMovimentacaoEstoque.Saida);

        kpis.ReceitaTotal.Should().Be(500m); // estornada nao conta
        kpis.TotalVendas.Should().Be(1);
        kpis.TotalUnidades.Should().Be(5);
    }

    [Fact]
    public async Task GetByEmpresa_ExcluiEstornadas_DoCount()
    {
        // "Total de registros" do histórico vem do CountAsync de GetByEmpresaAsync; precisa
        // excluir estornadas para o estorno reverter o contador. (Não assertamos a lista
        // materializada aqui: o provider InMemory tem quirk com .Include — irrelevante em
        // Postgres, onde Include é LEFT JOIN. O count, sem Include, é o que importa.)
        _db.MovimentacoesEstoque.AddRange(
            Saida(NaturezaMovimentacaoEstoque.Venda, 5, 500m),
            Saida(NaturezaMovimentacaoEstoque.Venda, 2, 60m, estornada: true));
        await _db.SaveChangesAsync();
        _db.ChangeTracker.Clear();

        var (_, total) = await _repo.GetByEmpresaAsync(_empresaId, tipo: TipoMovimentacaoEstoque.Saida);

        total.Should().Be(1); // estornada não conta
    }

    public void Dispose() => _db.Dispose();
}
