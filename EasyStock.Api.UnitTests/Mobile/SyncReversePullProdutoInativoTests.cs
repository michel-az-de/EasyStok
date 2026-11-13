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

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task ProdutoInativado_ComEspelhos_RespeitaEmpresaELoja(bool filtrarLoja)
    {
        var lojaA = Guid.NewGuid();
        var lojaB = Guid.NewGuid();
        var produto = Produto("Descontinuado", StatusProduto.Inativo, DateTime.UtcNow, comEspelho: false);
        foreach (var (id, empresa, loja) in new (string, Guid, Guid?)[]
        {
            ("inativo-a", _empresaId, lojaA),
            ("inativo-b", _empresaId, lojaB),
            ("inativo-global", _empresaId, null),
            ("inativo-outra-empresa", Guid.NewGuid(), lojaA)
        })
            _db.Add(new Product { Id = id, Name = "Descontinuado", EmpresaId = empresa, LojaId = loja, ErpProductId = produto.Id });
        await _db.SaveChangesAsync();

        var mutations = new List<MutationDto>();
        await _servico.AppendAsync(mutations, DateTime.UtcNow.AddDays(-1), _empresaId, filtrarLoja ? lojaA : null);

        mutations.Should().OnlyContain(m => m.Type == "product.delete");
        var esperados = filtrarLoja
            ? new[] { "inativo-a", "inativo-global" }
            : new[] { "inativo-a", "inativo-b", "inativo-global" };
        mutations.Select(m => m.Payload.GetProperty("id").GetString()).Should().BeEquivalentTo(esperados);
    }

    [Fact]
    public async Task ProdutoReativado_ComEspelho_VoltaComoUpsertENaoComoDelete()
    {
        var produto = Produto("Voltou", StatusProduto.Ativo, DateTime.UtcNow, comEspelho: true);
        await _db.SaveChangesAsync();

        var mutations = new List<MutationDto>();
        await _servico.AppendAsync(mutations, DateTime.UtcNow.AddDays(-1), _empresaId, null);

        mutations.Should().NotContain(m => m.Type == "product.delete");
        mutations.Should().ContainSingle(m => m.Type == "product.upsert")
            .Which.Payload.GetProperty("id").GetString().Should().Be("mob-" + produto.Id);
    }

    [Fact]
    public async Task ProdutoInativoDeOutraEmpresa_NaoGeraEvento()
    {
        var outraEmpresa = Guid.NewGuid();
        _db.Add(new Produto
        {
            Id = Guid.NewGuid(), EmpresaId = outraEmpresa, Nome = "De outro", Tipo = TipoProduto.Alimento,
            Status = StatusProduto.Inativo, CriadoEm = DateTime.UtcNow.AddDays(-10), AlteradoEm = DateTime.UtcNow,
        });
        await _db.SaveChangesAsync();

        var mutations = new List<MutationDto>();
        await _servico.AppendAsync(mutations, DateTime.UtcNow.AddDays(-1), _empresaId, null);

        mutations.Should().BeEmpty();
    }

    [Fact]
    public async Task CursorZerado_NaoReemiteDeleteDosInativosHistoricos()
    {
        Produto("Historico", StatusProduto.Inativo, DateTime.UtcNow.AddDays(-30), comEspelho: true);
        await _db.SaveChangesAsync();

        var mutations = new List<MutationDto>();
        await _servico.AppendAsync(mutations, DateTime.UnixEpoch, _empresaId, null);

        mutations.Should().NotContain(m => m.Type == "product.delete");
    }
}
