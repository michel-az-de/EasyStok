using EasyStock.Application.Ports.Output.Ai;
using EasyStock.Application.Ports.Output.Integration.Crypto;
using EasyStock.Application.Ports.Output.Persistence;
using EasyStock.Application.Services.Atendimento;
using EasyStock.Application.Services.Ia;
using Microsoft.Extensions.Logging;
using EasyStock.Infra.Integrations.Ia;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;

namespace EasyStock.Infra.Integrations.DependencyInjection;

/// <summary>
/// Registra <see cref="IAgenteLlmClient"/> (S06). Sempre o cliente real: com
/// <c>Anthropic:Enabled=false</c>, <c>Anthropic:AgenteAtendimentoEnabled=false</c> ou sem
/// <c>Anthropic:ApiKey</c> ele fica <see cref="IAgenteLlmClient.Disponivel"/>=false e o turno não chama o LLM.
/// </summary>
public static class AgenteLlmServiceCollectionExtensions
{
    public static IServiceCollection AddEasyStockAgenteLlm(this IServiceCollection services, IConfiguration configuration)
    {
        services.Configure<AnthropicAgenteOptions>(configuration.GetSection(AnthropicAgenteOptions.Secao));
        services.Configure<MontagemFichaCliOptions>(configuration.GetSection(MontagemFichaCliOptions.Secao));
        services.AddSingleton<ClaudeCliMontagemFichaClient>();
        services.AddScoped<IMontagemFichaLlmClient>(sp =>
        {
            var cliente = sp.GetRequiredService<IOptions<MontagemFichaCliOptions>>().Value.Enabled
                ? ComRegistro(sp, sp.GetRequiredService<ClaudeCliMontagemFichaClient>()) // #3063: o CLI também grava chamada_ia
                : sp.GetRequiredService<IAgenteLlmClient>();
            return cliente as IMontagemFichaLlmClient ?? new MontagemFichaApiClient(cliente);
        });
        services.AddScoped<EasyStock.Application.UseCases.Atendimento.Producao.SugerirReceitaUseCase>();
        services.AddScoped<EasyStock.Application.UseCases.Acervo.AssistenteDoCardapioUseCase>();
        services.AddHttpClient<IPesquisaProducaoWeb, PesquisaProducaoWeb>(client =>
        {
            client.Timeout = TimeSpan.FromSeconds(12);
            client.DefaultRequestHeaders.UserAgent.ParseAdd("EasyStok/1.0 (+https://easystok.online)");
        }).ConfigurePrimaryHttpMessageHandler(() => new HttpClientHandler { AllowAutoRedirect = false });

        services.AddEasyStockIntegrationResilience(); // #2177: o agente passa pelo disjuntor da Anthropic
        services.AddHttpClient<AnthropicMessagesClient>((sp, client) =>
        {
            var opts = sp.GetRequiredService<IOptions<AnthropicAgenteOptions>>().Value;
            client.BaseAddress = new Uri(opts.BaseUrl.TrimEnd('/') + "/");
            client.Timeout = TimeSpan.FromSeconds(opts.TimeoutSegundos);
        });
        // #3064 (S2b de #2948): o provedor da loja tem cliente HTTP próprio, com as travas da spec §6; o do .env fica igual.
        services.AddHttpClient(ClienteProvedorIa.NomeHttp, client =>
        {
            client.Timeout = Timeout.InfiniteTimeSpan; // o prazo vem por chamada, em ClienteProvedorIa
            client.MaxResponseContentBufferSize = ClienteProvedorIa.RespostaMaximaBytes;
        }).ConfigurePrimaryHttpMessageHandler(ClienteProvedorIa.Handler);
        services.AddSingleton<IClienteProvedorIa, ClienteProvedorIa>();
        // #3063 (S2a): toda chamada do .env passa pelo registro de uso em chamada_ia. #3064: por fora, o roteador por loja,
        // scoped para a flag e a rota ficarem em memória durante a requisição.
        services.AddScoped<IAgenteLlmClient>(sp => ComRoteador(sp, ComRegistro(sp, sp.GetRequiredService<AnthropicMessagesClient>())));

        // #1398: transcrição de áudio no faster-whisper da VPS (ADR-0058); a Fireworks desativou o áudio.
        services.Configure<TranscricaoAudioOptions>(configuration.GetSection(TranscricaoAudioOptions.Secao));
        services.AddHttpClient<ITranscritorAudio, FireworksTranscritorAudio>((sp, client) =>
        {
            var opts = sp.GetRequiredService<IOptions<TranscricaoAudioOptions>>().Value;
            client.BaseAddress = new Uri(opts.BaseUrl.TrimEnd('/') + "/");
            client.Timeout = TimeSpan.FromSeconds(opts.TimeoutSegundos);
        });

        return services;
    }

    /// <summary>
    /// Sem persistência registrada (host sem banco), segue o cliente puro. #3365 (D2): o custo da chamada nasce aqui, com a
    /// tabela <c>Anthropic:Precos</c>; sem o decorador a resposta volta sem custo e o turno o grava nulo.
    /// </summary>
    private static IAgenteLlmClient ComRegistro(IServiceProvider sp, IAgenteLlmClient interno) =>
        sp.GetService<IRegistroChamadaIa>() is { } registro
            ? new RegistroChamadaIaLlmClient(interno, registro, sp.GetService<TimeProvider>() ?? TimeProvider.System,
                sp.GetRequiredService<ILogger<RegistroChamadaIaLlmClient>>(),
                sp.GetRequiredService<IOptions<PrecosModelosAgenteOptions>>())
            : interno;

    /// <summary>
    /// #3064: com o banco (flag, tenant, rota, cofre e registro), o roteador por loja vai por fora; sem ele, o do <c>.env</c>.
    /// </summary>
    private static IAgenteLlmClient ComRoteador(IServiceProvider sp, IAgenteLlmClient doEnv) =>
        sp.GetService<ITenantFeatureFlagRepository>() is { } flags
        && sp.GetService<ITenantAtual>() is { } tenant
        && sp.GetService<IRotasIa>() is { } rotas
        && sp.GetService<IIntegrationCredentialResolver>() is { } cofre
        && sp.GetService<IRegistroChamadaIa>() is { } registro
            ? new RoteadorIaLlmClient(doEnv, flags, tenant, rotas, cofre, sp.GetRequiredService<IClienteProvedorIa>(), registro,
                sp.GetService<TimeProvider>() ?? TimeProvider.System, sp.GetRequiredService<ILogger<RoteadorIaLlmClient>>())
            : doEnv;

    internal sealed class MontagemFichaApiClient(IAgenteLlmClient client) : IMontagemFichaLlmClient
    {
        internal IAgenteLlmClient Interno => client;
        public bool Disponivel => client.Disponivel;
        public Task<bool> DisponivelAsync(Guid empresaId, CancellationToken ct = default) => client.DisponivelAsync(empresaId, ct);
        public Task<RespostaLlm> EnviarAsync(RequisicaoLlm requisicao, CancellationToken ct = default) => client.EnviarAsync(requisicao, ct);
    }
}
