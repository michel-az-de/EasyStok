using System.Text;
using EasyStock.Api.Controllers;
using EasyStock.Application.Ports.Output;
using EasyStock.Application.Ports.Output.Persistence;
using EasyStock.Application.Ports.Output.Security;
using EasyStock.Application.UseCases.Financeiro.Pagamentos;
using FluentAssertions;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging.Abstractions;
using NSubstitute;

namespace EasyStock.Api.UnitTests.Controllers;

/// <summary>#1508: o webhook Pix não repassava o CancellationToken da requisição para a reconciliação.</summary>
public class WebhookPixControllerTests
{
    [Fact]
    public async Task Pix_RepassaOCancellationTokenDaRequisicaoParaAReconciliacao()
    {
        var contas = Substitute.For<IContaReceberRepository>();
        var reconciliar = new ReconciliarPixParcelaReceberUseCase(contas, Substitute.For<ICaixaRepository>(),
            Substitute.For<IEfiPixService>(), Substitute.For<IUnitOfWork>(),
            NullLogger<ReconciliarPixParcelaReceberUseCase>.Instance);
        var bypass = Substitute.For<IRowLevelSecurityBypass>();
        var config = new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?>
        {
            ["Efi:WebhookAllowUnsigned"] = "true",
        }).Build();
        var ambiente = Substitute.For<IWebHostEnvironment>();
        ambiente.EnvironmentName.Returns("Development");
        var http = new DefaultHttpContext();
        http.Request.Body = new MemoryStream(Encoding.UTF8.GetBytes("{\"pix\":[{\"txid\":\"cr-123\"}]}"));
        var controller = new WebhookPixController(config, reconciliar, NullLogger<WebhookPixController>.Instance, ambiente, bypass)
        {
            ControllerContext = new ControllerContext { HttpContext = http },
        };
        using var cts = new CancellationTokenSource();

        var result = await controller.Pix(cts.Token);

        result.Should().BeOfType<OkResult>();
        await contas.Received(1).GetParcelaByEfiTxidAsync("cr-123", cts.Token);
        // #2743: o webhook e anonimo; sem o bypass a policy RLS zera a busca da parcela.
        bypass.Received(1).Begin();
    }

    private const string Segredo = "segredo-do-teste";
    private const string Corpo = "{\"pix\":[{\"txid\":\"cr-123\"}]}";

    public static TheoryData<string> Recusas() => new()
    {
        "sem-assinatura", "sem-timestamp", "timestamp-nao-numerico", "timestamp-de-10-min-atras",
        "hmac-de-outro-segredo", "sem-segredo-e-sem-allow-unsigned", "allow-unsigned-em-production",
    };

    [Theory]
    [MemberData(nameof(Recusas))]
    public async Task Pix_recusado_na_assinatura_nunca_liga_o_bypass_de_RLS(string caso)
    {
        // #2763 (linha da #2765): o webhook é anônimo; quem não prova a assinatura não pode chegar ao bypass de RLS.
        var agora = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds().ToString();
        var dezMinutosAtras = DateTimeOffset.UtcNow.AddMinutes(-10).ToUnixTimeMilliseconds().ToString();
        (string? Segredo, bool AllowUnsigned, string Ambiente, string? Ts, string? Assinatura) p = caso switch
        {
            "sem-assinatura" => (Segredo, false, "Development", agora, null),
            "sem-timestamp" => (Segredo, false, "Development", null, Assinar(Segredo, agora)),
            "timestamp-nao-numerico" => (Segredo, false, "Development", "ontem", Assinar(Segredo, "ontem")),
            "timestamp-de-10-min-atras" => (Segredo, false, "Development", dezMinutosAtras, Assinar(Segredo, dezMinutosAtras)),
            "hmac-de-outro-segredo" => (Segredo, false, "Development", agora, Assinar("outro-segredo", agora)),
            "sem-segredo-e-sem-allow-unsigned" => (null, false, "Development", agora, null),
            "allow-unsigned-em-production" => (null, true, "Production", agora, null),
            _ => throw new ArgumentOutOfRangeException(nameof(caso)),
        };
        var contas = Substitute.For<IContaReceberRepository>();
        var reconciliar = new ReconciliarPixParcelaReceberUseCase(contas, Substitute.For<ICaixaRepository>(),
            Substitute.For<IEfiPixService>(), Substitute.For<IUnitOfWork>(),
            NullLogger<ReconciliarPixParcelaReceberUseCase>.Instance);
        var bypass = Substitute.For<IRowLevelSecurityBypass>();
        var config = new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?>
        {
            ["Efi:WebhookSecret"] = p.Segredo,
            ["Efi:WebhookAllowUnsigned"] = p.AllowUnsigned ? "true" : "false",
        }).Build();
        var ambiente = Substitute.For<IWebHostEnvironment>();
        ambiente.EnvironmentName.Returns(p.Ambiente);
        var http = new DefaultHttpContext();
        http.Request.Body = new MemoryStream(Encoding.UTF8.GetBytes(Corpo));
        if (p.Ts is not null) http.Request.Headers["X-Efi-Timestamp"] = p.Ts;
        if (p.Assinatura is not null) http.Request.Headers["X-Efi-Signature"] = p.Assinatura;
        var controller = new WebhookPixController(config, reconciliar, NullLogger<WebhookPixController>.Instance, ambiente, bypass)
        {
            ControllerContext = new ControllerContext { HttpContext = http },
        };

        var result = await controller.Pix(CancellationToken.None);

        result.Should().BeOfType<UnauthorizedResult>();
        bypass.DidNotReceiveWithAnyArgs().Begin();
        await contas.DidNotReceiveWithAnyArgs().GetParcelaByEfiTxidAsync(default!, default);
    }

    // Como o controller confere: HMAC-SHA256 de "{timestamp}.{corpo}", em hexadecimal minúsculo.
    private static string Assinar(string segredo, string ts)
    {
        using var hmac = new System.Security.Cryptography.HMACSHA256(Encoding.UTF8.GetBytes(segredo));
        return Convert.ToHexString(hmac.ComputeHash(Encoding.UTF8.GetBytes($"{ts}.{Corpo}"))).ToLowerInvariant();
    }
}
