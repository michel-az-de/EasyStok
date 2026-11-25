using EasyStock.Application.UseCases.GerenciarProduto;
using EasyStock.Domain.Entities;
using EasyStock.Domain.Enums;
using EasyStock.Domain.Sales;
using EasyStock.Domain.ValueObjects;
using EasyStock.Infra.Postgre.Repositories;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;

namespace EasyStock.Infra.Postgre.IntegrationTests.Repositories;

/// <summary>
/// #3299: o desempenho dos produtos roda no Postgres sem misturar empresas, nem quando o pedido de outra empresa
/// aponta um produto desta; cancelado e rascunho não vendem; frete não entra; e a faixa é a soma das linhas.
/// </summary>
[Collection("PostgreSqlTestCollection")]
public sealed class ProdutosDesempenhoQueryIntegrationTests(PostgreSqlDatabaseFixture fixture)
{
    private static readonly DateTime Agora = new(2026, 11, 24, 18, 0, 0, DateTimeKind.Utc);

    [SkippableFact]
    public async Task DesempenhoSomaSoAEmpresaEBateComAsLinhas()
    {
        Skip.If(!fixture.IsAvailable, fixture.UnavailableReason ?? "PostgreSQL indisponível");
        var empresa = Empresa.Criar("Desempenho #3299", Documento());
        var outra = Empresa.Criar("Desempenho alheio #3299", Documento());
        var categoria = new Categoria { Id = Guid.NewGuid(), EmpresaId = empresa.Id, Nome = "Massas 3299", CriadoEm = Agora, AlteradoEm = Agora };
        var categoriaAlheia = new Categoria { Id = Guid.NewGuid(), EmpresaId = outra.Id, Nome = "Alheia 3299", CriadoEm = Agora, AlteradoEm = Agora };
        var lasanha = NovoProduto(empresa.Id, categoria.Id, "Lasanha 3299", preco: 45m, custo: 15m);
        var molho = NovoProduto(empresa.Id, categoria.Id, "Molho 3299", preco: 15m);
        var farinha = NovoProduto(empresa.Id, categoria.Id, "Farinha 3299", insumo: true);
        var alheio = NovoProduto(outra.Id, categoriaAlheia.Id, "Alheio 3299", preco: 10m);

        var pedidos = new[]
        {
            NovoPedido(empresa.Id, StatusPedidoMapper.Entregue, Agora.AddDays(-5), It(lasanha.Id, 2m, 80m), It(molho.Id, 1m, 15m), It(null, 1m, 7m, PedidoItem.PrefixoDoFrete + "Centro")),
            NovoPedido(empresa.Id, StatusPedidoMapper.Entregue, Agora.AddDays(-30), It(lasanha.Id, 1m, 40m), It(null, 1m, 9m, "Avulso da casa")),
            NovoPedido(empresa.Id, StatusPedidoMapper.Preparando, Agora.AddDays(-120), It(lasanha.Id, 1m, 45m)),
            NovoPedido(empresa.Id, StatusPedidoMapper.Cancelado, Agora.AddDays(-2), It(lasanha.Id, 10m, 450m)),
            NovoPedido(empresa.Id, StatusPedidoMapper.Rascunho, Agora.AddDays(-1), It(molho.Id, 5m, 75m)),
            // Alheio: pedido da outra empresa apontando a lasanha desta, e venda do produto alheio.
            NovoPedido(outra.Id, StatusPedidoMapper.Entregue, Agora.AddDays(-3), It(lasanha.Id, 50m, 2000m), It(alheio.Id, 3m, 30m)),
            // Desta empresa apontando o produto alheio: não é produto do catálogo desta empresa.
            NovoPedido(empresa.Id, StatusPedidoMapper.Entregue, Agora.AddDays(-4), It(alheio.Id, 1m, 10m)),
        };
        await using (var gravar = fixture.CreateDbContext())
        {
            using var bypass = gravar.UseRowLevelSecurityBypass();
            await gravar.Database.MigrateAsync();
            gravar.Empresas.AddRange(empresa, outra);
            gravar.Categorias.AddRange(categoria, categoriaAlheia);
            gravar.Produtos.AddRange(lasanha, molho, farinha, alheio);
            gravar.Pedidos.AddRange(pedidos);
            await gravar.SaveChangesAsync();
        }

        await using var db = fixture.CreateDbContext();
        db.SetMobileTenantContext(empresa.Id);
        var consulta = new ProdutosDesempenhoQuery(db);

        (await consulta.CatalogoAsync(empresa.Id)).Select(p => p.Nome).Should().BeEquivalentTo(["Lasanha 3299", "Molho 3299", "Farinha 3299"]);
        var historico = (await consulta.HistoricoAsync(empresa.Id)).ToDictionary(h => h.ProdutoId);
        historico.Keys.Should().BeEquivalentTo([lasanha.Id, molho.Id], "cancelado, rascunho e pedido alheio não vendem");
        historico[lasanha.Id].Pedidos.Should().Be(3);
        historico[lasanha.Id].PrimeiraVendaEm.Should().Be(Agora.AddDays(-120));
        historico[lasanha.Id].UltimaVendaEm.Should().Be(Agora.AddDays(-5));
        (await consulta.ReceitaSemProdutoDesdeAsync(empresa.Id, Agora.AddDays(-90))).Should().Be(9m, "o avulso entra, o frete não");

        var r = await new DesempenhoDosProdutosUseCase(consulta, new Relogio()).ExecuteAsync(empresa.Id);
        var l = r.Produtos.Single(p => p.ProdutoId == lasanha.Id);
        l.Receita.Should().Be(120m);
        l.Unidades.Should().Be(3m);
        l.Pedidos.Should().Be(2);
        l.ReceitaAnterior.Should().Be(45m);
        l.PrecoVendido.Should().Be(41.25m, "R$ 165 em 4 unidades no ano");
        l.Curva.Should().Be(DesempenhoDoProduto.CurvaA);
        r.Resumo.Receita.Should().Be(135m).And.Be(r.Produtos.Sum(p => p.Receita));
        r.Resumo.Compras.Should().Be(2);
        r.Resumo.SemCusto.Should().Be(1, "o molho; a farinha é insumo");
    }

