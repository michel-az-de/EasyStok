using EasyStock.Application.Ports.Output.Persistence;
using EasyStock.Application.UseCases.Common;
using EasyStock.Application.UseCases.Financeiro.Common;
using EasyStock.Application.UseCases.Financeiro.ContasPagar;
using EasyStock.Application.UseCases.Financeiro.ContasReceber;
using EasyStock.Domain.Entities;
using EasyStock.Domain.Entities.Financeiro;
using EasyStock.Domain.Enums.Financeiro;
using EasyStock.Infra.Postgre.Data;
using EasyStock.Infra.Postgre.Repositories;
using FluentAssertions;
using Microsoft.Extensions.Logging.Abstractions;

namespace EasyStock.Infra.Postgre.IntegrationTests.Repositories;

/// <summary>
/// #2095 (banca do financeiro, pacote A): listagem, ordenação, busca e fluxo de caixa com banco real.
/// Os ids F* são os do relatório da banca de 27/10/2026.
/// </summary>
[Collection("PostgreSqlTestCollection")]
public sealed class ContasFinanceirasBanca2095Tests(PostgreSqlDatabaseFixture fixture)
{
    // DataVencimento convive em duas formas: meia-noite UTC da data civil (legado, Razor) e meia-noite de Brasília
    // (03:00Z, o console manda `instanteFinanceiro`). As duas são o mesmo dia civil.
    private static DateTime Legado(int mes, int dia) => new(2026, mes, dia, 0, 0, 0, DateTimeKind.Utc);
    private static DateTime Console(int mes, int dia) => new(2026, mes, dia, 3, 0, 0, DateTimeKind.Utc);

    private static (Guid EmpresaId, Guid CategoriaId) Empresa(EasyStockDbContext db, TipoCategoriaFinanceira tipo = TipoCategoriaFinanceira.Ambas)
    {
        var empresaId = Guid.NewGuid();
        db.SetMobileTenantContext(empresaId);
        db.Empresas.Add(new Empresa
        {
            Id = empresaId, Nome = "Banca 2095", Documento = empresaId.ToString("N")[..14],
            CriadoEm = DateTime.UtcNow, AlteradoEm = DateTime.UtcNow
        });
        var categoria = CategoriaFinanceira.Criar(empresaId, "Categoria", tipo);
        db.CategoriasFinanceiras.Add(categoria);
        return (empresaId, categoria.Id);
    }

    private static ContaPagar Pagar(EasyStockDbContext db, Guid empresaId, Guid cat, string descricao, params DateTime[] vencimentos)
    {
        var conta = ContaPagar.Criar(empresaId, null, cat, descricao, new DateTime(2026, 9, 1, 0, 0, 0, DateTimeKind.Utc));
        for (var i = 0; i < vencimentos.Length; i++) conta.AdicionarParcela(i + 1, 10m, vencimentos[i]);
        conta.Emitir();
        db.ContasPagar.Add(conta);
        return conta;
    }

    // ── F4: filtro de vencimento ────────────────────────────────────────────

    [SkippableFact]
    public async Task F4_vencimento_com_data_pura_e_o_dia_civil_inteiro_e_nao_quebra_no_Npgsql()
    {
        Skip.If(!fixture.IsAvailable, fixture.UnavailableReason ?? "PG indisponível");
        await using var db = fixture.CreateDbContext();
        var (empresaId, cat) = Empresa(db);
        var primeiroDiaConsole = Pagar(db, empresaId, cat, "01/10 console", Console(10, 1));
        var primeiroDiaLegado = Pagar(db, empresaId, cat, "01/10 legado", Legado(10, 1));
        var ultimoDiaLegado = Pagar(db, empresaId, cat, "31/10 legado", Legado(10, 31));
        var ultimoDiaConsole = Pagar(db, empresaId, cat, "31/10 console", Console(10, 31));
        var antes = Pagar(db, empresaId, cat, "30/09", Console(9, 30));
        var depois = Pagar(db, empresaId, cat, "01/11", Legado(11, 1));
        await db.SaveChangesAsync();
        var uc = new ListarContasPagarUseCase(new ContaPagarRepository(db));

        // ?vencimentoDe=2026-10-01&vencimentoAte=2026-10-31: o model binding entrega Kind=Unspecified.
        var r = await uc.ExecuteAsync(new ListarContasPagarQuery(empresaId,
            VencimentoDe: new DateTime(2026, 10, 1), VencimentoAte: new DateTime(2026, 10, 31)));

        r.Itens.Select(c => c.Id).Should().BeEquivalentTo(new[]
            { primeiroDiaConsole.Id, primeiroDiaLegado.Id, ultimoDiaLegado.Id, ultimoDiaConsole.Id });
        r.Itens.Should().NotContain(c => c.Id == antes.Id || c.Id == depois.Id);
    }

