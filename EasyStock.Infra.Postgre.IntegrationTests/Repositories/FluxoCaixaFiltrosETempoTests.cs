using EasyStock.Application.Ports.Output.Persistence;
using EasyStock.Domain.Entities;
using EasyStock.Domain.Entities.Financeiro;
using EasyStock.Domain.Enums.Financeiro;
using EasyStock.Infra.Postgre.Data;
using EasyStock.Infra.Postgre.Repositories;
using FluentAssertions;

namespace EasyStock.Infra.Postgre.IntegrationTests.Repositories;

[Collection("PostgreSqlTestCollection")]
public sealed class FluxoCaixaFiltrosETempoTests(PostgreSqlDatabaseFixture fixture)
{
    private static readonly DateTime Inicio = new(2026, 6, 1, 0, 0, 0, DateTimeKind.Utc);

    [SkippableTheory]
    [InlineData(TipoLadoFinanceiro.Pagar, true, false, 40)]
    [InlineData(TipoLadoFinanceiro.Pagar, false, true, 30)]
    [InlineData(TipoLadoFinanceiro.Pagar, true, true, 10)]
    [InlineData(TipoLadoFinanceiro.Pagar, false, false, 100)]
    [InlineData(TipoLadoFinanceiro.Receber, true, false, 40)]
    [InlineData(TipoLadoFinanceiro.Receber, false, true, 30)]
    [InlineData(TipoLadoFinanceiro.Receber, true, true, 10)]
    [InlineData(TipoLadoFinanceiro.Receber, false, false, 100)]
    public async Task Realizado_respeita_os_mesmos_filtros_do_previsto(
        TipoLadoFinanceiro lado, bool porCategoria, bool porCentroCusto, int esperado)
    {
        Skip.If(!fixture.IsAvailable, fixture.UnavailableReason);
        await using var db = fixture.CreateDbContext();
        var (empresaId, categoriaId, centroCustoId) = Preparar(db, lado);
        var outraCategoria = CategoriaFinanceira.Criar(empresaId, "Outra", TipoCategoriaFinanceira.Despesa);
        var outroCentro = CentroCusto.Criar(empresaId, "OUTRO", "Outro centro");
        db.AddRange(outraCategoria, outroCentro);
        AdicionarPagamento(db, lado, empresaId, categoriaId, centroCustoId, Inicio, 10m);
        AdicionarPagamento(db, lado, empresaId, outraCategoria.Id, centroCustoId, Inicio, 20m);
        AdicionarPagamento(db, lado, empresaId, categoriaId, outroCentro.Id, Inicio, 30m);
        AdicionarPagamento(db, lado, empresaId, outraCategoria.Id, outroCentro.Id, Inicio, 40m);
        var (outraEmpresa, categoriaOutraEmpresa, centroOutraEmpresa) = Preparar(db, lado);
        AdicionarPagamento(db, lado, outraEmpresa, categoriaOutraEmpresa, centroOutraEmpresa, Inicio, 999m);
        await db.SaveChangesAsync();
        db.SetMobileTenantContext(empresaId);

        var buckets = await new FluxoCaixaQueries(db).FluxoBucketsAsync(empresaId,
            PeriodicidadeFluxo.Diario, Inicio, Inicio.AddDays(1),
            porCategoria ? categoriaId : null, porCentroCusto ? centroCustoId : null);

        var previsto = buckets.Sum(b => lado == TipoLadoFinanceiro.Pagar ? b.PrevistoPagar : b.PrevistoReceber);
        var realizado = buckets.Sum(b => lado == TipoLadoFinanceiro.Pagar ? b.RealizadoPagar : b.RealizadoReceber);
        previsto.Should().Be(esperado);
        realizado.Should().Be(esperado);
    }

    [SkippableTheory]
    [InlineData(PeriodicidadeFluxo.Diario, TipoLadoFinanceiro.Pagar)]
    [InlineData(PeriodicidadeFluxo.Semanal, TipoLadoFinanceiro.Receber)]
    [InlineData(PeriodicidadeFluxo.Mensal, TipoLadoFinanceiro.Pagar)]
    public async Task Ultimo_segundo_do_bucket_nao_perde_pagamento(
        PeriodicidadeFluxo periodicidade, TipoLadoFinanceiro lado)
    {
        Skip.If(!fixture.IsAvailable, fixture.UnavailableReason);
        await using var db = fixture.CreateDbContext();
        var (empresaId, categoriaId, centroCustoId) = Preparar(db, lado);
        var virada = periodicidade switch
        {
            PeriodicidadeFluxo.Diario => Inicio.AddDays(1),
            PeriodicidadeFluxo.Semanal => Inicio.AddDays(7),
            _ => Inicio.AddMonths(1)
        };
        AdicionarPagamento(db, lado, empresaId, categoriaId, centroCustoId, virada.AddMilliseconds(-500), 10m);
        AdicionarPagamento(db, lado, empresaId, categoriaId, centroCustoId, virada, 20m);
        await db.SaveChangesAsync();

        var buckets = await new FluxoCaixaQueries(db).FluxoBucketsAsync(
            empresaId, periodicidade, Inicio, virada.AddHours(1));

        (lado == TipoLadoFinanceiro.Pagar ? buckets[0].RealizadoPagar : buckets[0].RealizadoReceber).Should().Be(10m);
        (lado == TipoLadoFinanceiro.Pagar ? buckets[1].RealizadoPagar : buckets[1].RealizadoReceber).Should().Be(20m);
    }

