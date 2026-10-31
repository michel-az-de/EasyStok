using System.Net.Http.Headers;
using EasyStock.Application.Ports.Output.Atendimento;
using EasyStock.Infra.Integrations.WhatsApp;
using EasyStock.Infra.Integrations.WhatsApp.Waha;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Options;

namespace EasyStock.Infra.Integrations.DependencyInjection;

/// <summary>
/// Registra <see cref="IWhatsAppCloudClient"/>: <see cref="WhatsAppCloudClient"/> real (HttpClient
/// nomeado "whatsapp-cloud", bearer + base address da Meta) quando
/// <c>Atendimento:WhatsApp:Cliente=meta</c> (#1102) ou <c>Notifications:WhatsApp:Provider=meta</c>
/// (o switch antigo), senão <see cref="StubWhatsAppCloudClient"/>. A chave do atendimento liga o
/// cliente real sem trocar o provider de notificações. Com <c>Atendimento:WhatsApp:Cliente=waha</c> o atendimento
/// sai pela ponte provisória do WAHA (#2072), e o número de plataforma continua na Meta.
/// </summary>
public static class WhatsAppCloudClientServiceCollectionExtensions
{
    public static IServiceCollection AddEasyStockWhatsAppCloudClient(
        this IServiceCollection services, IConfiguration configuration)
    {
        services.Configure<WhatsAppCloudOptions>(configuration.GetSection("Notifications:WhatsApp:Meta"));
        // N6: o número de plataforma vem da própria seção; o cliente o usa só nos métodos de plataforma.
        services.PostConfigure<WhatsAppCloudOptions>(o =>
            o.PhoneNumberIdPlataforma = configuration["Notifications:WhatsApp:Plataforma:PhoneNumberId"]?.Trim() ?? string.Empty);

        services.AddHttpClient<WhatsAppCloudClient>("whatsapp-cloud", (sp, client) =>
        {
            var opts = sp.GetRequiredService<IOptions<WhatsAppCloudOptions>>().Value;
            client.BaseAddress = new Uri(opts.BaseUrlEfetiva.TrimEnd('/') + "/");
            if (!string.IsNullOrWhiteSpace(opts.AccessToken))
                client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", opts.AccessToken);
        });

        // #1417: Embedded Signup (coexistência). Sem Bearer padrão: cada chamada leva o business token da empresa.
        services.AddHttpClient<IMetaEmbeddedSignupClient, MetaEmbeddedSignupClient>(MetaEmbeddedSignupClient.NomeHttpClient, (sp, client) =>
        {
            var opts = sp.GetRequiredService<IOptions<WhatsAppCloudOptions>>().Value;
            client.BaseAddress = new Uri(opts.BaseUrlEfetiva.TrimEnd('/') + "/");
            client.Timeout = TimeSpan.FromSeconds(20);
        });

        services.AddScoped<StubWhatsAppCloudClient>();
        // #2155: foto do perfil pelas pontes, num HttpClient sem a chave delas (o endereço é do CDN do WhatsApp).
        services.AddHttpClient<BaixadorFotoPerfilWhatsApp>(BaixadorFotoPerfilWhatsApp.HttpClientNome,
            client => client.Timeout = TimeSpan.FromSeconds(15));
        services.Configure<WppConnectOptions>(configuration.GetSection("Atendimento:WppConnect"));
        services.AddHttpClient<WppConnectClient>((sp, client) =>
        {
            var opts = sp.GetRequiredService<IOptions<WppConnectOptions>>().Value;
            client.BaseAddress = new Uri(opts.BaseUrl.TrimEnd('/') + "/");
            client.Timeout = TimeSpan.FromSeconds(30);
        }).RemoveAllLoggers(); // generate-token carrega o segredo no caminho: nunca registrar a URL.
        services.AddScoped<IWppConnectSessaoClient>(sp => sp.GetRequiredService<WppConnectClient>());

        services.AddHttpClient<CatalogoModelosWhatsApp>((sp, client) =>
        {
            var opts = sp.GetRequiredService<IOptions<WhatsAppCloudOptions>>().Value;
            client.BaseAddress = new Uri(opts.BaseUrlEfetiva.TrimEnd('/') + "/");
            client.Timeout = TimeSpan.FromSeconds(20);
        });
        // #2072 (ADR-0062): ponte provisória pelo WAHA. As peças ficam registradas sempre, porque o webhook do WAHA
        // depende do tradutor; só valem com Atendimento:WhatsApp:Cliente=waha, que vence a Meta no atendimento.
        var ponteWaha = UsaPonteWaha(configuration[ChaveClienteAtendimento]);
        GarantirUmaPonteSo(ponteWaha, configuration.GetValue<bool>(ChaveWppConnectHabilitado));
        services.Configure<WahaOptions>(configuration.GetSection(WahaOptions.Secao));
        services.PostConfigure<WahaOptions>(o => o.Ligada = ponteWaha);
        services.TryAddSingleton(TimeProvider.System);
        services.AddHttpClient<WahaWhatsAppClient>(ConfigurarHttpWaha);
        services.AddHttpClient<ResolvedorContatoWaha>(ConfigurarHttpWaha);
        services.AddScoped<MenuNumeradoWaha>();
        services.AddScoped<TradutorEventoWaha>();

        var clienteMeta = UsaClienteMeta(configuration[ChaveClienteAtendimento], configuration[ChaveProviderNotificacoes]);
        services.AddScoped<ICatalogoModelosAtendimento>(sp =>
            clienteMeta && !ponteWaha ? sp.GetRequiredService<CatalogoModelosWhatsApp>() : new CatalogoModelosIndisponivel());

        if (ponteWaha)
            services.AddScoped<IWhatsAppCloudClient>(sp => sp.GetRequiredService<WahaWhatsAppClient>());
        else if (clienteMeta)
            services.AddScoped<IWhatsAppCloudClient>(sp => sp.GetRequiredService<WhatsAppCloudClient>());
        else
            services.AddScoped<IWhatsAppCloudClient>(sp => sp.GetRequiredService<StubWhatsAppCloudClient>());

        if (configuration.GetValue<bool>("Atendimento:WppConnect:Habilitado"))
            services.AddScoped<IWhatsAppCloudClient, RoteadorWhatsAppClient>();

        // N6: WhatsApp de plataforma. Mesma classe (mesmo HttpClient, token e pipeline sem retry), métodos que não usam
        // o remetente do tenant. Registrado sempre: só o provider de plataforma o consome, e só com Provider=meta.
        services.AddScoped<EasyStock.Application.Ports.Output.Notifications.IClienteWhatsAppPlataforma>(
            sp => sp.GetRequiredService<WhatsAppCloudClient>());

        // Porta de canal (S34, ADR-0051): o WhatsApp e um dos adaptadores que o ResolvedorCanal escolhe. Na ponte, o
        // adaptador avisa que não tem modelo aprovado (#2072).
        if (ponteWaha)
            services.AddScoped<ICanalMensageria, CanalWhatsAppPonte>();
        else
            services.AddScoped<ICanalMensageria, CanalWhatsApp>();

        return services;
    }

