using EasyStock.Domain.Entities;
using EasyStock.Domain.Enums;
using EasyStock.Infra.Postgre.Repositories;
using FluentAssertions;

namespace EasyStock.Infra.Postgre.IntegrationTests.Repositories;

[Collection("PostgreSqlTestCollection")]
public sealed class CategoriaRepositoryIntegrationTests(PostgreSqlDatabaseFixture fixture)
{
    [SkippableFact]
    public async Task ExisteProdutosNaCategoriaAsync_considera_produto_vinculado_pela_subcategoria()
    {
        Skip.If(!fixture.IsAvailable, fixture.UnavailableReason ?? "Docker/PostgreSQL unavailable");
        var empresaId = Guid.NewGuid();
        var raizId = Guid.NewGuid();
        var subcategoriaId = Guid.NewGuid();
        var agora = DateTime.UtcNow;

        await using (var db = fixture.CreateDbContext())
        {
            db.SetMobileTenantContext(empresaId);
            db.Empresas.Add(new Empresa { Id = empresaId, Nome = "Empresa Subcategoria", Documento = empresaId.ToString("N")[..14], CriadoEm = agora, AlteradoEm = agora });
            db.Categorias.Add(new Categoria { Id = raizId, EmpresaId = empresaId, Nome = "Bebidas", CriadoEm = agora, AlteradoEm = agora });
            db.Categorias.Add(new Categoria { Id = subcategoriaId, EmpresaId = empresaId, CategoriaPaiId = raizId, Nome = "Sucos", CriadoEm = agora, AlteradoEm = agora });
            db.Produtos.Add(new Produto
            {
                Id = Guid.NewGuid(),
                EmpresaId = empresaId,
                CategoriaId = raizId,
                SubcategoriaId = subcategoriaId,
                Nome = "Suco de laranja",
                Tipo = TipoProduto.Fisico,
                Status = StatusProduto.Ativo,
                CriadoEm = agora,
                AlteradoEm = agora
            });
            await db.SaveChangesAsync();
        }

        await using (var db = fixture.CreateDbContext())
        {
            db.SetMobileTenantContext(empresaId);
            var emUso = await new CategoriaRepository(db).ExisteProdutosNaCategoriaAsync(subcategoriaId);

            emUso.Should().BeTrue("produto com SubcategoriaId apontando para a subcategoria bloquearia o DELETE pela FK");
        }
    }
}
