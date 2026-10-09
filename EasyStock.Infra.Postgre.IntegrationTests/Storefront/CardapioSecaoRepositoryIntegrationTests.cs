using EasyStock.Domain.Entities;
using EasyStock.Domain.Entities.Storefront;
using EasyStock.Infra.Postgre.Repositories.Storefront;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;

namespace EasyStock.Infra.Postgre.IntegrationTests.Storefront;

/// <summary>
/// M1.3 (#1483) e M1.2 (#1482) no Postgres: a contagem de pratos por seção traduz para SQL e fica na
/// vitrine; o menu público não traz item arquivado (ArquivadoEm, migration da M1.2).
/// </summary>
[Collection("PostgreSqlTestCollection")]
public sealed class CardapioSecaoRepositoryIntegrationTests(PostgreSqlDatabaseFixture fixture)
{
    [SkippableFact]
    public async Task ContaPratosPorSecao_DaVitrine_EMenuIgnoraArquivado()
    {
        Skip.If(!fixture.IsAvailable, fixture.UnavailableReason ?? "Docker/PostgreSQL indisponivel");
        var empresaId = Guid.NewGuid();
        Guid storefrontId, massasId, outraVitrineSecaoId;

        await using (var setup = fixture.CreateDbContext())
        {
            await setup.Database.MigrateAsync();
            setup.SetMobileTenantContext(empresaId);
            var empresa = Empresa.Criar($"Empresa {empresaId:N}", null);
            empresa.Id = empresaId;
            setup.Empresas.Add(empresa);

            var vitrine = Domain.Entities.Storefront.Storefront.Criar(empresaId, $"sf-{Guid.NewGuid():N}", "Loja", 0m);
            var outra = Domain.Entities.Storefront.Storefront.Criar(empresaId, $"sf-{Guid.NewGuid():N}", "Outra", 0m);
            setup.Storefronts.AddRange(vitrine, outra);
            var massas = CardapioSecao.CriarRaiz(vitrine.Id, "Massas", 1);
            var daOutra = CardapioSecao.CriarRaiz(outra.Id, "Massas", 1);
            setup.CardapioSecoes.AddRange(massas, daOutra);

            CardapioItem Prato(Guid sf, string nome, Guid? secao)
            {
                var i = CardapioItem.CriarAvulso(sf, nome, 30m);
                i.TornarVisivel();
                i.DefinirSecao(secao);
                return i;
            }
            var arquivado = Prato(vitrine.Id, "Nhoque", massas.Id);
            arquivado.Arquivar(DateTime.UtcNow);
            setup.CardapioItens.AddRange(
                Prato(vitrine.Id, "Lasanha", massas.Id), arquivado, Prato(vitrine.Id, "Torta", null),
                Prato(outra.Id, "Pizza", daOutra.Id));
            await setup.SaveChangesAsync();
            (storefrontId, massasId, outraVitrineSecaoId) = (vitrine.Id, massas.Id, daOutra.Id);
        }

        await using var db = fixture.CreateDbContext();
        db.SetMobileTenantContext(empresaId);

        var contagem = await new CardapioSecaoRepository(db).ContarItensPorSecaoAsync(storefrontId);
        contagem.Should().ContainKey(massasId).WhoseValue.Should().Be(2, "arquivado ainda é da categoria");
        contagem.Should().NotContainKey(outraVitrineSecaoId);

        var menu = await new CardapioItemRepository(db).GetVisiveisDoStorefrontAsync(storefrontId);
        menu.Select(i => i.NomePublico).Should().BeEquivalentTo("lasanha", "torta");
    }
}
