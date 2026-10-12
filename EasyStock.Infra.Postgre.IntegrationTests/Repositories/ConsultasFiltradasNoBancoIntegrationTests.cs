using EasyStock.Application.UseCases.Atendimento.Producao;
using EasyStock.Domain.Entities;
using EasyStock.Domain.Entities.Storefront;
using EasyStock.Domain.Enums;
using EasyStock.Domain.ValueObjects;
using EasyStock.Infra.Postgre.Repositories;
using EasyStock.Infra.Postgre.Repositories.Storefront;
using FluentAssertions;

namespace EasyStock.Infra.Postgre.IntegrationTests.Repositories;

/// <summary>
/// #1575: filtros que saíram da memória para o SQL devolvem o mesmo que o caminho antigo (a lista inteira
/// filtrada depois), em Postgres real. Cada prova refaz o caminho antigo ao lado do novo e compara.
/// </summary>
[Collection("PostgreSqlTestCollection")]
public sealed class ConsultasFiltradasNoBancoIntegrationTests(PostgreSqlDatabaseFixture fixture)
{
    private static readonly DateTime Agora = new(2026, 10, 11, 15, 0, 0, DateTimeKind.Utc);

    [SkippableFact]
    public async Task ArquivadosDoCardapio_MesmoResultadoDeTodosFiltradosEmMemoria()
    {
        Skip.If(!fixture.IsAvailable, fixture.UnavailableReason ?? "Docker/PostgreSQL indisponível");
        var empresa = Empresa.Criar("Cardápio arquivados", null);
        var vitrine = Domain.Entities.Storefront.Storefront.Criar(empresa.Id, $"sf-{Guid.NewGuid():N}", "Loja", 0m);
        var outra = Domain.Entities.Storefront.Storefront.Criar(empresa.Id, $"sf-{Guid.NewGuid():N}", "Outra", 0m);
        var massas = CardapioSecao.CriarRaiz(vitrine.Id, "Massas", 1);

        CardapioItem Prato(Guid sf, string nome, bool arquivado, Guid? secao = null)
        {
            var i = CardapioItem.CriarAvulso(sf, nome, 30m);
            i.TornarVisivel();
            i.DefinirSecao(secao);
            if (arquivado) i.Arquivar(Agora);
            return i;
        }
        var nhoque = Prato(vitrine.Id, "Nhoque", arquivado: true, massas.Id);
        nhoque.AdicionarVariacao(CardapioItemVariacao.Criar(nhoque.Id, "300 g", 28m));
        var torta = Prato(vitrine.Id, "Torta", arquivado: true);
        await using (var seed = fixture.CreateDbContext())
        {
            seed.SetMobileTenantContext(empresa.Id);
            seed.Empresas.Add(empresa);
            seed.Storefronts.AddRange(vitrine, outra);
            seed.CardapioSecoes.Add(massas);
            seed.CardapioItens.AddRange(
                nhoque, torta, Prato(vitrine.Id, "Lasanha", arquivado: false, massas.Id),
                Prato(outra.Id, "Pizza", arquivado: true));
            await seed.SaveChangesAsync();
        }

        await using var db = fixture.CreateDbContext();
        db.SetMobileTenantContext(empresa.Id);
        var repo = new CardapioItemRepository(db);

        var novo = await repo.GetArquivadosDoStorefrontAsync(vitrine.Id);
        var antigo = (await repo.GetTodosDoStorefrontAsync(vitrine.Id)).Where(i => i.EstaArquivado).ToList();

        static object Linha(CardapioItem i) => new
        {
            i.Id, Nome = i.NomeEfetivo(), Preco = i.PrecoEfetivo(), i.PesoExibicao, Categoria = i.CategoriaEfetiva(), i.FotoUrl,
            Porcoes = i.Variacoes.Count,
        };
        novo.Select(Linha).Should().BeEquivalentTo(antigo.Select(Linha));
        novo.Select(i => i.Id).Should().BeEquivalentTo([nhoque.Id, torta.Id], "só os tirados desta vitrine");
        novo.Single(i => i.Id == nhoque.Id).CategoriaEfetiva().Should().Be("Massas");
    }

