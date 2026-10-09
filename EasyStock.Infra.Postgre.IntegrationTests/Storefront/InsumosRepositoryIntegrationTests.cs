using EasyStock.Domain.Entities;
using EasyStock.Domain.Enums;
using EasyStock.Infra.Postgre.Repositories;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using EasyStock.Application.DependencyInjection;
using EasyStock.Application.Ports.Output;
using EasyStock.Application.UseCases.Atendimento.Producao;
using EasyStock.Infra.Postgre.Data;
using EasyStock.Infra.Postgre.DependencyInjection;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using NSubstitute;

namespace EasyStock.Infra.Postgre.IntegrationTests.Storefront;

/// <summary>
/// M2.3 (#1496) no Postgres: a lista de insumos só traz insumo ativo da própria empresa, e a contagem
/// de receitas por insumo traduz para SQL (produtos finais distintos).
/// </summary>
[Collection("PostgreSqlTestCollection")]
public sealed class InsumosRepositoryIntegrationTests(PostgreSqlDatabaseFixture fixture)
{
    [SkippableFact]
    public async Task CadastroReal_PersisteInsumoCompleto_EPermiteConsultarEAjustar()
    {
        Skip.If(!fixture.IsAvailable, fixture.UnavailableReason ?? "Docker/PostgreSQL indisponivel");
        Guid empresaId;
        await using (var seed = fixture.CreateDbContext())
        {
            var empresa = Empresa.Criar($"Insumos {Guid.NewGuid():N}", null);
            empresaId = empresa.Id;
            seed.SetMobileTenantContext(empresaId);
            seed.Empresas.Add(empresa);
            await seed.SaveChangesAsync();
        }
        var config = new ConfigurationBuilder().Build();
        var services = new ServiceCollection();
        services.AddSingleton<IConfiguration>(config);
        services.AddLogging();
        services.AddHttpContextAccessor();
        services.AddSingleton(Substitute.For<ICurrentUserAccessor>());
        services.AddMemoryCache();
        services.AddSingleton(Substitute.For<ICacheService>());
        services.AddEasyStockPostgreInfrastructure(fixture.ConnectionString, config);
        services.AddEasyStockApplication();
        await using var provider = services.BuildServiceProvider();
        Guid id;
        await using (var scope = provider.CreateAsyncScope())
        {
            scope.ServiceProvider.GetRequiredService<EasyStockDbContext>().SetMobileTenantContext(empresaId);
            var sut = scope.ServiceProvider.GetRequiredService<InsumosDaProducaoUseCase>();
            id = await sut.CriarAsync(empresaId, Guid.Empty, new InsumoInput("Molho de teste", UnidadeMedida.G, 100, 0.02m));
            var lista = await sut.ListarAsync(empresaId);
            lista.Should().ContainSingle(i => i.ProdutoId == id && i.Unidade == UnidadeMedida.G
                && i.Minimo == 100 && i.Custo == 0.02m && i.AbaixoDoMinimo);
        }
        await using (var scope = provider.CreateAsyncScope())
        {
            scope.ServiceProvider.GetRequiredService<EasyStockDbContext>().SetMobileTenantContext(empresaId);
            await scope.ServiceProvider.GetRequiredService<InsumosDaProducaoUseCase>()
                .AtualizarAsync(empresaId, id, new InsumoInput(null, null, 200, 0.03m));
        }
        await using var verificar = fixture.CreateDbContext();
        verificar.SetMobileTenantContext(empresaId);
        var produto = await verificar.Produtos.SingleAsync(p => p.EmpresaId == empresaId);
        produto.Id.Should().Be(id);
        produto.EhInsumo.Should().BeTrue();
        produto.Status.Should().Be(StatusProduto.Ativo);
        produto.PrecoReferencia.Should().BeNull();
        produto.QuantidadeMinima.Should().Be(200);
        produto.CustoReferencia!.Valor.Should().Be(0.03m);

        // Edição não pode perder isolamento nem sobrescrever uma versão concorrente.
        var repo = new ProdutoRepository(verificar);
        (await repo.GetByIdParaAtualizarAsync(Guid.NewGuid(), id)).Should().BeNull();
        await using var concorrente = fixture.CreateDbContext();
        concorrente.SetMobileTenantContext(empresaId);
        var repoConcorrente = new ProdutoRepository(concorrente);
        var copia = (await repoConcorrente.GetByIdParaAtualizarAsync(empresaId, id))!;
        produto.QuantidadeMinima = 220;
        await repo.UpdateAsync(produto);
        await verificar.SaveChangesAsync();
        copia.QuantidadeMinima = 230;
        await repoConcorrente.UpdateAsync(copia);
        var salvarVersaoAntiga = () => concorrente.SaveChangesAsync();
        await salvarVersaoAntiga.Should().ThrowAsync<DbUpdateConcurrencyException>();
    }

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