    [SkippableFact]
    public async Task F4_vencimento_em_ISO_com_Z_continua_aceito_na_conta_a_receber()
    {
        Skip.If(!fixture.IsAvailable, fixture.UnavailableReason ?? "PG indisponível");
        await using var db = fixture.CreateDbContext();
        var (empresaId, cat) = Empresa(db);
        var dentro = ContaReceber.Criar(empresaId, null, cat, "Dentro", Legado(9, 1));
        dentro.AdicionarParcela(1, 10m, Console(10, 15));
        dentro.Emitir();
        var fora = ContaReceber.Criar(empresaId, null, cat, "Fora", Legado(9, 1));
        fora.AdicionarParcela(1, 10m, Legado(10, 16));
        fora.Emitir();
        db.ContasReceber.AddRange(dentro, fora);
        await db.SaveChangesAsync();
        var uc = new ListarContasReceberUseCase(new ContaReceberRepository(db));

        // Console: instanteFinanceiro('2026-10-15') e instanteFinanceiro('2026-10-15', true).
        var r = await uc.ExecuteAsync(new ListarContasReceberQuery(empresaId,
            VencimentoDe: DateTime.Parse("2026-10-15T03:00:00Z").ToUniversalTime(),
            VencimentoAte: DateTime.Parse("2026-10-16T02:59:59.999Z").ToUniversalTime()));

        r.Itens.Select(c => c.Id).Should().Equal(dentro.Id);
    }

    // ── F11: ordenação pelo próximo vencimento em aberto ────────────────────

    [SkippableFact]
    public async Task F11_ordenacao_padrao_e_pelo_proximo_vencimento_em_aberto_e_sem_parcela_aberta_vai_para_o_fim()
    {
        Skip.If(!fixture.IsAvailable, fixture.UnavailableReason ?? "PG indisponível");
        await using var db = fixture.CreateDbContext();
        var (empresaId, cat) = Empresa(db);
        var hoje = DateTime.UtcNow.Date;
        var venceEm60 = Pagar(db, empresaId, cat, "vence em 60d", hoje.AddDays(60));
        // 1ª parcela (vencida há 10 dias) já paga: o próximo vencimento em aberto é daqui a 2 dias.
        var proximaEm2 = Pagar(db, empresaId, cat, "1ª paga, 2ª em 2d", hoje.AddDays(-10), hoje.AddDays(2));
        var paga = proximaEm2.Parcelas.First(p => p.Numero == 1);
        paga.RegistrarPagamento(PagamentoParcela.CriarConfirmado(empresaId, TipoLadoFinanceiro.Pagar, 10m, "pix", DateTime.UtcNow), hoje);
        proximaEm2.AtualizarStatusPorParcelas();
        var venceAmanha = Pagar(db, empresaId, cat, "vence amanhã", hoje.AddDays(1));
        var quitada = Pagar(db, empresaId, cat, "quitada", hoje.AddDays(-30));
        quitada.Parcelas.Single().RegistrarPagamento(PagamentoParcela.CriarConfirmado(empresaId, TipoLadoFinanceiro.Pagar, 10m, "pix", DateTime.UtcNow), hoje);
        quitada.AtualizarStatusPorParcelas();
        await db.SaveChangesAsync();
        var repo = new ContaPagarRepository(db);

        var (asc, _) = await repo.ListarAsync(empresaId);
        var (desc, _) = await repo.ListarAsync(empresaId, order: "desc");

        asc.Select(c => c.Id).Should().Equal(venceAmanha.Id, proximaEm2.Id, venceEm60.Id, quitada.Id);
        desc.Select(c => c.Id).Should().Equal(venceEm60.Id, proximaEm2.Id, venceAmanha.Id, quitada.Id);
    }

