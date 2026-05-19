using EasyStock.Domain.Entities;
using EasyStock.Domain.ValueObjects;
using EasyStock.Infra.Postgre.Repositories;
using FluentAssertions;

namespace EasyStock.Infra.Postgre.IntegrationTests.Repositories;

public class ProdutoVariacaoRepositoryIntegrationTests(PostgreSqlDatabaseFixture fixture) : IClassFixture<PostgreSqlDatabaseFixture>
{
    [SkippableFact]
    public async Task SearchAsync_deve_buscar_variacao_por_sku_cor_tamanho_e_descricao()
    {
        Skip.If(!fixture.IsAvailable, fixture.UnavailableReason ?? "Docker/PostgreSQL unavailable");
        await fixture.ResetDatabaseAsync();

        await using var context = fixture.CreateDbContext();
        var empresaId = Guid.NewGuid();
        var categoriaId = Guid.NewGuid();
        var produtoId = Guid.NewGuid();

        context.Empresas.Add(new Empresa { Id = empresaId, Nome = "Empresa A", Documento = "444", CriadoEm = DateTime.UtcNow, AlteradoEm = DateTime.UtcNow });
        context.Categorias.Add(new Categoria { Id = categoriaId, EmpresaId = empresaId, Nome = "Audio", CriadoEm = DateTime.UtcNow, AlteradoEm = DateTime.UtcNow });
        context.Produtos.Add(new Produto
        {
            Id = produtoId,
            EmpresaId = empresaId,
            CategoriaId = categoriaId,
            Nome = "Galaxy Buds FE",
            Tipo = Domain.Enums.TipoProduto.Fisico,
            Status = Domain.Enums.StatusProduto.Ativo,
            CriadoEm = DateTime.UtcNow,
            AlteradoEm = DateTime.UtcNow
        });
        context.ProdutosVariacao.Add(new ProdutoVariacao
        {
            Id = Guid.NewGuid(),
            EmpresaId = empresaId,
            ProdutoId = produtoId,
            Nome = "Grafite",
            Cor = "Grafite",
            Tamanho = "Unico",
            DescricaoComercial = "Buds FE grafite premium",
            Sku = CodigoSku.From("CAP3426"),
            Ativa = true,
            CriadoEm = DateTime.UtcNow,
            AlteradoEm = DateTime.UtcNow
        });

        await context.SaveChangesAsync();

        var repository = new ProdutoVariacaoRepository(context);

        (await repository.SearchAsync(empresaId, "CAP3426")).Should().ContainSingle();
        (await repository.SearchAsync(empresaId, "grafite")).Should().ContainSingle();
        (await repository.SearchAsync(empresaId, "premium")).Should().ContainSingle();
    }
}
