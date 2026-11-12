using EasyStock.Api.Mobile.Controllers;
using EasyStock.Api.Mobile.DTOs;
using EasyStock.Api.Mobile.Security;
using EasyStock.Application.Ports.Output;
using EasyStock.Application.Ports.Output.Persistence;
using EasyStock.Domain.Entities;
using EasyStock.Domain.Entities.Mobile;
using EasyStock.Domain.Enums;
using EasyStock.Infra.Postgre.Data;
using FluentAssertions;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging.Abstractions;
using NSubstitute;

namespace EasyStock.Api.UnitTests.Mobile;

/// <summary>
/// SYNC-3 (#2316), ressurreicao: o aparelho A recebe o delete do produto inativado; o aparelho B
/// vende ou ajusta o item e toca o espelho (mobile_products). O pull de A nao pode devolver o
/// espelho como product.upsert, senao o produto volta ao catalogo.
/// </summary>
public sealed class SyncPullProdutoInativoTests : IDisposable
{
    private readonly EasyStockDbContext _db;
    private readonly Guid _empresaId = Guid.NewGuid();

    public SyncPullProdutoInativoTests()
    {
        var currentUser = Substitute.For<ICurrentUserAccessor>();
        currentUser.IsAuthenticated.Returns(true);
        currentUser.EmpresaId.Returns(_empresaId);
        _db = new EasyStockDbContext(
            new DbContextOptionsBuilder<EasyStockDbContext>()
                .UseInMemoryDatabase($"sync-pull-inativo-{Guid.NewGuid()}")
                .Options,
            currentUser);
    }

    public void Dispose() => _db.Dispose();

    private async Task<List<MutationDto>> PullDoAparelhoAAsync()
    {
        var http = new DefaultHttpContext();
        http.Items[MobileAuth.HttpContextItemDevice] = new MobileDevice
        {
            Id = "aparelho-a", ApiKeyHash = "h", EmpresaId = _empresaId, LojaId = Guid.Empty
        };
        var controller = new SyncController(
            _db, null!, null!, null!, null!,
            Substitute.For<IProdutoRepository>(),
            new ConfigurationBuilder().Build(),
            TimeProvider.System,
            new EasyStock.Api.Mobile.Services.NumeroCurtoDosLotesMobile(_db, Substitute.For<ILoteNumeroCurtoRepository>(), NullLogger<EasyStock.Api.Mobile.Services.NumeroCurtoDosLotesMobile>.Instance),
            NullLogger<SyncController>.Instance)
        {
            ControllerContext = new ControllerContext { HttpContext = http }
        };
        var resultado = await controller.Pull(DateTimeOffset.UtcNow.AddDays(-1).ToUnixTimeMilliseconds(), "aparelho-a");
        return ((resultado.Result as OkObjectResult)!.Value as SyncPullResponse)!.Mutations.ToList();
    }

    private async Task<Produto> ProdutoComEspelhoTocadoPeloAparelhoB(StatusProduto status, Guid? empresaDoProduto = null)
    {
        var produto = new Produto
        {
            Id = Guid.NewGuid(), EmpresaId = empresaDoProduto ?? _empresaId, Nome = "Talharim", Tipo = TipoProduto.Alimento,
            Status = status, CriadoEm = DateTime.UtcNow.AddDays(-10), AlteradoEm = DateTime.UtcNow.AddHours(-2),
        };
        _db.Add(produto);
        _db.Add(new Product
        {
            Id = "mob-" + produto.Id, Name = "Talharim", EmpresaId = _empresaId, ErpProductId = produto.Id,
            LastDeviceId = "aparelho-b", UpdatedAt = DateTime.UtcNow.AddMinutes(-5),
        });
        await _db.SaveChangesAsync();
        _db.ChangeTracker.Clear();
        return produto;
    }

    [Fact]
    public async Task EspelhoDeProdutoInativo_TocadoPorOutroAparelho_NaoSaiComoUpsert()
    {
        await ProdutoComEspelhoTocadoPeloAparelhoB(StatusProduto.Inativo);

        var mutations = await PullDoAparelhoAAsync();

        mutations.Should().NotContain(m => m.Type == "product.upsert");
    }

    [Fact]
    public async Task EspelhoDeProdutoReativado_TocadoPorOutroAparelho_SaiComoUpsert()
    {
        await ProdutoComEspelhoTocadoPeloAparelhoB(StatusProduto.Ativo);

        var mutations = await PullDoAparelhoAAsync();

        mutations.Should().ContainSingle(m => m.Type == "product.upsert");
    }
}