    [SkippableFact]
    public async Task F11_mesmo_vencimento_tem_desempate_estavel_entre_paginas()
    {
        Skip.If(!fixture.IsAvailable, fixture.UnavailableReason ?? "PG indisponível");
        await using var db = fixture.CreateDbContext();
        var (empresaId, cat) = Empresa(db);
        var mesmoDia = DateTime.UtcNow.Date.AddDays(5);
        var contas = Enumerable.Range(1, 7).Select(i => Pagar(db, empresaId, cat, $"igual {i}", mesmoDia)).ToList();
        await db.SaveChangesAsync();
        var repo = new ContaPagarRepository(db);

        var vistos = new List<Guid>();
        for (var pagina = 1; pagina <= 4; pagina++)
        {
            var (itens, _) = await repo.ListarAsync(empresaId, page: pagina, pageSize: 2);
            vistos.AddRange(itens.Select(c => c.Id));
        }

        vistos.Should().OnlyHaveUniqueItems().And.BeEquivalentTo(contas.Select(c => c.Id));
    }

    // ── F15: busca com curinga ──────────────────────────────────────────────

    [SkippableTheory]
    [InlineData("%", "Desconto de 10%")]
    [InlineData("_", "Conta_luz")]
    public async Task F15_busca_com_curinga_do_ILIKE_procura_o_caractere_literal(string busca, string esperada)
    {
        Skip.If(!fixture.IsAvailable, fixture.UnavailableReason ?? "PG indisponível");
        await using var db = fixture.CreateDbContext();
        var (empresaId, cat) = Empresa(db);
        Pagar(db, empresaId, cat, "Aluguel", Console(10, 1));
        Pagar(db, empresaId, cat, esperada, Console(10, 1));
        await db.SaveChangesAsync();

        var (itens, _) = await new ContaPagarRepository(db).ListarAsync(empresaId, busca: busca);

        itens.Select(c => c.Descricao).Should().Equal(esperada);
    }

    // ── F13: fluxo de caixa no dia de Brasília ──────────────────────────────

    [SkippableFact]
    public async Task F13_fluxo_diario_agrupa_pelo_dia_de_Brasilia_e_nao_gera_linha_extra()
    {
        Skip.If(!fixture.IsAvailable, fixture.UnavailableReason ?? "PG indisponível");
        await using var db = fixture.CreateDbContext();
        var (empresaId, cat) = Empresa(db);
        // Previsto do dia 02/06 nas duas formas gravadas.
        Pagar(db, empresaId, cat, "vence 02/06 legado", Legado(6, 2));
        Pagar(db, empresaId, cat, "vence 02/06 console", Console(6, 2));
        // Pago em 01/06 às 22h de Brasília (02/06 01:00Z): é do dia 01/06.
        var paga = Pagar(db, empresaId, cat, "paga 01/06 22h", Console(6, 1));
        paga.Parcelas.Single().RegistrarPagamento(PagamentoParcela.CriarConfirmado(empresaId, TipoLadoFinanceiro.Pagar, 10m, "pix",
            new DateTime(2026, 6, 2, 1, 0, 0, DateTimeKind.Utc)), Console(6, 1));
        await db.SaveChangesAsync();

        // O console manda instanteFinanceiro('2026-06-01') e instanteFinanceiro('2026-06-02', true).
        var buckets = await new FluxoCaixaQueries(db).FluxoBucketsAsync(empresaId, PeriodicidadeFluxo.Diario,
            new DateTime(2026, 6, 1, 3, 0, 0, DateTimeKind.Utc), new DateTime(2026, 6, 3, 2, 59, 59, 999, DateTimeKind.Utc));

        buckets.Select(b => b.Rotulo).Should().Equal("01/06", "02/06");
        buckets[0].RealizadoPagar.Should().Be(10m);
        buckets[1].RealizadoPagar.Should().Be(0m);
        buckets[0].PrevistoPagar.Should().Be(10m);
        buckets[1].PrevistoPagar.Should().Be(20m);
    }

