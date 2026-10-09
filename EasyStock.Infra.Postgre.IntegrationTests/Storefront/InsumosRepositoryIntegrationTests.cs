using EasyStock.Domain.Entities;
using EasyStock.Domain.Enums;
using EasyStock.Infra.Postgre.Repositories;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;

namespace EasyStock.Infra.Postgre.IntegrationTests.Storefront;

/// <summary>
/// M2.3 (#1496) no Postgres: a lista de insumos só traz insumo ativo da própria empresa, e a contagem
/// de receitas por insumo traduz para SQL (produtos finais distintos).
/// </summary>
[Collection("PostgreSqlTestCollection")]
public sealed class InsumosRepositoryIntegrationTests(PostgreSqlDatabaseFixture fixture)
{
    [SkippableFact]
    public async Task InsumosDaEmpresa_EReceitasPorInsumo()
    {
        Skip.If(!fixture.IsAvailable, fixture.UnavailableReason ?? "Docker/PostgreSQL indisponivel");
        var empresaId = Guid.NewGuid();
        var outraId = Guid.NewGuid();
        Guid molhoId;

        await using (var setup = fixture.CreateDbContext())
        {
            await setup.Database.MigrateAsync();
            foreach (var id in new[] { empresaId, outraId })
            {
                setup.SetMobileTenantContext(id);
                var empresa = Empresa.Criar($"Empresa {id:N}", null);
                empresa.Id = id;
                setup.Empresas.Add(empresa);
            }
            Categoria Cat(Guid emp) => new() { Id = Guid.NewGuid(), EmpresaId = emp, Nome = "Cat", CriadoEm = DateTime.UtcNow, AlteradoEm = DateTime.UtcNow };
            var cat = Cat(empresaId);
            var catOutra = Cat(outraId);
            setup.Categorias.AddRange(cat, catOutra);
            Produto P(Guid emp, Guid catId, string nome, bool insumo, StatusProduto status = StatusProduto.Ativo) => new()
            {
                Id = Guid.NewGuid(), EmpresaId = emp, CategoriaId = catId, Nome = nome, Tipo = TipoProduto.Alimento,
                EhInsumo = insumo, Status = status, CriadoEm = DateTime.UtcNow, AlteradoEm = DateTime.UtcNow,
            };
            var molho = P(empresaId, cat.Id, "Molho", true);
            var lasanha = P(empresaId, cat.Id, "Lasanha", false);
            var nhoque = P(empresaId, cat.Id, "Nhoque", false);
            setup.Produtos.AddRange(molho, lasanha, nhoque,
                P(empresaId, cat.Id, "Insumo inativo", true, StatusProduto.Inativo),
                P(outraId, catOutra.Id, "Molho da outra", true));
            setup.ProdutosComposicao.AddRange(
                new ProdutoComposicao { Id = Guid.NewGuid(), EmpresaId = empresaId, ProdutoFinalId = lasanha.Id, InsumoId = molho.Id, Quantidade = 200, Unidade = UnidadeMedida.G },
                new ProdutoComposicao { Id = Guid.NewGuid(), EmpresaId = empresaId, ProdutoFinalId = nhoque.Id, InsumoId = molho.Id, Quantidade = 150, Unidade = UnidadeMedida.G });
            await setup.SaveChangesAsync();
            molhoId = molho.Id;
        }

        await using var db = fixture.CreateDbContext();
        db.SetMobileTenantContext(empresaId);

        var insumos = await new ProdutoRepository(db).GetInsumosAsync(empresaId);
        insumos.Select(i => i.Nome).Should().Equal("Molho");

        var receitas = await new ProdutoComposicaoRepository(db).ContarReceitasPorInsumoAsync(empresaId);
        receitas.Should().ContainKey(molhoId).WhoseValue.Should().Be(2);
    }
}
