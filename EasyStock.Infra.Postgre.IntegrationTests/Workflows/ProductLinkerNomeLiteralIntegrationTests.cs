using EasyStock.Api.Mobile.Services;
using EasyStock.Api.Mobile.Services.Linkers;
using EasyStock.Domain.Entities;
using EasyStock.Domain.Entities.Mobile;
using EasyStock.Domain.Enums;
using EasyStock.Infra.Postgre.Repositories;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;

namespace EasyStock.Infra.Postgre.IntegrationTests.Workflows;

/// <summary>
/// #2477: o vínculo do produto do app com o do ERP compara o nome sem diferenciar maiúsculas. "%" e "_" do nome
/// são texto: sem escape, "Bolo_" casava com "Bolos" e o espelho passava a seguir preço e estoque do produto errado.
/// </summary>
public class ProductLinkerNomeLiteralIntegrationTests(PostgreSqlDatabaseFixture fixture)
    : IClassFixture<PostgreSqlDatabaseFixture>
{
    [SkippableTheory]
    [InlineData("Bolo_", "Bolos")]
    [InlineData("Kit%", "Kit 50%")]
    [InlineData("Pão\\", "Pão")]
    public async Task NomeComCuringa_NaoVinculaAOutroProduto(string nomeNoApp, string nomeNoErp)
    {
        Skip.If(!fixture.IsAvailable, fixture.UnavailableReason ?? "Docker/PostgreSQL unavailable");
        await fixture.ResetDatabaseAsync();
        var (empresaId, erpId) = await SeedAsync(nomeNoErp);

        var vinculado = await VincularAsync(empresaId, nomeNoApp);

        vinculado.Should().NotBeNull("o produto do app vira um produto novo no ERP")
            .And.NotBe(erpId, $"\"{nomeNoApp}\" não é \"{nomeNoErp}\"");
    }

    [SkippableTheory]
    [InlineData("bolo_", "Bolo_")]
    [InlineData("KIT 50%", "Kit 50%")]
    public async Task MesmoNomeSemDiferenciarMaiusculas_Vincula(string nomeNoApp, string nomeNoErp)
    {
        Skip.If(!fixture.IsAvailable, fixture.UnavailableReason ?? "Docker/PostgreSQL unavailable");
        await fixture.ResetDatabaseAsync();
        var (empresaId, erpId) = await SeedAsync(nomeNoErp);

        (await VincularAsync(empresaId, nomeNoApp)).Should().Be(erpId);
    }

    private async Task<Guid?> VincularAsync(Guid empresaId, string nomeNoApp)
    {
        await using (var seed = fixture.CreateDbContext())
        {
            seed.SetMobileTenantContext(empresaId);
            seed.Set<Product>().Add(new Product { Id = "p-app", Name = nomeNoApp, Category = "doces", EmpresaId = empresaId });
            await seed.SaveChangesAsync();
        }

        await using (var db = fixture.CreateDbContext())
        {
            db.SetMobileTenantContext(empresaId);
            var linker = new ProductLinker(db, new MobileSystemUserResolver(db), NullLogger<ProductLinker>.Instance,
                new ProdutoRepository(db));
            await linker.ExecuteAsync(["p-app"], empresaId);
            await db.SaveChangesAsync();
        }

        await using var leitura = fixture.CreateDbContext();
        leitura.SetMobileTenantContext(empresaId);
        return (await leitura.Set<Product>().SingleAsync(p => p.Id == "p-app")).ErpProductId;
    }

    private async Task<(Guid EmpresaId, Guid ErpId)> SeedAsync(string nomeNoErp)
    {
        var empresaId = Guid.NewGuid();
        var categoriaId = Guid.NewGuid();
        var erpId = Guid.NewGuid();
        await using var seed = fixture.CreateDbContext();
        seed.Add(new Empresa { Id = empresaId, Nome = "Empresa linker", Documento = $"{Random.Shared.Next(100000, 999999)}" });
        seed.Add(new Categoria { Id = categoriaId, EmpresaId = empresaId, Nome = "Doces" });
        seed.Add(new Produto
        {
            Id = erpId, EmpresaId = empresaId, CategoriaId = categoriaId, Nome = nomeNoErp, Status = StatusProduto.Ativo
        });
        await seed.SaveChangesAsync();
        return (empresaId, erpId);
    }
}
