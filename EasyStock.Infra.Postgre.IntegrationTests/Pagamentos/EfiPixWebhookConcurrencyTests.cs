using EasyStock.Application.Ports.Output;
using EasyStock.Application.Ports.Output.Persistence;
using EasyStock.Application.UseCases.Faturas.RegistrarPagamentoFatura;
using EasyStock.Domain.Entities;
using EasyStock.Domain.Enums;
using EasyStock.Infra.Async.Pagamentos.Webhooks;
using EasyStock.Infra.Postgre.Data;
using EasyStock.Infra.Postgre.Repositories;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using NSubstitute;

namespace EasyStock.Infra.Postgre.IntegrationTests.Pagamentos;

/// <summary>
/// Integration tests do <see cref="EfiPixWebhookProcessor"/> contra Postgres
/// real (Testcontainers). Cobertura focada no caminho do bug aberto #289 +
/// guard-rails do contrato (valor exato, subpagamento, payload invalido,
/// multiplos txids no mesmo payload).
///
/// <para>
/// O cenario <c>DoisWebhooks_simultaneos_renovam_assinatura_apenas_uma_vez</c>
/// EXPOE o bug #289 (check-then-act sem <c>SELECT FOR UPDATE</c> +
/// <c>ExecuteInTransactionAsync</c>) — fica <see cref="SkippableFactAttribute"/>
/// com <c>Skip.If(true)</c> ate que #289 seja resolvido. Quando o fix entrar,
/// remover o skip; o teste deve passar naturalmente.
/// </para>
/// </summary>
public class EfiPixWebhookConcurrencyTests(PostgreSqlDatabaseFixture fixture)
    : IClassFixture<PostgreSqlDatabaseFixture>
{
    [SkippableFact]
    [Trait("Category", "RegressionBug")]
    public async Task DoisWebhooks_simultaneos_renovam_assinatura_apenas_uma_vez()
    {
        Skip.If(!fixture.IsAvailable, fixture.UnavailableReason ?? "Docker/PostgreSQL unavailable");
        Skip.If(true,
            "Aguardando fix do bug #289 (EfiPixWebhookProcessor sem SELECT FOR UPDATE). " +
            "Remover este Skip.If quando o processor passar a usar GetByTxidComLockAsync + ExecuteInTransactionAsync.");

        var txid = $"RACE-{Guid.NewGuid():N}";
        var dataFimInicial = DateTime.UtcNow.AddDays(5);
        var (empresaId, _) = await SeedCobrancaPendenteAsync(txid, valor: 100m, dataFimAssinatura: dataFimInicial);

        var payload = BuildPayloadJson([(txid, "100.00")]);

        // Cada task abre o seu proprio escopo (DbContext + repos + processor) — espelha
        // o cenario de producao onde dois webhooks HTTP simultaneos sao processados em
        // requests independentes (cada uma com seu scope DI).
        var task1 = Task.Run(() => ProcessWithFreshScopeAsync(payload));
        var task2 = Task.Run(() => ProcessWithFreshScopeAsync(payload));
        await Task.WhenAll(task1, task2);

        await using var assertCtx = fixture.CreateDbContext();
        var cobranca = await assertCtx.CobrancasAssinatura.AsNoTracking()
            .IgnoreQueryFilters().FirstAsync(c => c.Txid == txid);
        var assinatura = await assertCtx.AssinaturasEmpresa.AsNoTracking()
            .IgnoreQueryFilters().FirstAsync(a => a.EmpresaId == empresaId);

        cobranca.Status.Should().Be(StatusCobranca.Paga, "cobranca deve transicionar para Paga exatamente uma vez");
        var diasAdicionados = (assinatura.DataFim!.Value - dataFimInicial).TotalDays;
        diasAdicionados.Should().BeApproximately(30, 0.01,
            "renovacao deve somar 30 dias UMA vez — dois webhooks paralelos rendendo +60d caracterizam o bug #289 (duplo-fire da Efi). " +
            "Sem SELECT FOR UPDATE, ambos passam o check Status=Pendente antes de qualquer commit e somam +30d em cima de DataFim antigo.");
    }

    [SkippableFact]
    public async Task Webhook_com_valor_exato_marca_cobranca_paga_e_renova_assinatura()
    {
        Skip.If(!fixture.IsAvailable, fixture.UnavailableReason ?? "Docker/PostgreSQL unavailable");
        var txid = $"EXATO-{Guid.NewGuid():N}";
        var dataFimInicial = DateTime.UtcNow.AddDays(7);
        var (empresaId, _) = await SeedCobrancaPendenteAsync(txid, valor: 100m, dataFimAssinatura: dataFimInicial);

        var falhaNotifier = Substitute.For<IFalhaPagamentoNotifier>();
        await ProcessWithFreshScopeAsync(BuildPayloadJson([(txid, "100.00")]), falhaNotifier);

        await using var assertCtx = fixture.CreateDbContext();
        var cobranca = await assertCtx.CobrancasAssinatura.AsNoTracking()
            .IgnoreQueryFilters().FirstAsync(c => c.Txid == txid);
        var assinatura = await assertCtx.AssinaturasEmpresa.AsNoTracking()
            .IgnoreQueryFilters().FirstAsync(a => a.EmpresaId == empresaId);

        cobranca.Status.Should().Be(StatusCobranca.Paga);
        cobranca.PagoEm.Should().NotBeNull();
        assinatura.DataFim.Should().BeCloseTo(dataFimInicial.AddDays(30), TimeSpan.FromSeconds(2));
        await falhaNotifier.DidNotReceiveWithAnyArgs()
            .RegistrarFalhaAsync(default, default, default!, default);
    }

    [SkippableFact]
    public async Task Webhook_com_subpagamento_nao_marca_paga_e_chama_falha_notifier()
    {
        Skip.If(!fixture.IsAvailable, fixture.UnavailableReason ?? "Docker/PostgreSQL unavailable");
        var txid = $"SUB-{Guid.NewGuid():N}";
        var (empresaId, _) = await SeedCobrancaPendenteAsync(txid, valor: 100m,
            dataFimAssinatura: DateTime.UtcNow.AddDays(3));

        var falhaNotifier = Substitute.For<IFalhaPagamentoNotifier>();
        // 98.00 < 100.00 - 0.01 → recusa do processor (linha 109 do EfiPixWebhookProcessor).
        await ProcessWithFreshScopeAsync(BuildPayloadJson([(txid, "98.00")]), falhaNotifier);

        await using var assertCtx = fixture.CreateDbContext();
        var cobranca = await assertCtx.CobrancasAssinatura.AsNoTracking()
            .IgnoreQueryFilters().FirstAsync(c => c.Txid == txid);

        cobranca.Status.Should().Be(StatusCobranca.Pendente, "subpagamento NAO deve confirmar pagamento");
        cobranca.PagoEm.Should().BeNull();
        await falhaNotifier.Received(1).RegistrarFalhaAsync(
            empresaId,
            Arg.Any<Guid?>(),
            Arg.Is<string>(m => m.Contains("Subpagamento", StringComparison.OrdinalIgnoreCase)),
            Arg.Any<CancellationToken>());
    }

    [SkippableFact]
    public async Task Payload_JSON_invalido_nao_lanca_e_nao_altera_cobrancas()
    {
        Skip.If(!fixture.IsAvailable, fixture.UnavailableReason ?? "Docker/PostgreSQL unavailable");
        var txid = $"INVAL-{Guid.NewGuid():N}";
        await SeedCobrancaPendenteAsync(txid, valor: 100m,
            dataFimAssinatura: DateTime.UtcNow.AddDays(3));

        var act = async () => await ProcessWithFreshScopeAsync("{ not really json");
        await act.Should().NotThrowAsync("processor deve tratar JSON invalido como warning + return");

        await using var assertCtx = fixture.CreateDbContext();
        var cobranca = await assertCtx.CobrancasAssinatura.AsNoTracking()
            .IgnoreQueryFilters().FirstAsync(c => c.Txid == txid);
        cobranca.Status.Should().Be(StatusCobranca.Pendente);
    }

    [SkippableFact]
    public async Task Payload_com_dois_txids_processa_ambos_independente()
    {
        Skip.If(!fixture.IsAvailable, fixture.UnavailableReason ?? "Docker/PostgreSQL unavailable");
        var txidA = $"BATCH-A-{Guid.NewGuid():N}";
        var txidB = $"BATCH-B-{Guid.NewGuid():N}";
        var (empresaId, _) = await SeedCobrancaPendenteAsync(txidA, valor: 100m,
            dataFimAssinatura: DateTime.UtcNow.AddDays(2));
        await SeedCobrancaPendenteAsync(txidB, valor: 200m, empresaIdReuse: empresaId,
            dataFimAssinatura: DateTime.UtcNow.AddDays(2));

        await ProcessWithFreshScopeAsync(BuildPayloadJson([(txidA, "100.00"), (txidB, "200.00")]));

        await using var assertCtx = fixture.CreateDbContext();
        var pagas = await assertCtx.CobrancasAssinatura.AsNoTracking()
            .IgnoreQueryFilters()
            .Where(c => c.Txid == txidA || c.Txid == txidB)
            .ToListAsync();

        pagas.Should().HaveCount(2);
        pagas.Should().OnlyContain(c => c.Status == StatusCobranca.Paga,
            "ambos os items do array pix devem ser confirmados, pois um nao bloqueia o outro");
    }

    // ─────────────────────────────────────────────────────────────────────────

    private async Task<(Guid empresaId, Guid cobrancaId)> SeedCobrancaPendenteAsync(
        string txid, decimal valor, DateTime dataFimAssinatura, Guid? empresaIdReuse = null)
    {
        // Primeira chamada do teste reseta; chamadas subsequentes (cenario batch) preservam
        // o estado seedado anteriormente. Decisao do caller via empresaIdReuse.
        var primeiroSeed = !empresaIdReuse.HasValue;
        if (primeiroSeed) await fixture.ResetDatabaseAsync();

        await using var ctx = fixture.CreateDbContext();
        var empresaId = empresaIdReuse ?? Guid.NewGuid();
        var cobrancaId = Guid.NewGuid();
        var assinaturaId = Guid.NewGuid();
        var now = DateTime.UtcNow;

        if (primeiroSeed)
        {
            var planoId = Guid.NewGuid();
            ctx.Empresas.Add(new Empresa
            {
                Id = empresaId,
                Nome = "Empresa Teste EfiPix",
                Documento = empresaId.ToString("N")[..14],
                CriadoEm = now,
                AlteradoEm = now,
            });
            ctx.Planos.Add(new Plano
            {
                Id = planoId,
                Nome = "Plano Teste",
                PrecoMensal = 100m,
                LimiteLojas = 1,
                LimiteUsuarios = 5,
                LimiteProdutos = 1000,
                LimiteGeracoesIaMensais = 0,
                Ativo = true,
                CriadoEm = now,
            });
            ctx.AssinaturasEmpresa.Add(new AssinaturaEmpresa
            {
                Id = assinaturaId,
                EmpresaId = empresaId,
                PlanoId = planoId,
                DataInicio = now.AddDays(-25),
                DataFim = dataFimAssinatura,
                Status = StatusAssinatura.Ativa,
                CriadoEm = now.AddDays(-25),
                AlteradoEm = now.AddDays(-25),
            });
        }
        else
        {
            assinaturaId = await ctx.AssinaturasEmpresa.IgnoreQueryFilters()
                .Where(a => a.EmpresaId == empresaId).Select(a => a.Id).FirstAsync();
        }

        ctx.CobrancasAssinatura.Add(new CobrancaAssinatura
        {
            Id = cobrancaId,
            EmpresaId = empresaId,
            AssinaturaId = assinaturaId,
            Txid = txid,
            Valor = valor,
            PixCopiaCola = "00020126...test",
            QrCodeBase64 = "test-qr",
            Status = StatusCobranca.Pendente,
            CriadoEm = now,
            ExpiracaoEm = now.AddHours(1),
        });
        await ctx.SaveChangesAsync();

        return (empresaId, cobrancaId);
    }

    private async Task ProcessWithFreshScopeAsync(string payload, IFalhaPagamentoNotifier? falhaNotifier = null)
    {
        await using var ctx = fixture.CreateDbContext();
        var cobrancaRepo = new CobrancaAssinaturaRepository(ctx);
        var assinaturaRepo = new AssinaturaEmpresaRepository(ctx);

        // RegistrarPagamentoFaturaUseCase so e invocado quando cobranca.FaturaId != null.
        // Todas as cobrancas seedadas aqui nascem com FaturaId == null, entao o UC fica
        // construido mas inerte — IFaturaRepository substituto + ctx como UoW chega.
        var faturaRepo = Substitute.For<IFaturaRepository>();
        var registrarPagamentoUc = new RegistrarPagamentoFaturaUseCase(
            faturaRepo, ctx, NullLogger<RegistrarPagamentoFaturaUseCase>.Instance);

        var notifier = falhaNotifier ?? Substitute.For<IFalhaPagamentoNotifier>();
        var processor = new EfiPixWebhookProcessor(
            cobrancaRepo, assinaturaRepo, ctx,
            registrarPagamentoUc, notifier,
            NullLogger<EfiPixWebhookProcessor>.Instance);

        await processor.ProcessarAsync(payload, new Dictionary<string, string?>());
    }

    private static string BuildPayloadJson(IEnumerable<(string txid, string valor)> items)
    {
        var entries = string.Join(",", items.Select(it =>
            $"{{\"txid\":\"{it.txid}\",\"valor\":\"{it.valor}\"}}"));
        return $"{{\"pix\":[{entries}]}}";
    }
}
