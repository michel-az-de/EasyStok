using EasyStock.Domain.Entities;
using EasyStock.Domain.Enums;
using EasyStock.Domain.ValueObjects;
using EasyStock.Infra.Postgre.Repositories;
using FluentAssertions;

namespace EasyStock.Infra.Postgre.IntegrationTests.Repositories;

public class ProdutoRepositoryIntegrationTests(PostgreSqlDatabaseFixture fixture) : IClassFixture<PostgreSqlDatabaseFixture>
{
    [Fact]
    public async Task SearchAsync_deve_buscar_por_nome_sku_e_marca_respeitando_empresa()
    {
        if (!fixture.IsAvailable) return;
        await fixture.ResetDatabaseAsync();

        await using var context = fixture.CreateDbContext();
        var empresaA = new Empresa { Id = Guid.NewGuid(), Nome = "Empresa A", Documento = "111", CriadoEm = DateTime.UtcNow, AlteradoEm = DateTime.UtcNow };
        var empresaB = new Empresa { Id = Guid.NewGuid(), Nome = "Empresa B", Documento = "222", CriadoEm = DateTime.UtcNow, AlteradoEm = DateTime.UtcNow };
        var categoriaA = new Categoria { Id = Guid.NewGuid(), EmpresaId = empresaA.Id, Nome = "Audio", CriadoEm = DateTime.UtcNow, AlteradoEm = DateTime.UtcNow };
        var categoriaB = new Categoria { Id = Guid.NewGuid(), EmpresaId = empresaB.Id, Nome = "Audio", CriadoEm = DateTime.UtcNow, AlteradoEm = DateTime.UtcNow };

        context.Empresas.AddRange(empresaA, empresaB);
        context.Categorias.AddRange(categoriaA, categoriaB);
        context.Produtos.AddRange(
            new Produto
            {
                Id = Guid.NewGuid(),
                EmpresaId = empresaA.Id,
                CategoriaId = categoriaA.Id,
                Nome = "Galaxy Buds FE",
                Marca = "Samsung",
                Tipo = TipoProduto.Fisico,
                SkuBase = CodigoSku.From("BUDS-FE"),
                CodigoBarras = "7890000000001",
                Status = StatusProduto.Ativo,
                CriadoEm = DateTime.UtcNow,
                AlteradoEm = DateTime.UtcNow
            },
            new Produto
            {
                Id = Guid.NewGuid(),
                EmpresaId = empresaB.Id,
                CategoriaId = categoriaB.Id,
                Nome = "Galaxy Buds FE",
                Marca = "Samsung",
                Tipo = TipoProduto.Fisico,
                SkuBase = CodigoSku.From("BUDS-FE-OUTRA"),
                Status = StatusProduto.Ativo,
                CriadoEm = DateTime.UtcNow,
                AlteradoEm = DateTime.UtcNow
            });

        await context.SaveChangesAsync();

        var repository = new ProdutoRepository(context);

        var porNome = await repository.SearchAsync(empresaA.Id, "buds");
        var porSku = await repository.SearchAsync(empresaA.Id, "BUDS-FE");
        var porMarca = await repository.SearchAsync(empresaA.Id, "samsung");

        porNome.Should().ContainSingle();
        porSku.Should().ContainSingle();
        porMarca.Should().ContainSingle();
        porNome.Single().EmpresaId.Should().Be(empresaA.Id);
    }

    [Fact]
    public async Task Deve_persistir_value_objects_do_produto()
    {
        if (!fixture.IsAvailable) return;
        await fixture.ResetDatabaseAsync();

        var produtoId = Guid.NewGuid();
        var empresaId = Guid.NewGuid();
        var categoriaId = Guid.NewGuid();

        await using (var context = fixture.CreateDbContext())
        {
            context.Empresas.Add(new Empresa { Id = empresaId, Nome = "Empresa A", Documento = "333", CriadoEm = DateTime.UtcNow, AlteradoEm = DateTime.UtcNow });
            context.Categorias.Add(new Categoria { Id = categoriaId, EmpresaId = empresaId, Nome = "Audio", CriadoEm = DateTime.UtcNow, AlteradoEm = DateTime.UtcNow });
            context.Produtos.Add(new Produto
            {
                Id = produtoId,
                EmpresaId = empresaId,
                CategoriaId = categoriaId,
                Nome = "Galaxy Buds FE",
                Tipo = TipoProduto.Fisico,
                SkuBase = CodigoSku.From("BUDS-FE"),
                Dimensoes = Dimensoes.From(0.3m, 10.5m, 5.2m, 8.1m),
                CustoReferencia = Dinheiro.FromDecimal(250m),
                PrecoReferencia = Dinheiro.FromDecimal(399.90m),
                Status = StatusProduto.Ativo,
                CriadoEm = DateTime.UtcNow,
                AlteradoEm = DateTime.UtcNow
            });
            await context.SaveChangesAsync();
        }

        await using (var context = fixture.CreateDbContext())
        {
            var repository = new ProdutoRepository(context);
            var produto = await repository.GetByIdAsync(produtoId);

            produto.Should().NotBeNull();
            produto!.SkuBase!.Value.Should().Be("BUDS-FE");
            produto.Dimensoes.Should().NotBeNull();
            produto.Dimensoes!.Peso.Should().Be(0.3m);
            produto.CustoReferencia!.Valor.Should().Be(250m);
            produto.PrecoReferencia!.Valor.Should().Be(399.90m);
        }
    }
}
