using System.Security.Cryptography;
using System.Text;
using EasyStock.Api.Controllers;
using EasyStock.Application.Ports.Output;
using EasyStock.Application.Ports.Output.Persistence;
using EasyStock.Application.UseCases.Financeiro.Pagamentos;
using EasyStock.Domain.Entities;
using EasyStock.Domain.Enums;
using FluentAssertions;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging.Abstractions;
using NSubstitute;

namespace EasyStock.Infra.Postgre.IntegrationTests.Pagamentos;

/// <summary>
/// Webhook Pix E2E contra Postgres real: valida camadas do controller
/// (assinatura HMAC, replay protection) + processor (valor parcial, sobrepagamento,
/// idempotencia). Diferente dos testes de processor isolado: estes exercitam o
/// <see cref="WebhookPixController.Pix"/> com <see cref="DefaultHttpContext"/>
/// e dependencias reais (repos contra DB).
/// </summary>
public class WebhookPixControllerE2ETests(PostgreSqlDatabaseFixture fixture)
    : IClassFixture<PostgreSqlDatabaseFixture>
{
    private const string WebhookSecret = "segredo-super-secreto-teste-42";

    [SkippableFact]
    public async Task Txid_de_cobranca_de_assinatura_antiga_responde_200_sem_alterar_nada()
    {
        // Poda P02: a cobranca de assinatura SaaS saiu. Txid antigo (sem prefixo "cr")
        // responde 200 para o Efi parar de retentar, sem excecao e sem renovar nada.
        Skip.If(!fixture.IsAvailable, fixture.UnavailableReason ?? "Docker/PostgreSQL unavailable");
        await fixture.ResetDatabaseAsync();

        var txid = $"E2E-{Guid.NewGuid():N}";
        var dataFimInicial = DateTime.UtcNow.AddDays(5);
        var (empresaId, _) = await SeedAsync(txid, valor: 100m, dataFimAssinatura: dataFimInicial);

        var payload = BuildPayloadJson([(txid, "100.00")]);
        var (timestamp, signature) = GerarAssinaturaEfi(payload, WebhookSecret);

        var controller = CriarController();
        ConfigurarRequest(controller, payload, timestamp, signature);

        var result = await controller.Pix(CancellationToken.None);
        result.Should().BeOfType<OkResult>();

        await using var assert = fixture.CreateDbContext();
        var cobranca = await assert.CobrancasAssinatura.AsNoTracking()
            .IgnoreQueryFilters().FirstAsync(c => c.Txid == txid);
        var assinatura = await assert.AssinaturasEmpresa.AsNoTracking()
            .IgnoreQueryFilters().FirstAsync(a => a.EmpresaId == empresaId);

        cobranca.Status.Should().Be(StatusCobranca.Pendente);
        assinatura.DataFim.Should().BeCloseTo(dataFimInicial, TimeSpan.FromSeconds(2));
    }

    [SkippableFact]
    public async Task Assinatura_invalida_retorna_401()
    {
        Skip.If(!fixture.IsAvailable, fixture.UnavailableReason ?? "Docker/PostgreSQL unavailable");
        await fixture.ResetDatabaseAsync();

        var txid = $"E2E-{Guid.NewGuid():N}";
        await SeedAsync(txid, valor: 100m, dataFimAssinatura: DateTime.UtcNow.AddDays(5));

        var payload = BuildPayloadJson([(txid, "100.00")]);

        var controller = CriarController();
        // Assinatura errada (outro secret)
        var (timestamp, signature) = GerarAssinaturaEfi(payload, "outro-secret");
        ConfigurarRequest(controller, payload, timestamp, signature);

        var result = await controller.Pix(CancellationToken.None);
        result.Should().BeOfType<UnauthorizedResult>();

        await using var assert = fixture.CreateDbContext();
        var cobranca = await assert.CobrancasAssinatura.AsNoTracking()
            .IgnoreQueryFilters().FirstAsync(c => c.Txid == txid);
        cobranca.Status.Should().Be(StatusCobranca.Pendente, "assinatura invalida nao deve processar nada");
    }

    [SkippableFact]
    public async Task Replay_timestamp_fora_da_janela_retorna_401()
    {
        Skip.If(!fixture.IsAvailable, fixture.UnavailableReason ?? "Docker/PostgreSQL unavailable");
        await fixture.ResetDatabaseAsync();

        var txid = $"E2E-{Guid.NewGuid():N}";
        await SeedAsync(txid, valor: 100m, dataFimAssinatura: DateTime.UtcNow.AddDays(5));

        var payload = BuildPayloadJson([(txid, "100.00")]);

        // Timestamp de 10 minutos atras (fora da janela de 5 min)
        var oldTimestamp = DateTimeOffset.UtcNow.AddMinutes(-10).ToUnixTimeMilliseconds().ToString();
        var (_, signature) = GerarAssinaturaEfi(payload, WebhookSecret, oldTimestamp);

        var controller = CriarController();
        ConfigurarRequest(controller, payload, oldTimestamp, signature);

        var result = await controller.Pix(CancellationToken.None);
        result.Should().BeOfType<UnauthorizedResult>();
    }

    // ─────────────────────────────────────────────────────────────────────────

    private async Task<(Guid empresaId, Guid cobrancaId)> SeedAsync(
        string txid, decimal valor, DateTime dataFimAssinatura)
    {
        await using var ctx = fixture.CreateDbContext();
        var empresaId = Guid.NewGuid();
        var cobrancaId = Guid.NewGuid();
        var assinaturaId = Guid.NewGuid();
        var planoId = Guid.NewGuid();
        var now = DateTime.UtcNow;

        ctx.Empresas.Add(new Empresa
        {
            Id = empresaId,
            Nome = "Empresa Teste Webhook Pix",
            Documento = empresaId.ToString("N")[..14],
            CriadoEm = now,
            AlteradoEm = now,
        });

        ctx.Planos.Add(new Plano
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

    private WebhookPixController CriarController()
    {
        var config = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["Efi:WebhookSecret"] = WebhookSecret,
                ["Efi:WebhookAllowUnsigned"] = "false",
            })
            .Build();

        var env = Substitute.For<IWebHostEnvironment>();
        env.EnvironmentName.Returns("Development");

        var ctx = fixture.CreateDbContext();

        var reconciliarPixUc = new ReconciliarPixParcelaReceberUseCase(
            Substitute.For<IContaReceberRepository>(),
            Substitute.For<ICaixaRepository>(),
            Substitute.For<IEfiPixService>(),
            ctx,
            NullLogger<ReconciliarPixParcelaReceberUseCase>.Instance);

        return new WebhookPixController(
            config,
            reconciliarPixUc,
            NullLogger<WebhookPixController>.Instance,
            env);
    }

    private static void ConfigurarRequest(WebhookPixController controller, string body, string timestamp, string signature)
    {
        var httpCtx = new DefaultHttpContext();
        httpCtx.Request.Body = new MemoryStream(Encoding.UTF8.GetBytes(body));
        httpCtx.Request.Headers["X-Efi-Signature"] = signature;
        httpCtx.Request.Headers["X-Efi-Timestamp"] = timestamp;
        httpCtx.Request.ContentType = "application/json";

        controller.ControllerContext = new ControllerContext { HttpContext = httpCtx };
    }

    private static (string timestamp, string signature) GerarAssinaturaEfi(string body, string secret, string? timestamp = null)
    {
        var ts = timestamp ?? DateTimeOffset.UtcNow.ToUnixTimeMilliseconds().ToString();
        var toSign = $"{ts}.{body}";

        using var hmac = new HMACSHA256(Encoding.UTF8.GetBytes(secret));
        var hash = hmac.ComputeHash(Encoding.UTF8.GetBytes(toSign));
        var sig = Convert.ToHexString(hash).ToLowerInvariant();

        return (ts, sig);
    }

    private static string BuildPayloadJson(IEnumerable<(string txid, string valor)> items)
    {
        var entries = string.Join(",", items.Select(it =>
            $"{{\"txid\":\"{it.txid}\",\"valor\":\"{it.valor}\"}}"));
        return $"{{\"pix\":[{entries}]}}";
    }
}
