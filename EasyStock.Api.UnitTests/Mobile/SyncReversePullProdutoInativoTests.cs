using EasyStock.Api.Mobile.DTOs;
using EasyStock.Api.Mobile.Services;
using EasyStock.Application.Ports.Output;
using EasyStock.Domain.Entities;
using EasyStock.Domain.Entities.Mobile;
using EasyStock.Domain.Enums;
using EasyStock.Infra.Postgre.Data;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging.Abstractions;
using NSubstitute;

namespace EasyStock.Api.UnitTests.Mobile;

/// <summary>
/// SYNC-3 (#2316): produto inativado no console continuava no app porque o pull só
/// mandava upsert dos ativos e nunca a remoção.
/// </summary>
public sealed class SyncReversePullProdutoInativoTests : IDisposable
{
    private readonly EasyStockDbContext _db;
    private readonly SyncReversePullService _servico;
    private readonly Guid _empresaId = Guid.NewGuid();

    public SyncReversePullProdutoInativoTests()
    {
        var currentUser = Substitute.For<ICurrentUserAccessor>();
        currentUser.IsAuthenticated.Returns(true);
        currentUser.EmpresaId.Returns(_empresaId);
        _db = new EasyStockDbContext(
            new DbContextOptionsBuilder<EasyStockDbContext>()
                .UseInMemoryDatabase($"reverse-pull-inativo-{Guid.NewGuid()}")
                .Options,
            currentUser);
        _servico = new SyncReversePullService(
            _db, new ConfigurationBuilder().Build(), NullLogger<SyncReversePullService>.Instance,
            new NumeroCurtoDosLotesMobile(_db, Substitute.For<EasyStock.Application.Ports.Output.Persistence.ILoteNumeroCurtoRepository>(), NullLogger<NumeroCurtoDosLotesMobile>.Instance));
    }

    public void Dispose() => _db.Dispose();

    private Produto Produto(string nome, StatusProduto status, DateTime alteradoEm, bool comEspelho)
    {
        var categoria = new Categoria { Id = Guid.NewGuid(), EmpresaId = _empresaId, Nome = "Massas" };
        _db.Add(categoria);
        var produto = new Produto
        {
            Id = Guid.NewGuid(), EmpresaId = _empresaId, Nome = nome, Tipo = TipoProduto.Alimento,
            Status = status, CategoriaId = categoria.Id,
            CriadoEm = DateTime.UtcNow.AddDays(-10), AlteradoEm = alteradoEm,
        };
        _db.Add(produto);
        if (comEspelho)
            _db.Add(new Product { Id = "mob-" + produto.Id, Name = nome, Category = "extra", EmpresaId = _empresaId, ErpProductId = produto.Id });
        return produto;
    }

    [Fact]
    public async Task ProdutoInativado_SemEspelho_EmiteDeleteComIdDoErp()
    {
        var produto = Produto("Descontinuado", StatusProduto.Inativo, DateTime.UtcNow, comEspelho: false);
        await _db.SaveChangesAsync();

        var mutations = new List<MutationDto>();
        await _servico.AppendAsync(mutations, DateTime.UtcNow.AddDays(-1), _empresaId, null);

        mutations.Should().NotContain(m => m.Type == "product.upsert");
        mutations.Should().ContainSingle(m => m.Type == "product.delete")
            .Which.Payload.GetProperty("id").GetString().Should().Be(produto.Id.ToString());
    }

    [Fact]
    public async Task ProdutoInativado_ComEspelho_EmiteDeleteComIdDoEspelho()
    {
        var produto = Produto("Descontinuado", StatusProduto.Inativo, DateTime.UtcNow, comEspelho: true);
        await _db.SaveChangesAsync();

        var mutations = new List<MutationDto>();
        await _servico.AppendAsync(mutations, DateTime.UtcNow.AddDays(-1), _empresaId, null);

        mutations.Should().ContainSingle(m => m.Type == "product.delete")
            .Which.Payload.GetProperty("id").GetString().Should().Be("mob-" + produto.Id);
    }

    [Fact]
    public async Task ProdutoInativoAntesDoCursor_NaoEmiteDelete()
    {
        Produto("Velho", StatusProduto.Inativo, DateTime.UtcNow.AddDays(-5), comEspelho: false);
        await _db.SaveChangesAsync();

        var mutations = new List<MutationDto>();
        await _servico.AppendAsync(mutations, DateTime.UtcNow.AddDays(-1), _empresaId, null);

        mutations.Should().NotContain(m => m.Type == "product.delete");
    }
}
