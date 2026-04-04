using EasyStock.Domain.Entities;
using EasyStock.Domain.Enums;
using EasyStock.Domain.ValueObjects;
using EasyStock.Infra.Postgre.Repositories;
using FluentAssertions;

namespace EasyStock.Infra.Postgre.IntegrationTests.Repositories;

public class ItemEstoqueRepositoryIntegrationTests(PostgreSqlDatabaseFixture fixture) : IClassFixture<PostgreSqlDatabaseFixture>
{
    [Fact]
    public async Task SearchAsync_deve_buscar_item_por_codigo_chave_variacao_e_descricao()
    {
        if (!fixture.IsAvailable) return;
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
            Status = StatusItemEstoque.Ativo,
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

    [Fact]
    public async Task Deve_persistir_value_objects_do_item_estoque()
    {
        if (!fixture.IsAvailable) return;
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
                Status = StatusItemEstoque.Ativo,
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
}
