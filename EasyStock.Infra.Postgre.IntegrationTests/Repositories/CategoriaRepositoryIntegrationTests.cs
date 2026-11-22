using EasyStock.Application.UseCases.Common;
using EasyStock.Application.UseCases.GerenciarCategoria;
using EasyStock.Domain.Entities;
using EasyStock.Domain.Enums;
using EasyStock.Infra.Postgre.Data;
using EasyStock.Infra.Postgre.Repositories;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;

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

    private async Task<(Guid EmpresaId, Guid RaizId, Guid SubEmUsoId, Guid SubLivreId)> SemearHierarquiaAsync()
    {
        var empresaId = Guid.NewGuid();
        var raizId = Guid.NewGuid();
        var subEmUsoId = Guid.NewGuid();
        var subLivreId = Guid.NewGuid();
        var agora = DateTime.UtcNow;

        await using var db = fixture.CreateDbContext();
        db.SetMobileTenantContext(empresaId);
        db.Empresas.Add(new Empresa { Id = empresaId, Nome = "Empresa Exclusao", Documento = empresaId.ToString("N")[..14], CriadoEm = agora, AlteradoEm = agora });
        db.Categorias.Add(new Categoria { Id = raizId, EmpresaId = empresaId, Nome = "Bebidas", CriadoEm = agora, AlteradoEm = agora });
        db.Categorias.Add(new Categoria { Id = subEmUsoId, EmpresaId = empresaId, CategoriaPaiId = raizId, Nome = "Sucos", CriadoEm = agora, AlteradoEm = agora });
        db.Categorias.Add(new Categoria { Id = subLivreId, EmpresaId = empresaId, CategoriaPaiId = raizId, Nome = "Chas", CriadoEm = agora, AlteradoEm = agora });
        db.Produtos.Add(new Produto
        {
            Id = Guid.NewGuid(), EmpresaId = empresaId, CategoriaId = raizId, SubcategoriaId = subEmUsoId,
            Nome = "Suco de uva", Tipo = TipoProduto.Fisico, Status = StatusProduto.Ativo, CriadoEm = agora, AlteradoEm = agora
        });
        await db.SaveChangesAsync();
        return (empresaId, raizId, subEmUsoId, subLivreId);
    }

    [SkippableFact]
    public async Task RemoverAsync_raiz_com_filhas_responde_validacao_em_vez_de_violar_a_fk()
    {
        // #2504: antes, 23503 em categorias_CategoriaPaiId_fkey virava 500.
        Skip.If(!fixture.IsAvailable, fixture.UnavailableReason ?? "Docker/PostgreSQL unavailable");
        var (empresaId, raizId, _, _) = await SemearHierarquiaAsync();

        await using var db = fixture.CreateDbContext();
        db.SetMobileTenantContext(empresaId);
        var act = () => new GerenciarCategoriaUseCase(new CategoriaRepository(db), db).RemoverAsync(raizId, empresaId);

        await act.Should().ThrowAsync<UseCaseValidationException>().WithMessage("*subcategorias*");
        (await db.Categorias.AnyAsync(c => c.Id == raizId)).Should().BeTrue();
    }

    [SkippableFact]
    public async Task RemoverAsync_subcategoria_em_uso_responde_validacao_em_vez_de_violar_a_fk()
    {
        Skip.If(!fixture.IsAvailable, fixture.UnavailableReason ?? "Docker/PostgreSQL unavailable");
        var (empresaId, _, subEmUsoId, _) = await SemearHierarquiaAsync();

        await using var db = fixture.CreateDbContext();
        db.SetMobileTenantContext(empresaId);
        var act = () => new GerenciarCategoriaUseCase(new CategoriaRepository(db), db).RemoverAsync(subEmUsoId, empresaId);

        await act.Should().ThrowAsync<UseCaseValidationException>().WithMessage("Há produtos nesta subcategoria*");
    }

    [SkippableFact]
    public async Task RemoverAsync_subcategoria_livre_e_excluida()
    {
        Skip.If(!fixture.IsAvailable, fixture.UnavailableReason ?? "Docker/PostgreSQL unavailable");
        var (empresaId, _, _, subLivreId) = await SemearHierarquiaAsync();

        await using (var db = fixture.CreateDbContext())
        {
            db.SetMobileTenantContext(empresaId);
            await new GerenciarCategoriaUseCase(new CategoriaRepository(db), db).RemoverAsync(subLivreId, empresaId);
        }

        await using (var db = fixture.CreateDbContext())
        {
            db.SetMobileTenantContext(empresaId);
            (await db.Categorias.AnyAsync(c => c.Id == subLivreId)).Should().BeFalse();
        }
    }

    [SkippableTheory]
    [InlineData("bebidas")]
    [InlineData("BEBIDAS")]
    [InlineData("  Bebidas ")]
    public async Task ExisteNomeAsync_acha_o_nome_sem_diferenciar_caixa_nem_espacos_nas_pontas(string nome)
    {
        Skip.If(!fixture.IsAvailable, fixture.UnavailableReason ?? "Docker/PostgreSQL unavailable");
        var (empresaId, _, _, _) = await SemearHierarquiaAsync();

        await using var db = fixture.CreateDbContext();
        db.SetMobileTenantContext(empresaId);

        (await new CategoriaRepository(db).ExisteNomeAsync(empresaId, nome)).Should().BeTrue();
    }

    [SkippableFact]
    public async Task ExisteNomeAsync_ignora_a_propria_categoria_e_a_de_outra_empresa()
    {
        Skip.If(!fixture.IsAvailable, fixture.UnavailableReason ?? "Docker/PostgreSQL unavailable");
        var (empresaId, raizId, _, _) = await SemearHierarquiaAsync();
        var (outraEmpresaId, _, _, _) = await SemearHierarquiaAsync();

        await using var db = fixture.CreateDbContext();
        db.SetMobileTenantContext(empresaId);
        var repo = new CategoriaRepository(db);

        (await repo.ExisteNomeAsync(empresaId, "Bebidas", raizId)).Should().BeFalse("a própria categoria não conta como duplicada");
        (await repo.ExisteNomeAsync(empresaId, "Bebidas", Guid.NewGuid())).Should().BeTrue();
        (await repo.ExisteNomeAsync(empresaId, "Bebida")).Should().BeFalse("a comparação é por igualdade, não por prefixo");
        (await repo.ExisteNomeAsync(outraEmpresaId, "Bebidas")).Should().BeFalse("o contexto é de outra empresa");
    }

    // #3101: lower("Nome") na consulta não casa com o índice único (EmpresaId, nome_lower);
    // o nome vira filtro linha a linha. A consulta precisa comparar a coluna gerada.
    [SkippableFact]
    public async Task ExisteNomeAsync_compara_a_coluna_gerada_do_indice_unico()
    {
        Skip.If(!fixture.IsAvailable, fixture.UnavailableReason ?? "Docker/PostgreSQL unavailable");
        var (empresaId, _, _, _) = await SemearHierarquiaAsync();
        var comandos = new List<string>();

        await using var db = new EasyStockDbContext(new DbContextOptionsBuilder<EasyStockDbContext>()
            .UseNpgsql(fixture.ConnectionString)
            .LogTo(comandos.Add, [RelationalEventId.CommandExecuted])
            .Options);
        db.SetMobileTenantContext(empresaId);

        (await new CategoriaRepository(db).ExisteNomeAsync(empresaId, "bebidas")).Should().BeTrue();

        comandos.Should().Contain(sql => sql.Contains("nome_lower"));
        comandos.Should().NotContain(sql => sql.Contains("lower(c.\"Nome\")"));
    }
}
