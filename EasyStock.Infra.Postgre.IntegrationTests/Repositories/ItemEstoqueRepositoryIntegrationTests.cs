using EasyStock.Domain.Entities;
using EasyStock.Domain.Enums;
using EasyStock.Domain.ValueObjects;
using EasyStock.Infra.Postgre.Repositories;
using FluentAssertions;

namespace EasyStock.Infra.Postgre.IntegrationTests.Repositories;

public class ItemEstoqueRepositoryIntegrationTests(PostgreSqlDatabaseFixture fixture) : IClassFixture<PostgreSqlDatabaseFixture>
{
    [SkippableFact]
    public async Task SearchAsync_deve_buscar_item_por_codigo_chave_variacao_e_descricao()
    {
        Skip.If(!fixture.IsAvailable, fixture.UnavailableReason ?? "Docker/PostgreSQL unavailable");
        await fixture.ResetDatabaseAsync();

        await using var context = fixture.CreateDbContext();
        var empresaId = Guid.NewGuid();
        var categoriaId = Guid.NewGuid();
        var produtoId = Guid.NewGuid();

        context.Empresas.Add(new Empresa { Id = empresaId, Nome = "Empresa A", Documento = "555", CriadoEm = DateTime.UtcNow, AlteradoEm = DateTime.UtcNow });
        context.Categorias.Add(new Categoria { Id = categoriaId, EmpresaId = empresaId, Nome = "Audio", CriadoEm = DateTime.UtcNow, AlteradoEm = DateTime.UtcNow });
        context.Produtos.Add(new Produto
        {
            Id = produtoId,
            EmpresaId = empresaId,
            CategoriaId = categoriaId,
            Nome = "Galaxy Buds FE",
            Tipo = TipoProduto.Fisico,
            Status = StatusProduto.Ativo,
            CriadoEm = DateTime.UtcNow,
            AlteradoEm = DateTime.UtcNow
        });
        context.ItensEstoque.Add(new ItemEstoque
        {
            Id = Guid.NewGuid(),
            EmpresaId = empresaId,
            ProdutoId = produtoId,
            CodigoInterno = "CAP3426",
            CodigoMarketplace = "ML-ABC",
            ChavePesquisa = "CAP3426 BUDS-FE GALAXY BUDS FE",
            VariacaoDescricao = "Grafite",
            Cor = "Grafite",
            Tamanho = "Unico",
            DescricaoAnuncio = "Fone bluetooth buds fe grafite",
            QuantidadeInicial = Quantidade.From(10),
            QuantidadeAtual = Quantidade.From(10),
            CustoUnitario = Dinheiro.FromDecimal(250m),
            Status = StatusItemEstoque.Ok,
            EntradaEm = DateTime.UtcNow,
            CriadoEm = DateTime.UtcNow,
            AlteradoEm = DateTime.UtcNow
        });

        await context.SaveChangesAsync();

        var repository = new ItemEstoqueRepository(context);

        (await repository.SearchAsync(empresaId, "CAP3426")).Should().ContainSingle();
        (await repository.SearchAsync(empresaId, "ML-ABC")).Should().ContainSingle();
        (await repository.SearchAsync(empresaId, "grafite")).Should().ContainSingle();
        (await repository.SearchAsync(empresaId, "bluetooth")).Should().ContainSingle();
    }

