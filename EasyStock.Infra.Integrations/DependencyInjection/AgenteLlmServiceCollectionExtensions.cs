using EasyStock.Application.Ports.Output.Ai;
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

        services.AddHttpClient<IAgenteLlmClient, AnthropicMessagesClient>((sp, client) =>
        {
            var opts = sp.GetRequiredService<IOptions<AnthropicAgenteOptions>>().Value;
            client.BaseAddress = new Uri(opts.BaseUrl.TrimEnd('/') + "/");
            client.Timeout = TimeSpan.FromSeconds(opts.TimeoutSegundos);
        });

        // #1398: transcrição de áudio. Sem chave própria, reaproveita a chave Fireworks do agente.
        services.Configure<TranscricaoAudioOptions>(configuration.GetSection(TranscricaoAudioOptions.Secao));
        services.PostConfigure<TranscricaoAudioOptions>(o =>
        {
            if (!string.IsNullOrWhiteSpace(o.ApiKey)) return;
            var agente = configuration.GetSection(AnthropicAgenteOptions.Secao).Get<AnthropicAgenteOptions>();
            if (agente is { AutenticacaoBearer: true } && !string.IsNullOrWhiteSpace(agente.ApiKeyAgente))
                o.ApiKey = agente.ApiKeyAgente;
        });
        services.AddHttpClient<ITranscritorAudio, FireworksTranscritorAudio>((sp, client) =>
        {
            var opts = sp.GetRequiredService<IOptions<TranscricaoAudioOptions>>().Value;
            client.BaseAddress = new Uri(opts.BaseUrl.TrimEnd('/') + "/");
            client.Timeout = TimeSpan.FromSeconds(opts.TimeoutSegundos);
        });

        return services;
    }
}