    private static void ConfigurarHttpWaha(IServiceProvider sp, HttpClient client)
    {
        var opts = sp.GetRequiredService<IOptions<WahaOptions>>().Value;
        if (Uri.TryCreate(opts.BaseUrl.Trim().TrimEnd('/') + "/", UriKind.Absolute, out var endereco))
            client.BaseAddress = endereco;
        if (!string.IsNullOrWhiteSpace(opts.ApiKey))
            client.DefaultRequestHeaders.Add("X-Api-Key", opts.ApiKey.Trim());
        client.Timeout = TimeSpan.FromSeconds(Math.Max(5, opts.TimeoutSegundos));
    }

    /// <summary>Chave própria do cliente do atendimento: <c>meta</c>, <c>waha</c> (ponte provisória, #2072) ou <c>stub</c>.</summary>
    public const string ChaveClienteAtendimento = "Atendimento:WhatsApp:Cliente";

    /// <summary>Chave que liga a conexão provisória pelo WPPConnect (#2071).</summary>
    public const string ChaveWppConnectHabilitado = "Atendimento:WppConnect:Habilitado";

    /// <summary>
    /// WAHA e WPPConnect são pontes alternativas para o mesmo número: ligadas juntas, cada mensagem do cliente chegaria
    /// duas vezes com ids diferentes e as respostas sairiam por uma só. A API e o Worker não sobem com as duas.
    /// </summary>
    public static void GarantirUmaPonteSo(bool waha, bool wppConnect)
    {
        if (waha && wppConnect)
            throw new InvalidOperationException(
                "Atendimento:WhatsApp:Cliente=waha and Atendimento:WppConnect:Habilitado=true cannot be on together: " +
                "turn on only one WhatsApp bridge (docs/dev/whatsapp-ponte-waha.md).");
    }

    /// <summary>Ponte provisória pelo WAHA (#2072): só com <c>Atendimento:WhatsApp:Cliente=waha</c>.</summary>
    public static bool UsaPonteWaha(string? cliente)
        => string.Equals(cliente?.Trim(), "waha", StringComparison.OrdinalIgnoreCase);

    /// <summary>Chave antiga, do provider de notificações; com <c>meta</c> também leva o cliente real.</summary>
    public const string ChaveProviderNotificacoes = "Notifications:WhatsApp:Provider";

    /// <summary>
    /// Núcleo puro da escolha real/stub: real quando <paramref name="cliente"/> é <c>meta</c> OU
    /// quando <paramref name="provider"/> é <c>meta</c>. O segundo caso não é só fallback: o
    /// <c>MetaCloudWhatsAppProvider</c> das notificações envia por este mesmo cliente, então
    /// <c>Cliente=stub</c> com <c>Provider=meta</c> engoliria as notificações em silêncio. Mesma
    /// regra que o <c>StartupHardening</c> usa para exigir as credenciais da Meta.
    /// </summary>
    public static bool UsaClienteMeta(string? cliente, string? provider)
        => EhMeta(cliente) || EhMeta(provider);

    private static bool EhMeta(string? valor)
        => string.Equals(valor?.Trim(), "meta", StringComparison.OrdinalIgnoreCase);
}