    [SkippableTheory]
    [InlineData(TipoLadoFinanceiro.Pagar)]
    [InlineData(TipoLadoFinanceiro.Receber)]
    public async Task Kpi_mensal_inclui_fracao_do_ultimo_segundo_sem_incluir_mes_seguinte(TipoLadoFinanceiro lado)
    {
        Skip.If(!fixture.IsAvailable, fixture.UnavailableReason);
        await using var db = fixture.CreateDbContext();
        var (empresaId, categoriaId, centroCustoId) = Preparar(db, lado);
        var virada = Inicio.AddMonths(1);
        AdicionarPagamento(db, lado, empresaId, categoriaId, centroCustoId, virada.AddMilliseconds(-500), 10m);
        AdicionarPagamento(db, lado, empresaId, categoriaId, centroCustoId, virada, 20m);
        await db.SaveChangesAsync();

        var kpis = await new FluxoCaixaQueries(db).KpisDashboardAsync(empresaId, Inicio);

        (lado == TipoLadoFinanceiro.Pagar ? kpis.TotalPagoMes : kpis.TotalRecebidoMes).Should().Be(10m);
    }

    [SkippableFact]
    public async Task Periodo_muito_longo_para_no_limite_de_24_buckets()
    {
        Skip.If(!fixture.IsAvailable, fixture.UnavailableReason);
        await using var db = fixture.CreateDbContext();
        var (empresaId, _, _) = Preparar(db, TipoLadoFinanceiro.Pagar);
        await db.SaveChangesAsync();

        var buckets = await new FluxoCaixaQueries(db).FluxoBucketsAsync(empresaId,
            PeriodicidadeFluxo.Diario, Inicio, DateTime.SpecifyKind(DateTime.MaxValue, DateTimeKind.Utc));

        buckets.Count.Should().Be(24);
    }

    private static (Guid EmpresaId, Guid CategoriaId, Guid CentroCustoId) Preparar(EasyStockDbContext db, TipoLadoFinanceiro lado)
    {
        var empresaId = Guid.NewGuid();
        db.SetMobileTenantContext(empresaId);
        db.Empresas.Add(new Empresa
        {
            Id = empresaId, Nome = "Regressão financeira", Documento = empresaId.ToString("N")[..14],
            CriadoEm = DateTime.UtcNow, AlteradoEm = DateTime.UtcNow
        });
        var categoria = CategoriaFinanceira.Criar(empresaId, "Categoria",
            lado == TipoLadoFinanceiro.Pagar ? TipoCategoriaFinanceira.Despesa : TipoCategoriaFinanceira.Receita);
        var centro = CentroCusto.Criar(empresaId, "CENTRO", "Centro de custo");
        db.AddRange(categoria, centro);
        return (empresaId, categoria.Id, centro.Id);
    }

    private static void AdicionarPagamento(EasyStockDbContext db, TipoLadoFinanceiro lado,
        Guid empresaId, Guid categoriaId, Guid centroCustoId, DateTime em, decimal valor)
    {
        var pagamento = PagamentoParcela.CriarConfirmado(empresaId, lado, valor, "pix", em);
        if (lado == TipoLadoFinanceiro.Pagar)
        {
            var conta = ContaPagar.Criar(empresaId, null, categoriaId, "Conta paga", Inicio, centroCustoId);
            conta.AdicionarParcela(1, valor, Inicio);
            conta.Emitir();
            conta.Parcelas.Single().RegistrarPagamento(pagamento, Inicio);
            db.ContasPagar.Add(conta);
        }
        else
        {
            var conta = ContaReceber.Criar(empresaId, null, categoriaId, "Conta recebida", Inicio, centroCustoId);
            conta.AdicionarParcela(1, valor, Inicio);
            conta.Emitir();
            conta.Parcelas.Single().RegistrarPagamento(pagamento, Inicio);
            db.ContasReceber.Add(conta);
        }
    }
}
