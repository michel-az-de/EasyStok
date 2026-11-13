using System.Reflection;
using EasyStock.Api.Controllers;
using EasyStock.Api.Controllers.Webhooks;
using FluentAssertions;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Filters;
using Microsoft.AspNetCore.RateLimiting;
using Microsoft.Extensions.DependencyInjection;

namespace EasyStock.Api.UnitTests.Controllers;

/// <summary>
/// #2280: webhook é anônimo e lê o corpo inteiro para conferir a assinatura. Sem teto, o Kestrel aceita 30 MB por
/// requisição antes de qualquer HMAC; o WPPConnect já tinha 2 MB, os outros não.
/// </summary>
public class WebhooksLimiteCorpoTests
{
    private const long TetoBytes = 2_000_000;

    public static TheoryData<string> PostsDeWebhook()
    {
        var dados = new TheoryData<string>();
        foreach (var acao in Acoes()) dados.Add($"{acao.DeclaringType!.Name}.{acao.Name}");
        return dados;
    }

    // #2401: os webhooks de pagamento (gateway e Pix) ficam fora do namespace Webhooks e tinham o mesmo furo.
    private static IEnumerable<MethodInfo> Acoes() =>
        typeof(WebhookWhatsAppController).Assembly.GetTypes()
            .Where(t => (t.Namespace == typeof(WebhookWhatsAppController).Namespace
                    || t == typeof(WebhookGatewayController) || t == typeof(WebhookPixController))
                && typeof(ControllerBase).IsAssignableFrom(t))
            .SelectMany(t => t.GetMethods(BindingFlags.Public | BindingFlags.Instance | BindingFlags.DeclaredOnly))
            .Where(m => m.GetCustomAttribute<HttpPostAttribute>() is not null);

    [Fact]
    public void HaWebhooksParaConferir() => Acoes().Should().HaveCountGreaterThanOrEqualTo(8);

    [Theory]
    [MemberData(nameof(PostsDeWebhook))]
    public void PostDeWebhook_LimitaOCorpoEmAte2MB(string acaoNome)
    {
        var acao = Acoes().Single(a => $"{a.DeclaringType!.Name}.{a.Name}" == acaoNome);

        var limite = acao.GetCustomAttribute<RequestSizeLimitAttribute>()
            ?? acao.DeclaringType!.GetCustomAttribute<RequestSizeLimitAttribute>();

        limite.Should().NotBeNull($"{acaoNome} é anônimo e, sem teto, o Kestrel aceita 30 MB antes da assinatura");
        BytesDoFiltro(limite!).Should().BeInRange(1, TetoBytes);
    }

    [Theory]
    [MemberData(nameof(PostsDeWebhook))]
    public void PostDeWebhook_TemRateLimit(string acaoNome)
    {
        var acao = Acoes().Single(a => $"{a.DeclaringType!.Name}.{a.Name}" == acaoNome);

        var rate = acao.GetCustomAttribute<EnableRateLimitingAttribute>()
            ?? acao.DeclaringType!.GetCustomAttribute<EnableRateLimitingAttribute>();

        rate.Should().NotBeNull($"{acaoNome} é anônimo e precisa da política webhook-meta");
        rate!.PolicyName.Should().Be("webhook-meta");
    }

    /// <summary>O atributo não expõe o valor e o filtro é interno: lê o `Bytes` do filtro que o atributo cria para a requisição.</summary>
    private static long BytesDoFiltro(RequestSizeLimitAttribute atributo)
    {
        var tipoDoFiltro = typeof(RequestSizeLimitAttribute).Assembly.GetType("Microsoft.AspNetCore.Mvc.Filters.RequestSizeLimitFilter")!;
        var servicos = new ServiceCollection().AddLogging().AddTransient(tipoDoFiltro).BuildServiceProvider();
        var filtro = ((IFilterFactory)atributo).CreateInstance(servicos);
        return Convert.ToInt64(tipoDoFiltro.GetProperty("Bytes", BindingFlags.Public | BindingFlags.Instance)!.GetValue(filtro));
    }
}
