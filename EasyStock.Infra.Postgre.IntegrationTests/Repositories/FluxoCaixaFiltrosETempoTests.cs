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
    // #2095 (F13): as linhas do fluxo são dias civis de Brasília; 03:00Z é a meia-noite de Brasília de 01/06.
    private static readonly DateTime Inicio = new(2026, 6, 1, 3, 0, 0, DateTimeKind.Utc);
    // #2268: o KPI do mês usa o mês civil de Brasília (como o Extrato); a referência é a data civil 00:00Z.
    private static readonly DateTime InicioMesUtc = new(2026, 6, 1, 0, 0, 0, DateTimeKind.Utc);

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
        var virada = Inicio.AddMonths(1); // 01/07 03:00Z, meia-noite de Brasília
        AdicionarPagamento(db, lado, empresaId, categoriaId, centroCustoId, virada.AddMilliseconds(-500), 10m);
        AdicionarPagamento(db, lado, empresaId, categoriaId, centroCustoId, virada, 20m);
        await db.SaveChangesAsync();

        var kpis = await new FluxoCaixaQueries(db).KpisDashboardAsync(empresaId, InicioMesUtc);

        (lado == TipoLadoFinanceiro.Pagar ? kpis.TotalPagoMes : kpis.TotalRecebidoMes).Should().Be(10m);
    }

    /// <summary>
    /// #2546: o card "a vencer em 30 dias" inclui o 30º dia inteiro. Vencimento gravado pelo console é 03:00Z
    /// (meia-noite de Brasília) e ficava fora com `&lt;= hoje + 30` (00:00Z); o 31º dia continua fora.
    /// </summary>
    [SkippableTheory]
    [InlineData(TipoLadoFinanceiro.Pagar)]
    [InlineData(TipoLadoFinanceiro.Receber)]
    public async Task Kpi_a_vencer_30_dias_inclui_o_trigesimo_dia_gravado_as_tres_horas_utc(TipoLadoFinanceiro lado)
    {
        Skip.If(!fixture.IsAvailable, fixture.UnavailableReason);
        await using var db = fixture.CreateDbContext();
        var (empresaId, categoriaId, _) = Preparar(db, lado);
        var dia30 = Inicio.AddDays(30);  // 01/07 03:00Z
        var dia31 = Inicio.AddDays(31);  // 02/07 03:00Z
        AdicionarParcelaAberta(db, lado, empresaId, categoriaId, dia30, 100m);
        AdicionarParcelaAberta(db, lado, empresaId, categoriaId, dia31, 7m);
        await db.SaveChangesAsync();

        var kpis = await new FluxoCaixaQueries(db).KpisDashboardAsync(empresaId, InicioMesUtc);

        if (lado == TipoLadoFinanceiro.Pagar)
        {
            kpis.TotalAVencer30dPagar.Should().Be(100m);
            kpis.QtdContasPagarAVencer30d.Should().Be(1);
        }
        else
        {
            kpis.TotalAVencer30dReceber.Should().Be(100m);
            kpis.QtdContasReceberAVencer30d.Should().Be(1);
        }
    }

    /// <summary>
    /// #2546: o fluxo mensal segue o mês do calendário. Começando em 31/01, a parcela de 10/02 cai na linha
    /// "02/2026"; antes a linha "01/2026" ia de 31/01 a 27/02 e engolia fevereiro.
    /// </summary>
    [SkippableFact]
    public async Task Fluxo_mensal_alinha_as_linhas_ao_mes_do_calendario()
    {
        Skip.If(!fixture.IsAvailable, fixture.UnavailableReason);
        await using var db = fixture.CreateDbContext();
        var (empresaId, categoriaId, _) = Preparar(db, TipoLadoFinanceiro.Pagar);
        var inicio = new DateTime(2026, 1, 31, 3, 0, 0, DateTimeKind.Utc);
        AdicionarParcelaAberta(db, TipoLadoFinanceiro.Pagar, empresaId, categoriaId,
            new DateTime(2026, 2, 10, 3, 0, 0, DateTimeKind.Utc), 50m);
        await db.SaveChangesAsync();

        var buckets = await new FluxoCaixaQueries(db).FluxoBucketsAsync(
            empresaId, PeriodicidadeFluxo.Mensal, inicio, new DateTime(2026, 3, 31, 2, 59, 59, DateTimeKind.Utc));

        buckets.Select(b => b.Rotulo).Should().Equal("01/2026", "02/2026", "03/2026");
        buckets.Single(b => b.Rotulo == "02/2026").PrevistoPagar.Should().Be(50m);
        buckets.Single(b => b.Rotulo == "01/2026").PrevistoPagar.Should().Be(0m);
    }

    /// <summary>
    /// #2268: o realizado soma o movimento avulso do Caixa; o que um pagamento de parcela referencia já conta como
    /// pagamento de conta e não entra de novo. Estornado, abertura e outra empresa ficam fora.
    /// </summary>
    [SkippableFact]
    public async Task Movimentos_avulsos_do_caixa_excluem_o_vinculado_a_parcela()
    {
        Skip.If(!fixture.IsAvailable, fixture.UnavailableReason);
        await using var db = fixture.CreateDbContext();
        var (empresaId, categoriaId, centroCustoId) = Preparar(db, TipoLadoFinanceiro.Receber);
        var vinculado = MovimentoCaixa.Criar(empresaId, "entrada", 10m, Inicio.AddHours(1));
        var estornado = MovimentoCaixa.Criar(empresaId, "entrada", 100m, Inicio.AddHours(1));
        estornado.EstornadoEm = Inicio.AddHours(2);
        db.MovimentosCaixa.AddRange(vinculado, estornado,
            MovimentoCaixa.Criar(empresaId, "entrada", 7m, Inicio.AddHours(1)),
            MovimentoCaixa.Criar(empresaId, "saida", 3m, Inicio.AddHours(2)),
            MovimentoCaixa.Criar(empresaId, "abertura", 50m, Inicio.AddMinutes(1)),
            MovimentoCaixa.Criar(empresaId, "entrada", 5m, Inicio.AddDays(1)));
        AdicionarPagamento(db, TipoLadoFinanceiro.Receber, empresaId, categoriaId, centroCustoId, Inicio.AddHours(1), 10m);
        await db.SaveChangesAsync();
        var pagamento = db.PagamentosParcela.Local.Single(p => p.EmpresaId == empresaId);
        pagamento.MovimentoCaixaId = vinculado.Id;
        var (outraEmpresa, _, _) = Preparar(db, TipoLadoFinanceiro.Receber);
        db.MovimentosCaixa.Add(MovimentoCaixa.Criar(outraEmpresa, "entrada", 999m, Inicio.AddHours(1)));
        await db.SaveChangesAsync();
        db.SetMobileTenantContext(empresaId);

        var linhas = await new FluxoCaixaQueries(db).MovimentosAvulsosDoCaixaAsync(empresaId, Inicio, Inicio.AddDays(1));

        linhas.Select(l => l.Valor).Should().BeEquivalentTo([7m, -3m]);
    }

    [SkippableFact]
    public async Task Periodo_muito_longo_e_recusado_com_mensagem_e_sem_laco_infinito()
    {
        Skip.If(!fixture.IsAvailable, fixture.UnavailableReason);
        await using var db = fixture.CreateDbContext();
        var (empresaId, _, _) = Preparar(db, TipoLadoFinanceiro.Pagar);
        await db.SaveChangesAsync();

        // #2095 (F13): antes cortava calado em 24 linhas; agora o teto é 366 e passar dele responde com mensagem.
        var acao = () => new FluxoCaixaQueries(db).FluxoBucketsAsync(empresaId,
            PeriodicidadeFluxo.Diario, Inicio, DateTime.SpecifyKind(DateTime.MaxValue, DateTimeKind.Utc));

        (await acao.Should().ThrowAsync<ArgumentException>()).Which.Message.Should().Contain("no máximo 366 linhas");
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

    private static void AdicionarParcelaAberta(EasyStockDbContext db, TipoLadoFinanceiro lado,
        Guid empresaId, Guid categoriaId, DateTime vencimento, decimal valor)
    {
        if (lado == TipoLadoFinanceiro.Pagar)
        {
            var conta = ContaPagar.Criar(empresaId, null, categoriaId, "Conta aberta", InicioMesUtc);
            conta.AdicionarParcela(1, valor, vencimento);
            conta.Emitir();
            db.ContasPagar.Add(conta);
        }
        else
        {
            var conta = ContaReceber.Criar(empresaId, null, categoriaId, "Conta aberta", InicioMesUtc);
            conta.AdicionarParcela(1, valor, vencimento);
            conta.Emitir();
            db.ContasReceber.Add(conta);
        }
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