    [SkippableFact]
    public async Task SearchAsync_deve_retornar_ordem_deterministica_em_chamadas_consecutivas()
    {
        Skip.If(!fixture.IsAvailable, fixture.UnavailableReason ?? "Docker/PostgreSQL unavailable");
        await fixture.ResetDatabaseAsync();

        await using var context = fixture.CreateDbContext();
        var empresaId = Guid.NewGuid();
        var categoriaId = Guid.NewGuid();
        var produtoId = Guid.NewGuid();

        context.Empresas.Add(new Empresa { Id = empresaId, Nome = "Empresa Paginacao", Documento = "777", CriadoEm = DateTime.UtcNow, AlteradoEm = DateTime.UtcNow });
        context.Categorias.Add(new Categoria { Id = categoriaId, EmpresaId = empresaId, Nome = "Eletronicos", CriadoEm = DateTime.UtcNow, AlteradoEm = DateTime.UtcNow });
        context.Produtos.Add(new Produto { Id = produtoId, EmpresaId = empresaId, CategoriaId = categoriaId, Nome = "Produto Test", Tipo = TipoProduto.Fisico, Status = StatusProduto.Ativo, CriadoEm = DateTime.UtcNow, AlteradoEm = DateTime.UtcNow });

        for (var i = 1; i <= 5; i++)
        {
            context.ItensEstoque.Add(new ItemEstoque
            {
                Id = Guid.NewGuid(),
                EmpresaId = empresaId,
                ProdutoId = produtoId,
                CodigoInterno = $"SKU-{i:D3}",
                ChavePesquisa = $"sku teste item {i:D3}",
                QuantidadeInicial = Quantidade.From(i),
                QuantidadeAtual = Quantidade.From(i),
                CustoUnitario = Dinheiro.FromDecimal(10m * i),
                Status = StatusItemEstoque.Ok,
                EntradaEm = DateTime.UtcNow,
                CriadoEm = DateTime.UtcNow,
                AlteradoEm = DateTime.UtcNow
            });
        }
        await context.SaveChangesAsync();

        var repository = new ItemEstoqueRepository(context);

        var primeira = (await repository.SearchAsync(empresaId, "sku teste", maxResults: 3)).Select(i => i.CodigoInterno).ToList();
        var segunda  = (await repository.SearchAsync(empresaId, "sku teste", maxResults: 3)).Select(i => i.CodigoInterno).ToList();

        primeira.Should().HaveCount(3);
        primeira.Should().Equal(segunda, "chamadas consecutivas devem retornar a mesma ordem determinística");
    }

    [SkippableFact]
    public async Task Deve_persistir_value_objects_do_item_estoque()
    {
        Skip.If(!fixture.IsAvailable, fixture.UnavailableReason ?? "Docker/PostgreSQL unavailable");
        await fixture.ResetDatabaseAsync();

        var empresaId = Guid.NewGuid();
        var categoriaId = Guid.NewGuid();
        var produtoId = Guid.NewGuid();
        var itemId = Guid.NewGuid();

        await using (var context = fixture.CreateDbContext())
        {
            context.Empresas.Add(new Empresa { Id = empresaId, Nome = "Empresa A", Documento = "666", CriadoEm = DateTime.UtcNow, AlteradoEm = DateTime.UtcNow });
            context.Categorias.Add(new Categoria { Id = categoriaId, EmpresaId = empresaId, Nome = "Audio", CriadoEm = DateTime.UtcNow, AlteradoEm = DateTime.UtcNow });
            context.Produtos.Add(new Produto
            {
                Id = produtoId,
                EmpresaId = empresaId,
                CategoriaId = categoriaId,
                Nome = "Galaxy Buds FE",
                Tipo = TipoProduto.Fisico,
                Status = StatusProduto.Ativo,
                CriadoEm = DateTime.UtcNow,
                AlteradoEm = DateTime.UtcNow
            });
            context.ItensEstoque.Add(new ItemEstoque
            {
                Id = itemId,
                EmpresaId = empresaId,
                ProdutoId = produtoId,
                CodigoLote = CodigoLote.From("LOTE-01"),
                DimensoesReais = Dimensoes.From(0.3m, 10.5m, 5.2m, 8.1m),
                QuantidadeInicial = Quantidade.From(12),
                QuantidadeAtual = Quantidade.From(7),
                CustoUnitario = Dinheiro.FromDecimal(250m),
                PrecoVendaSugerido = Dinheiro.FromDecimal(399.90m),
                ValidadeEm = Validade.From(new DateTime(2026, 12, 31)),
                Status = StatusItemEstoque.Ok,
                EntradaEm = DateTime.UtcNow,
                CriadoEm = DateTime.UtcNow,
                AlteradoEm = DateTime.UtcNow
            });
            await context.SaveChangesAsync();
        }

        await using (var context = fixture.CreateDbContext())
        {
            var repository = new ItemEstoqueRepository(context);
            var item = await repository.GetByIdAsync(itemId);

            item.Should().NotBeNull();
            item!.CodigoLote!.Value.Should().Be("LOTE-01");
            item.DimensoesReais!.Largura.Should().Be(10.5m);
            item.QuantidadeInicial.Value.Should().Be(12);
            item.QuantidadeAtual.Value.Should().Be(7);
            item.CustoUnitario.Valor.Should().Be(250m);
            item.PrecoVendaSugerido!.Valor.Should().Be(399.90m);
            item.ValidadeEm!.DataValidade.Should().Be(new DateTime(2026, 12, 31));
        }
    }