    [SkippableFact]
    public async Task F13_periodo_com_mais_linhas_que_o_teto_e_recusado_com_mensagem_em_vez_de_cortar_calado()
    {
        Skip.If(!fixture.IsAvailable, fixture.UnavailableReason ?? "PG indisponível");
        await using var db = fixture.CreateDbContext();
        var (empresaId, _) = Empresa(db);
        await db.SaveChangesAsync();
        var queries = new FluxoCaixaQueries(db);

        var umAnoDiario = await queries.FluxoBucketsAsync(empresaId, PeriodicidadeFluxo.Diario,
            new DateTime(2026, 1, 1), new DateTime(2026, 12, 31));
        var acao = () => queries.FluxoBucketsAsync(empresaId, PeriodicidadeFluxo.Diario,
            new DateTime(2025, 1, 1), new DateTime(2026, 12, 31));

        umAnoDiario.Should().HaveCount(365);
        (await acao.Should().ThrowAsync<ArgumentException>()).Which.Message.Should().Contain("Período longo demais");
    }

    // ── F7: referência inexistente ──────────────────────────────────────────

    [SkippableFact]
    public async Task F7_fornecedor_inexistente_vira_erro_de_validacao_e_nao_erro_de_banco()
    {
        Skip.If(!fixture.IsAvailable, fixture.UnavailableReason ?? "PG indisponível");
        await using var db = fixture.CreateDbContext();
        var (empresaId, cat) = Empresa(db, TipoCategoriaFinanceira.Despesa);
        await db.SaveChangesAsync();
        var uc = new CriarContaPagarUseCase(new ContaPagarRepository(db), new CategoriaFinanceiraRepository(db),
            new CentroCustoRepository(db), db, NullLogger<CriarContaPagarUseCase>.Instance,
            new FornecedorRepository(db), new LojaRepository(db));

        var acao = () => uc.ExecuteAsync(new CriarContaPagarCommand(empresaId, Guid.NewGuid(), cat, "Conta",
            DateTime.UtcNow, [new ParcelaSpec(1, 10m, DateTime.UtcNow.AddDays(1))]));

        (await acao.Should().ThrowAsync<Exception>()).Which.Should().BeOfType<UseCaseValidationException>()
            .Which.Message.Should().Be("Fornecedor não encontrado.");
    }

    // ── F12: "hoje" do filtro de status é o de Brasília ─────────────────────

    private sealed class Relogio(DateTimeOffset agora) : TimeProvider
    {
        public override DateTimeOffset GetUtcNow() => agora;
    }

    [SkippableTheory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task F12_as_23h_de_Brasilia_a_parcela_que_vence_hoje_esta_Aberta_e_nao_Vencida(bool gravadaPeloConsole)
    {
        Skip.If(!fixture.IsAvailable, fixture.UnavailableReason ?? "PG indisponível");
        await using var db = fixture.CreateDbContext();
        var (empresaId, cat) = Empresa(db);
        var venceHoje = Pagar(db, empresaId, cat, "vence 27/10", gravadaPeloConsole ? Console(10, 27) : Legado(10, 27));
        await db.SaveChangesAsync();
        // 27/10 às 23h em Brasília: em UTC já é 28/10.
        var noite = new ContaPagarRepository(db, new Relogio(DateTimeOffset.Parse("2026-10-28T02:00:00Z")));
        var diaSeguinte = new ContaPagarRepository(db, new Relogio(DateTimeOffset.Parse("2026-10-28T13:00:00Z")));

        var (vencidasNoite, _) = await noite.ListarAsync(empresaId, status: StatusContaFinanceira.Vencida);
        var (abertasNoite, _) = await noite.ListarAsync(empresaId, status: StatusContaFinanceira.Aberta);
        var (vencidasDepois, _) = await diaSeguinte.ListarAsync(empresaId, status: StatusContaFinanceira.Vencida);

        vencidasNoite.Should().NotContain(c => c.Id == venceHoje.Id);
        abertasNoite.Should().Contain(c => c.Id == venceHoje.Id);
        vencidasDepois.Should().Contain(c => c.Id == venceHoje.Id);
    }
}
