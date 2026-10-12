using System.Data.Common;
using EasyStock.Application.UseCases.Caixa;
using EasyStock.Domain.Entities;
using EasyStock.Domain.Enums;
using EasyStock.Domain.ValueObjects;
using EasyStock.Infra.Postgre.Data;
using EasyStock.Infra.Postgre.Repositories;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Xunit.Abstractions;

namespace EasyStock.Infra.Postgre.IntegrationTests.Repositories;

[Collection("PostgreSqlTestCollection")]
public sealed class AnalyticsAgregacaoTests(PostgreSqlDatabaseFixture fixture, ITestOutputHelper output)
{
    private EasyStockDbContext CriarContexto(MedidorLeituras? medidor = null)
    {
        var options = new DbContextOptionsBuilder<EasyStockDbContext>().UseNpgsql(fixture.ConnectionString);
        if (medidor is not null) options.AddInterceptors(medidor);
        return new EasyStockDbContext(options.Options);
    }

    private static AnalyticsRepository CriarRepo(EasyStockDbContext db) =>
        new(db, new CaixaSaldoCalculator(new CaixaRepository(db)));

    private async Task<(Guid EmpresaId, Guid LojaId, Guid[] Produtos)> PrepararAsync(bool incluirPerda = false)
    {
        await using var db = CriarContexto();
        var empresaId = Guid.NewGuid();
        var agora = DateTime.UtcNow;
        db.SetMobileTenantContext(empresaId);
        db.Empresas.Add(new Empresa
        {
            Id = empresaId, Nome = "Agregação", Documento = empresaId.ToString("N")[..14],
            CriadoEm = agora, AlteradoEm = agora
        });
        var loja = Loja.Criar(empresaId, "Loja alvo");
        var outraLoja = Loja.Criar(empresaId, "Outra loja");
        db.Lojas.AddRange(loja, outraLoja);
        var categoria = new Categoria
        {
            Id = Guid.NewGuid(), EmpresaId = empresaId, Nome = "Categoria",
            CriadoEm = agora, AlteradoEm = agora
        };
        db.Categorias.Add(categoria);
        var produtos = new Guid[4];
        for (var i = 0; i < produtos.Length; i++)
        {
            var produto = new Produto
            {
                Id = Guid.NewGuid(), EmpresaId = empresaId, CategoriaId = categoria.Id,
                Nome = $"Produto {i}", Tipo = TipoProduto.Fisico, Status = StatusProduto.Ativo,
                CriadoEm = agora, AlteradoEm = agora
            };
            produtos[i] = produto.Id;
            db.Produtos.Add(produto);
            var estoque = new ItemEstoque
            {
                Id = Guid.NewGuid(), EmpresaId = empresaId, ProdutoId = produto.Id, LojaId = loja.Id,
                QuantidadeInicial = Quantidade.From(100), QuantidadeAtual = Quantidade.From(100),
                CustoUnitario = Dinheiro.FromDecimal(10m), PrecoVendaSugerido = Dinheiro.FromDecimal(15m),
                EntradaEm = agora, UltimaMovimentacaoEm = agora.AddDays(-1), Status = StatusItemEstoque.Ok,
                CriadoEm = agora, AlteradoEm = agora
            };
            db.ItensEstoque.Add(estoque);
            for (var n = 0; n < 20; n++)
                db.MovimentacoesEstoque.Add(Movimento(estoque, i + 1, i == 0 ? null : 12.34m * (i + 1), agora.AddDays(-1)));

            if (i != 0) continue;
            db.MovimentacoesEstoque.Add(Movimento(estoque, 999, 999m, agora.AddDays(-60)));
            db.MovimentacoesEstoque.Add(Movimento(estoque, 999, 999m, agora.AddDays(1)));
            var entrada = Movimento(estoque, 999, 999m, agora.AddDays(-1));
            entrada.Tipo = TipoMovimentacaoEstoque.Entrada;
            db.MovimentacoesEstoque.Add(entrada);
            var estoqueOutraLoja = new ItemEstoque
            {
                Id = Guid.NewGuid(), EmpresaId = empresaId, ProdutoId = produto.Id, LojaId = outraLoja.Id,
                QuantidadeInicial = Quantidade.From(100), QuantidadeAtual = Quantidade.From(100),
                CustoUnitario = Dinheiro.FromDecimal(10m), EntradaEm = agora, Status = StatusItemEstoque.Ok,
                CriadoEm = agora, AlteradoEm = agora
            };
            db.ItensEstoque.Add(estoqueOutraLoja);
            db.MovimentacoesEstoque.Add(Movimento(estoqueOutraLoja, 999, 999m, agora.AddDays(-1)));
            if (incluirPerda)
            {
                var perda = Movimento(estoque, 999, 999m, agora.AddDays(-1));
                perda.Natureza = NaturezaMovimentacaoEstoque.Perda;
                db.MovimentacoesEstoque.Add(perda);
            }
        }
        await db.SaveChangesAsync();
        return (empresaId, loja.Id, produtos);
    }

