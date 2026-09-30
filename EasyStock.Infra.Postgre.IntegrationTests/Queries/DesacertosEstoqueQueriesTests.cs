using EasyStock.Domain.Entities;
using EasyStock.Domain.Enums;
using EasyStock.Domain.ValueObjects;
using EasyStock.Infra.Postgre.Queries;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;

namespace EasyStock.Infra.Postgre.IntegrationTests.Queries;

/// <summary>S22: produto com descoberto aparece com as saídas desde o último ajuste; sem descoberto, não.</summary>
public class DesacertosEstoqueQueriesTests(PostgreSqlDatabaseFixture fixture)
    : IClassFixture<PostgreSqlDatabaseFixture>
{
    [SkippableFact]
    public async Task ProjetaProdutoComDescobertoESaidasDoLote()
    {
        Skip.If(!fixture.IsAvailable, fixture.UnavailableReason ?? "Docker/PostgreSQL indisponivel");

        var agora = DateTime.UtcNow;
        var empresa = Empresa.Criar("Casa da Baba Desacerto", "55555555000191");
        var categoria = new Categoria { Id = Guid.NewGuid(), EmpresaId = empresa.Id, Nome = "Massas", CriadoEm = agora, AlteradoEm = agora };
        var lasanha = NovoProduto(empresa.Id, categoria.Id, "Lasanha", agora);
        var bolo = NovoProduto(empresa.Id, categoria.Id, "Bolo", agora);
        var loteLasanha = NovoLote(empresa.Id, lasanha.Id, atual: 0m, descoberto: 2m, agora);
        var loteBolo = NovoLote(empresa.Id, bolo.Id, atual: 4m, descoberto: 0m, agora);
        var pedido = Guid.NewGuid();

        await using (var db = fixture.CreateDbContext())
        {
            using var _ = db.UseRowLevelSecurityBypass();
            await db.Database.MigrateAsync();
            db.Empresas.Add(empresa);
            db.Categorias.Add(categoria);
            db.Produtos.AddRange(lasanha, bolo);
            db.ItensEstoque.AddRange(loteLasanha, loteBolo);
            db.MovimentacoesEstoque.Add(NovaSaida(loteLasanha, 2m, $"{pedido}:{Guid.NewGuid()}", agora.AddHours(-1)));
            await db.SaveChangesAsync();
        }

        await using (var db = fixture.CreateDbContext())
        {
            db.SetMobileTenantContext(empresa.Id);
            var queries = new DesacertosEstoqueQueries(db);

            var desacertos = await queries.ListarAsync(empresa.Id, null);

            var d = desacertos.Should().ContainSingle().Subject;
            d.ProdutoId.Should().Be(lasanha.Id);
            d.Nome.Should().Be("Lasanha");
            d.QuantidadeDescoberta.Should().Be(2m);
            d.Saidas.Should().ContainSingle().Which.DocumentoReferencia.Should().StartWith(pedido.ToString());
            (await queries.ListarAsync(Guid.NewGuid(), null)).Should().BeEmpty("EmpresaId no WHERE");
        }
    }

    private static Produto NovoProduto(Guid empresaId, Guid categoriaId, string nome, DateTime agora) => new()
    {
        Id = Guid.NewGuid(), EmpresaId = empresaId, CategoriaId = categoriaId, Nome = nome,
        Tipo = TipoProduto.Fisico, Status = StatusProduto.Ativo, CriadoEm = agora, AlteradoEm = agora,
    };

    private static ItemEstoque NovoLote(Guid empresaId, Guid produtoId, decimal atual, decimal descoberto, DateTime agora) => new()
    {
        Id = Guid.NewGuid(), EmpresaId = empresaId, ProdutoId = produtoId,
        QuantidadeInicial = Quantidade.From(10), QuantidadeAtual = Quantidade.From(atual),
        QuantidadeDescoberta = Quantidade.From(descoberto),
        CustoUnitario = Dinheiro.FromDecimal(10m), EntradaEm = agora.AddDays(-1),
        Status = StatusItemEstoque.Ok, CriadoEm = agora, AlteradoEm = agora,
    };

    private static MovimentacaoEstoque NovaSaida(ItemEstoque lote, decimal quantidade, string referencia, DateTime em) => new()
    {
        Id = Guid.NewGuid(), EmpresaId = lote.EmpresaId, ItemEstoqueId = lote.Id, ProdutoId = lote.ProdutoId,
        Tipo = TipoMovimentacaoEstoque.Saida, Natureza = NaturezaMovimentacaoEstoque.Venda,
        Quantidade = Quantidade.From(quantidade), DataMovimentacao = em, DocumentoReferencia = referencia, CriadoEm = em,
    };
}
