using EasyStock.Api.Mobile.DTOs;
using EasyStock.Api.Mobile.Services;
using EasyStock.Application.Ports.Output;
using EasyStock.Domain.Entities;
using EasyStock.Domain.Entities.Mobile;
using EasyStock.Domain.Enums;
using EasyStock.Domain.ValueObjects;
using EasyStock.Infra.Postgre.Data;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging.Abstractions;
using NSubstitute;

namespace EasyStock.Api.UnitTests.Mobile;

/// <summary>
/// SYNC-2 (#2306): alteração de Produto ERP já espelhado em mobile_products não chegava ao app.
/// </summary>
public sealed class SyncReversePullEspelhoAtualizadoTests : IDisposable
{
    private readonly EasyStockDbContext _db;
    private readonly SyncReversePullService _servico;
    private readonly Guid _empresaId = Guid.NewGuid();

    public SyncReversePullEspelhoAtualizadoTests()
    {
        var currentUser = Substitute.For<ICurrentUserAccessor>();
        currentUser.IsAuthenticated.Returns(true);
        currentUser.EmpresaId.Returns(_empresaId);
        _db = new EasyStockDbContext(
            new DbContextOptionsBuilder<EasyStockDbContext>()
                .UseInMemoryDatabase($"reverse-pull-espelho-{Guid.NewGuid()}")
                .Options,
            currentUser);
        _servico = new SyncReversePullService(
            _db, new ConfigurationBuilder().Build(), NullLogger<SyncReversePullService>.Instance,
            new NumeroCurtoDosLotesMobile(_db, Substitute.For<EasyStock.Application.Ports.Output.Persistence.ILoteNumeroCurtoRepository>(), NullLogger<NumeroCurtoDosLotesMobile>.Instance));
    }

    private Guid CriarCategoria()
    {
        var categoria = new Categoria { Id = Guid.NewGuid(), EmpresaId = _empresaId, Nome = "Massas" };
        _db.Add(categoria);
        return categoria.Id;
    }

    private Produto ProdutoErpComEspelho(string nome, DateTime alteradoEm, StatusProduto status = StatusProduto.Ativo)
    {
        var produto = new Produto
        {
            Id = Guid.NewGuid(),
            EmpresaId = _empresaId,
            Nome = nome,
            Tipo = TipoProduto.Alimento,
            Status = status,
            CategoriaId = CriarCategoria(),
            CriadoEm = DateTime.UtcNow.AddDays(-10),
            AlteradoEm = alteradoEm,
        };
        _db.Add(produto);
        _db.Add(new Product
        {
            Id = "mob-" + produto.Id,
            Name = "Nome antigo",
            Category = "extra",
            Stock = 7,
            EmpresaId = _empresaId,
            ErpProductId = produto.Id,
        });
        return produto;
    }

    private void ItemEstoqueDoErp(Guid produtoId, Guid? lojaId, decimal quantidade) => _db.Add(new ItemEstoque
    {
        Id = Guid.NewGuid(),
        EmpresaId = _empresaId,
        ProdutoId = produtoId,
        LojaId = lojaId,
        QuantidadeInicial = Quantidade.From(quantidade),
        QuantidadeAtual = Quantidade.From(quantidade),
        CustoUnitario = Dinheiro.FromDecimal(1m),
    });

    [Fact]
    public async Task ProdutoErpAlterado_ComEspelho_EmiteUpsertComIdDoEspelhoEEstoqueDele()
    {
        var produto = ProdutoErpComEspelho("Nome novo do console", DateTime.UtcNow);
        ItemEstoqueDoErp(produto.Id, null, 7m);
        await _db.SaveChangesAsync();

        var mutations = new List<MutationDto>();
        await _servico.AppendAsync(mutations, DateTime.UtcNow.AddDays(-1), _empresaId, null);

        var upsert = mutations.Should().ContainSingle(m => m.Type == "product.upsert").Subject;
        upsert.Payload.GetProperty("id").GetString().Should().StartWith("mob-");
        upsert.Payload.GetProperty("name").GetString().Should().Be("Nome novo do console");
        upsert.Payload.GetProperty("stock").GetInt32().Should().Be(7);
    }

    [Fact]
    public async Task ProdutoErpNaoAlterado_ComEspelho_NaoEmiteUpsert()
    {
        ProdutoErpComEspelho("Nome antigo", DateTime.UtcNow.AddDays(-5));
        await _db.SaveChangesAsync();

        var mutations = new List<MutationDto>();
        await _servico.AppendAsync(mutations, DateTime.UtcNow.AddDays(-1), _empresaId, null);

        mutations.Should().NotContain(m => m.Type == "product.upsert");
    }

    [Fact]
    public async Task ProdutoErpAlterado_EstoqueVemDosItensDaLojaDoDevice()
    {
        var lojaDevice = Guid.NewGuid();
        var produto = ProdutoErpComEspelho("Nhoque", DateTime.UtcNow);
        ItemEstoqueDoErp(produto.Id, lojaDevice, 4m);
        ItemEstoqueDoErp(produto.Id, Guid.NewGuid(), 9m);
        await _db.SaveChangesAsync();

        var mutations = new List<MutationDto>();
        await _servico.AppendAsync(mutations, DateTime.UtcNow.AddDays(-1), _empresaId, lojaDevice);

        mutations.Single(m => m.Type == "product.upsert").Payload.GetProperty("stock").GetInt32().Should().Be(4);
    }

    public void Dispose() => _db.Dispose();
}
