using EasyStock.Api.Mobile.DTOs;
using EasyStock.Api.Mobile.Services;
using EasyStock.Application.Ports.Output;
using EasyStock.Domain.Entities;
using EasyStock.Domain.Enums;
using EasyStock.Infra.Postgre.Data;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging.Abstractions;
using NSubstitute;

namespace EasyStock.Api.UnitTests.Mobile;

/// <summary>
/// Regressao #1469: produto criado no ERP chegava ao PWA com categoria "Geral" e sumia do
/// grid de producao, que so desenha massa/molho/extra.
/// </summary>
public sealed class SyncReversePullCategoriaTests : IDisposable
{
    private readonly EasyStockDbContext _db;
    private readonly SyncReversePullService _servico;
    private readonly Guid _empresaId = Guid.NewGuid();

    public SyncReversePullCategoriaTests()
    {
        var currentUser = Substitute.For<ICurrentUserAccessor>();
        currentUser.IsAuthenticated.Returns(true);
        currentUser.EmpresaId.Returns(_empresaId);
        _db = new EasyStockDbContext(
            new DbContextOptionsBuilder<EasyStockDbContext>()
                .UseInMemoryDatabase($"reverse-pull-{Guid.NewGuid()}")
                .Options,
            currentUser);
        _servico = new SyncReversePullService(
            _db, new ConfigurationBuilder().Build(), NullLogger<SyncReversePullService>.Instance);
    }

    private void Produto(string nome, string nomeCategoria)
    {
        var categoria = new Categoria { Id = Guid.NewGuid(), EmpresaId = _empresaId, Nome = nomeCategoria };
        _db.Add(categoria);
        _db.Add(new Produto
        {
            Id = Guid.NewGuid(),
            EmpresaId = _empresaId,
            Nome = nome,
            Tipo = TipoProduto.Alimento,
            Status = StatusProduto.Ativo,
            CategoriaId = categoria.Id,
            CriadoEm = DateTime.UtcNow,
            AlteradoEm = DateTime.UtcNow
        });
    }

    [Theory]
    [InlineData("Massas Frescas", "massa")]
    [InlineData("MOLHOS", "molho")]
    [InlineData("Bebidas", "extra")]
    [InlineData("Geral", "extra")]
    public async Task ProdutoDoErp_ChegaComCategoriaQueOGridDesenha(string categoriaErp, string esperada)
    {
        Produto("Item do console", categoriaErp);
        await _db.SaveChangesAsync();

        var mutations = new List<MutationDto>();
        await _servico.AppendAsync(mutations, DateTime.UtcNow.AddDays(-1), _empresaId, null);

        mutations.Where(m => m.Type == "product.upsert")
            .Should().ContainSingle()
            .Which.Payload.GetProperty("category").GetString().Should().Be(esperada);
    }

    public void Dispose() => _db.Dispose();
}
