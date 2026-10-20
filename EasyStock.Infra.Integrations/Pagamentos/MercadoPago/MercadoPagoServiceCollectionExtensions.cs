using EasyStock.Application.Ports.Output.Pagamentos;
using Microsoft.Extensions.Configuration;
using EasyStock.Infra.Integrations.Simulacao;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace EasyStock.Infra.Integrations.Pagamentos.MercadoPago;

public static class MercadoPagoServiceCollectionExtensions
{
    private const string HostDeProducao = "api.mercadopago.com";

    /// <summary>
    /// Registra <see cref="IMercadoPagoClient"/> (I3.1, #1667). Três estados, nenhum silencioso:
    /// simulador (<c>MercadoPago:UseStub=true</c>, nunca em Production), cliente real (há
    /// <c>MercadoPago:AccessToken</c>) ou não configurado. Em Production a subida falha com simulador,
    /// com token <c>TEST-</c> ou com <c>BaseUrl</c> que não seja <c>https://api.mercadopago.com</c>.
    /// Com credencial real (não <c>TEST-</c>), em qualquer ambiente, <c>NotificationUrl</c> e <c>BackUrlSuccess</c>
    /// são obrigatórias (#1724-8): sem elas nenhum webhook chega e o link nunca baixa sozinho.
    /// </summary>
    /// <param name="ambiente">Nome do ambiente do host; vazio vale Production (falha fechado).</param>
    public static IServiceCollection AddMercadoPagoClient(
        this IServiceCollection services,
        IConfiguration configuration,
        string? ambiente)
    {
        var producao = FabricaDeSimulador.EmProducao(ambiente);
        var useStub = configuration.GetValue<bool>("MercadoPago:UseStub");
        var token = configuration["MercadoPago:AccessToken"];
        var baseUrl = string.IsNullOrWhiteSpace(configuration["MercadoPago:BaseUrl"])
            ? "https://api.mercadopago.com/"
            : configuration["MercadoPago:BaseUrl"]!.Trim();

        if (producao)
            RecusarConfiguracaoDeTeste(useStub, token, baseUrl);

        if (useStub)
        {
            services.AddScoped<IMercadoPagoClient>(sp => FabricaDeSimulador.Criar(
                ambiente,
                () => new StubMercadoPagoClient(sp.GetRequiredService<ILogger<StubMercadoPagoClient>>())));
        }
        else if (string.IsNullOrWhiteSpace(token))
        {
            services.AddScoped<IMercadoPagoClient, MercadoPagoNaoConfiguradoClient>();
        }
        else
        {
            ExigirUrlsDaCredencialReal(configuration, token);
            services.Configure<MercadoPagoOptions>(configuration.GetSection(MercadoPagoOptions.Section));

            // S32: registra a PORTA como client tipado. O antigo AddHttpClient<MercadoPagoClient> +
            // AddScoped<IMercadoPagoClient, MercadoPagoClient> resolvia a porta com um HttpClient sem BaseAddress.
            // Sem handler de retry: escrita no provedor não repete sozinha (invariante 12).
            services.AddHttpClient<IMercadoPagoClient, MercadoPagoClient>(client =>
            {
                client.BaseAddress = new Uri(baseUrl);
                client.Timeout = TimeSpan.FromSeconds(10); // timeout externo de segurança
            });
        }
        // S27: estorno da ocorrência sobre o EstornarAsync (S32).
        services.AddScoped<IEstornoPedidoGateway, MercadoPagoEstornoPedidoGateway>();

        return services;
    }

    /// <summary>
    /// #1724-8: com credencial real o Mercado Pago precisa saber para onde mandar o webhook e para onde devolver o
    /// cliente (<c>auto_return=approved</c>). Sem as URLs a API subia calada e o pagamento aprovado ficava pendente.
    /// Credencial <c>TEST-</c> (sandbox local) não exige: o fluxo é conferido pela consulta ao provedor.
    /// </summary>
    private static void ExigirUrlsDaCredencialReal(IConfiguration configuration, string token)
    {
        if (token.TrimStart().StartsWith("TEST-", StringComparison.OrdinalIgnoreCase)) return;
        if (string.IsNullOrWhiteSpace(configuration["MercadoPago:NotificationUrl"]))
            throw new InvalidOperationException(
                "MercadoPago:NotificationUrl é obrigatória com credencial de produção: sem ela nenhum webhook chega e o link nunca baixa sozinho. Defina MercadoPago__NotificationUrl (https://<host da API>/api/webhooks/mercadopago).");
        if (string.IsNullOrWhiteSpace(configuration["MercadoPago:BackUrlSuccess"]))
            throw new InvalidOperationException(
                "MercadoPago:BackUrlSuccess é obrigatória com credencial de produção: o retorno automático do Checkout Pro precisa de uma página de destino. Defina MercadoPago__BackUrlSuccess.");
    }

    /// <summary>Mensagens sem o valor do token: a exceção de subida vai para o log.</summary>
    private static void RecusarConfiguracaoDeTeste(bool useStub, string? token, string baseUrl)
    {
        if (useStub)
            throw new InvalidOperationException(
                "MercadoPago:UseStub=true não é permitido em Production: o simulador não cobra nem estorna de verdade. Remova a flag.");

        if (token is not null && token.TrimStart().StartsWith("TEST-", StringComparison.OrdinalIgnoreCase))
            throw new InvalidOperationException(
                "MercadoPago:AccessToken é uma credencial TEST- e não é permitida em Production. Use a credencial de produção (APP_USR).");

        if (!Uri.TryCreate(baseUrl, UriKind.Absolute, out var url)
            || url.Scheme != Uri.UriSchemeHttps
            || !string.Equals(url.Host, HostDeProducao, StringComparison.OrdinalIgnoreCase))
            throw new InvalidOperationException(
                $"MercadoPago:BaseUrl precisa ser https://{HostDeProducao}/ em Production.");
    }
}
