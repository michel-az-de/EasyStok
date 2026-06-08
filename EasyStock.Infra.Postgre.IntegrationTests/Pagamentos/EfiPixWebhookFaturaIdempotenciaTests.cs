using EasyStock.Application.Ports.Output;
using EasyStock.Application.UseCases.Faturas.RegistrarPagamentoFatura;
using EasyStock.Domain.Entities;
using EasyStock.Domain.Enums;
using EasyStock.Infra.Async.Pagamentos.Webhooks;
using EasyStock.Infra.Postgre.IntegrationTests.Faturas;
using EasyStock.Infra.Postgre.Repositories;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using NSubstitute;

namespace EasyStock.Infra.Postgre.IntegrationTests.Pagamentos;

/// <summary>
/// Idempotencia do webhook Pix no nivel CORRETO (o processor, que segura o lock +
/// status-check), nao no use case nu. Dois webhooks com o mesmo txid devem gerar
/// exatamente UM FaturaPagamento Confirmado — o 2o para no check
/// <c>cobranca.Status != Pendente</c> ([EfiPixWebhookProcessor]). Usa o
/// FaturaRepository REAL (diferente do mock do EfiPixWebhookConcurrencyTests).
/// </summary>
[Collection("PostgreSqlTestCollection")]
public sealed class EfiPixWebhookFaturaIdempotenciaTests(PostgreSqlDatabaseFixture fixture)
{
    [SkippableFact]
    public async Task Dois_webhooks_mesmo_txid_geram_apenas_um_pagamento_confirmado()
    {
        Skip.If(!fixture.IsAvailable, fixture.UnavailableReason ?? "Docker/PostgreSQL unavailable");

        var empresaId = Guid.NewGuid();
        var txid = $"IDEM-{Guid.NewGuid():N}";
        var faturaId = await SeedCobrancaComFaturaAsync(empresaId, txid, valor: 100m);

        var payload = $"{{\"pix\":[{{\"txid\":\"{txid}\",\"valor\":\"100.00\"}}]}}";
        await ProcessRealAsync(payload);
        await ProcessRealAsync(payload); // retry do PSP — deve ser idempotente

        await using var assert = fixture.CreateDbContext();
        var confirmados = await assert.FaturaPagamentos.AsNoTracking().IgnoreQueryFilters()
            .Where(p => p.FaturaId == faturaId && p.Status == StatusFaturaPagamento.Confirmado)
            .ToListAsync();

        confirmados.Should().HaveCount(1,
            "o status-check da cobranca (Paga) barra o 2o webhook — sem FaturaPagamento duplicado");
    }

    private async Task<Guid> SeedCobrancaComFaturaAsync(Guid empresaId, string txid, decimal valor)
    {
        await using var seed = fixture.CreateDbContext();
        seed.SetMobileTenantContext(empresaId);
        var now = DateTime.UtcNow;

        seed.Empresas.Add(FaturaTestSeed.Empresa(empresaId));

        var fatura = FaturaTestSeed.FaturaEmitida(empresaId, valor);
        seed.Faturas.Add(fatura);

        var planoId = Guid.NewGuid();
        seed.Planos.Add(new Plano
        {
            Id = planoId,
            Nome = "Plano Teste",
            PrecoMensal = valor,
            LimiteLojas = 1,
            LimiteUsuarios = 5,
            LimiteProdutos = 1000,
            LimiteGeracoesIaMensais = 0,
            Ativo = true,
            CriadoEm = now,
        });

        var assinaturaId = Guid.NewGuid();
        seed.AssinaturasEmpresa.Add(new AssinaturaEmpresa
        {
            Id = assinaturaId,
            EmpresaId = empresaId,
            PlanoId = planoId,
            DataInicio = now.AddDays(-10),
            DataFim = now.AddDays(20),
            Status = StatusAssinatura.Ativa,
            CriadoEm = now.AddDays(-10),
            AlteradoEm = now.AddDays(-10),
        });

        seed.CobrancasAssinatura.Add(new CobrancaAssinatura
        {
            Id = Guid.NewGuid(),
            EmpresaId = empresaId,
            AssinaturaId = assinaturaId,
            FaturaId = fatura.Id,
            Txid = txid,
            Valor = valor,
            PixCopiaCola = "00020126...test",
            QrCodeBase64 = "test-qr",
            Status = StatusCobranca.Pendente,
            CriadoEm = now,
            ExpiracaoEm = now.AddHours(1),
        });

        await seed.SaveChangesAsync();
        return fatura.Id;
    }

    private async Task ProcessRealAsync(string payload)
    {
        await using var ctx = fixture.CreateDbContext();
        var processor = new EfiPixWebhookProcessor(
            new CobrancaAssinaturaRepository(ctx),
            new AssinaturaEmpresaRepository(ctx),
            ctx,
            new RegistrarPagamentoFaturaUseCase(
                new FaturaRepository(ctx), ctx, NullLogger<RegistrarPagamentoFaturaUseCase>.Instance),
            Substitute.For<IFalhaPagamentoNotifier>(),
            NullLogger<EfiPixWebhookProcessor>.Instance);

        await processor.ProcessarAsync(payload, new Dictionary<string, string?>());
    }
}