    private sealed class Relogio : TimeProvider { public override DateTimeOffset GetUtcNow() => new(Agora); }

    private static string Documento() => Guid.NewGuid().ToString("N")[..14];

    private static Produto NovoProduto(Guid empresaId, Guid categoriaId, string nome, decimal? preco = null, decimal? custo = null, bool insumo = false) => new()
    {
        Id = Guid.NewGuid(), EmpresaId = empresaId, CategoriaId = categoriaId, Nome = nome, Tipo = TipoProduto.Alimento,
        Status = StatusProduto.Ativo, EhInsumo = insumo, CriadoEm = Agora.AddDays(-200), AlteradoEm = Agora,
        PrecoReferencia = preco is { } p ? Dinheiro.FromDecimal(p) : null, CustoReferencia = custo is { } c ? Dinheiro.FromDecimal(c) : null,
    };

    private static Pedido NovoPedido(Guid empresaId, string status, DateTime criadoEm, params (Guid? ProdutoId, decimal Quantidade, decimal Subtotal, string? Nome)[] itens)
    {
        var p = Pedido.Criar(empresaId, null, origem: "whatsapp");
        p.Status = status;
        p.CriadoEm = criadoEm;
        foreach (var (produtoId, quantidade, subtotal, nome) in itens)
            p.Itens.Add(new PedidoItem { Id = Guid.NewGuid(), PedidoId = p.Id, ProdutoId = produtoId, Nome = nome ?? "Item", Quantidade = quantidade,
                PrecoUnitario = subtotal / quantidade, Subtotal = subtotal, CriadoEm = criadoEm });
        p.Total = Dinheiro.FromDecimal(itens.Sum(i => i.Subtotal));
        return p;
    }

    private static (Guid? ProdutoId, decimal Quantidade, decimal Subtotal, string? Nome) It(Guid? produtoId, decimal quantidade, decimal subtotal, string? nome = null) =>
        (produtoId, quantidade, subtotal, nome);
}