    [SkippableFact]
    public async Task GetItensEstoquePaginadosAsync_deve_respeitar_tenant_por_empresaId()
    {
        Skip.If(!fixture.IsAvailable, fixture.UnavailableReason ?? "Docker/PostgreSQL unavailable");
        await fixture.ResetDatabaseAsync();

        await using var context = fixture.CreateDbContext();
        var empresaA = new Empresa { Id = Guid.NewGuid(), Nome = "Empresa A", Documento = "111", CriadoEm = DateTime.UtcNow, AlteradoEm = DateTime.UtcNow };
        var empresaB = new Empresa { Id = Guid.NewGuid(), Nome = "Empresa B", Documento = "222", CriadoEm = DateTime.UtcNow, AlteradoEm = DateTime.UtcNow };
        var categoriaA = new Categoria { Id = Guid.NewGuid(), EmpresaId = empresaA.Id, Nome = "Audio", CriadoEm = DateTime.UtcNow, AlteradoEm = DateTime.UtcNow };
        var produtoA = new Produto
        {
            Id = Guid.NewGuid(),
            EmpresaId = empresaA.Id,
            CategoriaId = categoriaA.Id,
            Nome = "Produto A",
            Tipo = TipoProduto.Fisico,
            Status = StatusProduto.Ativo,
            CriadoEm = DateTime.UtcNow,
            AlteradoEm = DateTime.UtcNow
        };

        context.Empresas.AddRange(empresaA, empresaB);
        context.Categorias.Add(categoriaA);
        context.Produtos.Add(produtoA);
        context.ItensEstoque.AddRange(
            new ItemEstoque
            {
                Id = Guid.NewGuid(),
                EmpresaId = empresaA.Id,
                ProdutoId = produtoA.Id,
                QuantidadeAtual = Quantidade.From(10),
                Status = StatusItemEstoque.Ok,
                EntradaEm = DateTime.UtcNow,
                CriadoEm = DateTime.UtcNow,
                AlteradoEm = DateTime.UtcNow
            },
            new ItemEstoque
            {
                Id = Guid.NewGuid(),
                EmpresaId = empresaA.Id,
                ProdutoId = produtoA.Id,
                QuantidadeAtual = Quantidade.From(5),
                Status = StatusItemEstoque.Ok,
                EntradaEm = DateTime.UtcNow,
                CriadoEm = DateTime.UtcNow,
                AlteradoEm = DateTime.UtcNow
            },
            new ItemEstoque
            {
                Id = Guid.NewGuid(),
                EmpresaId = empresaB.Id,
                ProdutoId = Guid.NewGuid(), // Produto B, mas não importa
                QuantidadeAtual = Quantidade.From(20),
                Status = StatusItemEstoque.Ok,
                EntradaEm = DateTime.UtcNow,
                CriadoEm = DateTime.UtcNow,
                AlteradoEm = DateTime.UtcNow
            });

        await context.SaveChangesAsync();

        var repository = new ItemEstoqueRepository(context);

        var (itensA, totalA) = await repository.GetItensEstoquePaginadosAsync(empresaA.Id, 1, 10);
        var (itensB, totalB) = await repository.GetItensEstoquePaginadosAsync(empresaB.Id, 1, 10);

        itensA.Should().HaveCount(2);
        itensA.Should().AllSatisfy(i => i.EmpresaId.Should().Be(empresaA.Id));
        totalA.Should().Be(2);

        itensB.Should().HaveCount(1);
        itensB.Should().AllSatisfy(i => i.EmpresaId.Should().Be(empresaB.Id));
        totalB.Should().Be(1);
    }