    [SkippableFact]
    public async Task LotesComSaldoOuDescoberto_MesmoSaldoEDescobertoDeTodosOsLotes()
    {
        Skip.If(!fixture.IsAvailable, fixture.UnavailableReason ?? "Docker/PostgreSQL indisponível");
        var empresa = Empresa.Criar("Lotes zerados", null);
        var categoria = new Categoria { Id = Guid.NewGuid(), EmpresaId = empresa.Id, Nome = "Massas", CriadoEm = Agora, AlteradoEm = Agora };
        var produto = new Produto
        {
            Id = Guid.NewGuid(), EmpresaId = empresa.Id, CategoriaId = categoria.Id, Nome = "Lasanha",
            Tipo = TipoProduto.Fisico, Status = StatusProduto.Ativo, CriadoEm = Agora, AlteradoEm = Agora,
        };
        var soZerados = new Produto
        {
            Id = Guid.NewGuid(), EmpresaId = empresa.Id, CategoriaId = categoria.Id, Nome = "Nhoque",
            Tipo = TipoProduto.Fisico, Status = StatusProduto.Ativo, CriadoEm = Agora, AlteradoEm = Agora,
        };

        ItemEstoque Lote(Guid produtoId, decimal atual, decimal descoberta) => new()
        {
            Id = Guid.NewGuid(), EmpresaId = empresa.Id, ProdutoId = produtoId,
            QuantidadeInicial = Quantidade.From(10), QuantidadeAtual = Quantidade.From(atual),
            QuantidadeDescoberta = Quantidade.From(descoberta),
            CustoUnitario = Dinheiro.FromDecimal(10m), EntradaEm = Agora.AddDays(-1),
            Status = StatusItemEstoque.Ok, CriadoEm = Agora, AlteradoEm = Agora,
        };
        var comSaldo = Lote(produto.Id, 4, 0);
        var zeradoComDescoberto = Lote(produto.Id, 0, 3);
        var zerado = Lote(produto.Id, 0, 0);
        await using (var seed = fixture.CreateDbContext())
        {
            seed.SetMobileTenantContext(empresa.Id);
            seed.Empresas.Add(empresa);
            seed.Categorias.Add(categoria);
            seed.Produtos.AddRange(produto, soZerados);
            seed.ItensEstoque.AddRange(comSaldo, zeradoComDescoberto, zerado, Lote(soZerados.Id, 0, 0), Lote(soZerados.Id, 0, 0));
            await seed.SaveChangesAsync();
        }

        await using var db = fixture.CreateDbContext();
        db.SetMobileTenantContext(empresa.Id);
        var repo = new ItemEstoqueRepository(db);
        Guid[] ids = [produto.Id, soZerados.Id];

        var todos = await repo.GetByProdutosAsync(empresa.Id, ids, null);
        var filtrados = await repo.GetByProdutosAsync(empresa.Id, ids, null, apenasComSaldoOuDescoberto: true);

        todos[produto.Id].Should().HaveCount(3, "o padrão segue trazendo o lote zerado");
        todos[soZerados.Id].Should().HaveCount(2);
        filtrados[produto.Id].Select(l => l.Id).Should().BeEquivalentTo([comSaldo.Id, zeradoComDescoberto.Id]);
        filtrados.Should().NotContainKey(soZerados.Id, "produto só com lote zerado não aparece; quem consome trata como vazio");

        var hoje = DateOnly.FromDateTime(Agora);
        foreach (var id in ids)
        {
            var antigo = EstoqueDoDiaUseCase.SaldoEDescoberto(todos[id], hoje);
            var novo = EstoqueDoDiaUseCase.SaldoEDescoberto(filtrados.TryGetValue(id, out var l) ? l : [], hoje);
            novo.Should().Be(antigo);
        }
        EstoqueDoDiaUseCase.SaldoEDescoberto(filtrados[produto.Id], hoje).Should().Be((4m, 3m));
    }
}