    private static MovimentacaoEstoque Movimento(ItemEstoque estoque, int quantidade, decimal? valor, DateTime instante) => new()
    {
        Id = Guid.NewGuid(), EmpresaId = estoque.EmpresaId, ProdutoId = estoque.ProdutoId, ItemEstoqueId = estoque.Id,
        Tipo = TipoMovimentacaoEstoque.Saida, Natureza = NaturezaMovimentacaoEstoque.Venda,
        Quantidade = Quantidade.From(quantidade), ValorTotal = valor.HasValue ? Dinheiro.FromDecimal(valor.Value) : null,
        DataMovimentacao = instante, CriadoEm = instante
    };

    [SkippableTheory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Ranking_considera_apenas_vendas_da_loja_e_do_periodo(bool ascending)
    {
        Skip.If(!fixture.IsAvailable, fixture.UnavailableReason ?? "PostgreSQL indisponível");
        var (empresaId, lojaId, produtos) = await PrepararAsync(incluirPerda: true);
        await using var db = CriarContexto();
        db.SetMobileTenantContext(empresaId);

        var ranking = await CriarRepo(db).GetTopProdutosPorLojaAsync(empresaId, lojaId, top: 2, ascending: ascending);

        var indices = ascending ? new[] { 0, 1 } : new[] { 3, 2 };
        ranking.Select(p => p.ProdutoId).Should().Equal(indices.Select(i => produtos[i]));
        foreach (var (produto, i) in ranking.Zip(indices))
        {
            produto.QuantidadeVendida.Should().Be(20 * (i + 1));
            produto.ReceitaGerada.Should().Be(i == 0 ? 0m : 20 * 12.34m * (i + 1));
            produto.TaxaSaidaDiaria.Should().Be(Math.Round(20m * (i + 1) / 30, 2));
        }
        (await CriarRepo(db).GetTopProdutosPorLojaAsync(Guid.NewGuid(), lojaId)).Should().BeEmpty();
    }

    [SkippableTheory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Ranking_limita_as_leituras_independentemente_do_total_de_movimentacoes(bool ascending)
    {
        Skip.If(!fixture.IsAvailable, fixture.UnavailableReason ?? "PostgreSQL indisponível");
        var (empresaId, lojaId, _) = await PrepararAsync();
        var medidor = new MedidorLeituras();
        await using var db = CriarContexto(medidor);
        db.SetMobileTenantContext(empresaId);

        var ranking = await CriarRepo(db).GetTopProdutosPorLojaAsync(empresaId, lojaId, top: 2, ascending: ascending);

        ranking.Should().HaveCount(2);
        output.WriteLine($"Ranking: consultas={medidor.Consultas}, leituras={medidor.Leituras}");
        medidor.Consultas.Should().Be(1);
        medidor.Leituras.Should().BeLessThanOrEqualTo(3, "ler só o top 2 e o fim do reader, em vez das 80 movimentações");
    }

    [SkippableFact]
    public async Task Dashboard_agrega_sem_carregar_todas_as_linhas_e_preserva_valores()
    {
        Skip.If(!fixture.IsAvailable, fixture.UnavailableReason ?? "PostgreSQL indisponível");
        var (empresaId, lojaId, _) = await PrepararAsync(incluirPerda: true);
        var medidor = new MedidorLeituras();
        await using var db = CriarContexto(medidor);
        db.SetMobileTenantContext(empresaId);

        var resumo = await CriarRepo(db).GetDashboardResumoAsync(empresaId, lojaId: lojaId);

        resumo.TotalSkus.Should().Be(4);
        resumo.QuantidadeTotalEmEstoque.Should().Be(400);
        resumo.ValorCustoEstoque.Should().Be(4000m);
        resumo.ValorTotalEstoque.Should().Be(6000m);
        resumo.ReceitaEstimadaPeriodo.Should().Be(2221.20m);
        resumo.MediaVendasDiaria.Should().Be(6.67m);
        resumo.AlertasEstoqueBaixo.Should().Be(0);
        output.WriteLine($"Dashboard: consultas={medidor.Consultas}, leituras={medidor.Leituras}");
        medidor.Leituras.Should().BeLessThanOrEqualTo(12, "o volume lido deve depender dos agregados, não dos lotes/vendas");
    }

    [SkippableFact]
    public async Task Comparacao_e_resumo_excluem_perdas_da_receita_e_da_velocidade_de_vendas()
    {
        Skip.If(!fixture.IsAvailable, fixture.UnavailableReason ?? "PostgreSQL indisponível");
        var (empresaId, lojaId, _) = await PrepararAsync(incluirPerda: true);
        await using var db = CriarContexto();
        db.SetMobileTenantContext(empresaId);
        var repo = CriarRepo(db);

        var comparacao = (await repo.GetComparacaoLojasAsync(empresaId)).Single(l => l.LojaId == lojaId);
        var resumo = await repo.GetResumoInteligenciaLojaAsync(empresaId, lojaId);

        comparacao.ReceitaPeriodo.Should().Be(2221.20m);
        comparacao.MediaVendasDiaria.Should().Be(6.67m);
        comparacao.HealthScore.Should().Be(83.3m);
        resumo.Should().NotBeNull();
        resumo!.ReceitaPeriodo.Should().Be(comparacao.ReceitaPeriodo);
        resumo.MediaVendasDiaria.Should().Be(comparacao.MediaVendasDiaria);
        resumo.HealthScore.Should().Be(comparacao.HealthScore);
        resumo.HealthClassificacao.Should().Be("Excelente");
        resumo.DimSalesVelocity.Should().Be(33.4m);
        resumo.ItensAbaixoMinimo.Should().Be(0);
    }

    [SkippableTheory]
    [InlineData(0)]
    [InlineData(-1)]
    public async Task Ranking_com_limite_nao_positivo_permanece_vazio(int top)
    {
        Skip.If(!fixture.IsAvailable, fixture.UnavailableReason ?? "PostgreSQL indisponível");
        var (empresaId, lojaId, _) = await PrepararAsync();
        await using var db = CriarContexto();
        db.SetMobileTenantContext(empresaId);

        (await CriarRepo(db).GetTopProdutosPorLojaAsync(empresaId, lojaId, top: top)).Should().BeEmpty();
    }

    [SkippableFact]
    public async Task Dashboard_sem_dados_preserva_zeros()
    {
        Skip.If(!fixture.IsAvailable, fixture.UnavailableReason ?? "PostgreSQL indisponível");
        await using var db = CriarContexto();
        var empresaId = Guid.NewGuid();
        db.SetMobileTenantContext(empresaId);

        var resumo = await CriarRepo(db).GetDashboardResumoAsync(empresaId);

        resumo.TotalSkus.Should().Be(0);
        resumo.QuantidadeTotalEmEstoque.Should().Be(0);
        resumo.ValorTotalEstoque.Should().Be(0m);
        resumo.ValorCustoEstoque.Should().Be(0m);
        resumo.ReceitaEstimadaPeriodo.Should().Be(0m);
        resumo.MediaVendasDiaria.Should().Be(0m);
    }

    private sealed class MedidorLeituras : DbCommandInterceptor
    {
        public int Consultas { get; private set; }
        public int Leituras { get; private set; }

        public override InterceptionResult DataReaderDisposing(DbCommand command,
            DataReaderDisposingEventData eventData, InterceptionResult result)
        {
            Consultas++;
            Leituras += eventData.ReadCount;
            return result;
        }
    }
}