    [SkippableFact]
    public async Task GetEstoqueBaixoAsync_deve_respeitar_tenant_e_filtros()
    {
        Skip.If(!fixture.IsAvailable, fixture.UnavailableReason ?? "Docker/PostgreSQL unavailable");
        await fixture.ResetDatabaseAsync();

        await using var context = fixture.CreateDbContext();
        var empresaA = new Empresa { Id = Guid.NewGuid(), Nome = "Empresa A", Documento = "111", CriadoEm = DateTime.UtcNow, AlteradoEm = DateTime.UtcNow };
        var empresaB = new Empresa { Id = Guid.NewGuid(), Nome = "Empresa B", Documento = "222", CriadoEm = DateTime.UtcNow, AlteradoEm = DateTime.UtcNow };
        var categoriaA = new Categoria { Id = Guid.NewGuid(), EmpresaId = empresaA.Id, Nome = "Audio", CriadoEm = DateTime.UtcNow, AlteradoEm = DateTime.UtcNow };
        var produtoA = new Produto
        {
            Id = Guid.NewGuid(),
            EmpresaId = empresaA.Id,
            CategoriaId = categoriaA.Id,
            Nome = "Produto A",
            Tipo = TipoProduto.Fisico,
            Status = StatusProduto.Ativo,
            CriadoEm = DateTime.UtcNow,
            AlteradoEm = DateTime.UtcNow
        };

        context.Empresas.AddRange(empresaA, empresaB);
        context.Categorias.Add(categoriaA);
        context.Produtos.Add(produtoA);
        context.ItensEstoque.AddRange(
            new ItemEstoque
            {
                Id = Guid.NewGuid(),
                EmpresaId = empresaA.Id,
                ProdutoId = produtoA.Id,
                QuantidadeAtual = Quantidade.From(3), // Baixo
                Status = StatusItemEstoque.Ok,
                EntradaEm = DateTime.UtcNow,
                CriadoEm = DateTime.UtcNow,
                AlteradoEm = DateTime.UtcNow
            },
            new ItemEstoque
            {
                Id = Guid.NewGuid(),
                EmpresaId = empresaA.Id,
                ProdutoId = produtoA.Id,
                QuantidadeAtual = Quantidade.From(10), // Normal
                Status = StatusItemEstoque.Ok,
                EntradaEm = DateTime.UtcNow,
                CriadoEm = DateTime.UtcNow,
                AlteradoEm = DateTime.UtcNow
            },
            new ItemEstoque
            {
                Id = Guid.NewGuid(),
                EmpresaId = empresaB.Id,
                ProdutoId = Guid.NewGuid(),
                QuantidadeAtual = Quantidade.From(2), // Baixo, mas empresa B
                Status = StatusItemEstoque.Ok,
                EntradaEm = DateTime.UtcNow,
                CriadoEm = DateTime.UtcNow,
                AlteradoEm = DateTime.UtcNow
            });

        await context.SaveChangesAsync();

        var repository = new ItemEstoqueRepository(context);

        var (itensBaixoA, totalA) = await repository.GetEstoqueBaixoAsync(empresaA.Id, 5, 1, 10);

        itensBaixoA.Should().ContainSingle();
        itensBaixoA.Single().QuantidadeAtual.Value.Should().Be(3);
        itensBaixoA.Single().EmpresaId.Should().Be(empresaA.Id);
        totalA.Should().Be(1);
    }
}
