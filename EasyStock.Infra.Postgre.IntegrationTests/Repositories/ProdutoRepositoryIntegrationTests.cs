using EasyStock.Domain.Entities;
using EasyStock.Domain.Enums;
using EasyStock.Domain.ValueObjects;
using EasyStock.Infra.Postgre.Repositories;
using FluentAssertions;

namespace EasyStock.Infra.Postgre.IntegrationTests.Repositories;

public class ProdutoRepositoryIntegrationTests(PostgreSqlDatabaseFixture fixture) : IClassFixture<PostgreSqlDatabaseFixture>
{
    [SkippableFact]
    public async Task SearchAsync_deve_buscar_por_nome_sku_e_marca_respeitando_empresa()
    {
        Skip.If(!fixture.IsAvailable, fixture.UnavailableReason ?? "Docker/PostgreSQL unavailable");
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

        context.SetMobileTenantContext(empresaA.Id);
        var repository = new ProdutoRepository(context);

        var porNome = await repository.SearchAsync(empresaA.Id, "buds");
        var porSku = await repository.SearchAsync(empresaA.Id, "BUDS-FE");
        var porMarca = await repository.SearchAsync(empresaA.Id, "samsung");

        porNome.Should().ContainSingle();
        porSku.Should().ContainSingle();
        porMarca.Should().ContainSingle();
        porNome.Single().EmpresaId.Should().Be(empresaA.Id);
    }

    [SkippableFact]
    public async Task Deve_persistir_value_objects_do_produto()
    {
        Skip.If(!fixture.IsAvailable, fixture.UnavailableReason ?? "Docker/PostgreSQL unavailable");
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
            context.SetMobileTenantContext(empresaId);
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

    [SkippableFact]
    public async Task GetProdutosPaginadosAsync_deve_respeitar_tenant_por_empresaId()
    {
        Skip.If(!fixture.IsAvailable, fixture.UnavailableReason ?? "Docker/PostgreSQL unavailable");
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
                Nome = "Produto A1",
                Tipo = TipoProduto.Fisico,
                Status = StatusProduto.Ativo,
                CriadoEm = DateTime.UtcNow,
                AlteradoEm = DateTime.UtcNow
            },
            new Produto
            {
                Id = Guid.NewGuid(),
                EmpresaId = empresaA.Id,
                CategoriaId = categoriaA.Id,
                Nome = "Produto A2",
                Tipo = TipoProduto.Fisico,
                Status = StatusProduto.Ativo,
                CriadoEm = DateTime.UtcNow,
                AlteradoEm = DateTime.UtcNow
            },
            new Produto
            {
                Id = Guid.NewGuid(),
                EmpresaId = empresaB.Id,
                CategoriaId = categoriaB.Id,
                Nome = "Produto B1",
                Tipo = TipoProduto.Fisico,
                Status = StatusProduto.Ativo,
                CriadoEm = DateTime.UtcNow,
                AlteradoEm = DateTime.UtcNow
            });

        await context.SaveChangesAsync();

        var repository = new ProdutoRepository(context);

        context.SetMobileTenantContext(empresaA.Id);
        var (produtosA, totalA) = await repository.GetProdutosPaginadosAsync(empresaA.Id, 1, 10);
        context.SetMobileTenantContext(empresaB.Id);
        var (produtosB, totalB) = await repository.GetProdutosPaginadosAsync(empresaB.Id, 1, 10);

        produtosA.Should().HaveCount(2);
        produtosA.Should().AllSatisfy(p => p.EmpresaId.Should().Be(empresaA.Id));
        totalA.Should().Be(2);

        produtosB.Should().HaveCount(1);
        produtosB.Should().AllSatisfy(p => p.EmpresaId.Should().Be(empresaB.Id));
        totalB.Should().Be(1);
    }

    [SkippableFact] // BUG-10 (QA v1.10 #674, refs #561): dedup de nome e case-insensitive ("Vassoura"=="vassoura").
    public async Task ExistsNomeAsync_deve_ser_case_insensitive()
    {
        Skip.If(!fixture.IsAvailable, fixture.UnavailableReason ?? "Docker/PostgreSQL unavailable");
        await fixture.ResetDatabaseAsync();

        await using var context = fixture.CreateDbContext();
        var empresa = new Empresa { Id = Guid.NewGuid(), Nome = "Empresa", Documento = "111", CriadoEm = DateTime.UtcNow, AlteradoEm = DateTime.UtcNow };
        var categoria = new Categoria { Id = Guid.NewGuid(), EmpresaId = empresa.Id, Nome = "Limpeza", CriadoEm = DateTime.UtcNow, AlteradoEm = DateTime.UtcNow };
        context.Empresas.Add(empresa);
        context.Categorias.Add(categoria);
        context.Produtos.Add(new Produto
        {
            Id = Guid.NewGuid(), EmpresaId = empresa.Id, CategoriaId = categoria.Id,
            Nome = "Vassoura", Tipo = TipoProduto.Fisico, Status = StatusProduto.Ativo,
            CriadoEm = DateTime.UtcNow, AlteradoEm = DateTime.UtcNow
        });
        await context.SaveChangesAsync();
        context.SetMobileTenantContext(empresa.Id);
        var repository = new ProdutoRepository(context);

        (await repository.ExistsNomeAsync(empresa.Id, "vassoura")).Should().BeTrue("dedup deve ignorar caixa (minuscula)");
        (await repository.ExistsNomeAsync(empresa.Id, "VASSOURA")).Should().BeTrue("dedup deve ignorar caixa (maiuscula)");
        (await repository.ExistsNomeAsync(empresa.Id, "  Vassoura  ")).Should().BeTrue("dedup deve ignorar espacos");
        (await repository.ExistsNomeAsync(empresa.Id, "Rodo")).Should().BeFalse("nome distinto nao colide");
    }
}
