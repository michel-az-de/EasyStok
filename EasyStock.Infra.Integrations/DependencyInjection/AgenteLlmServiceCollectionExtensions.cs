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
        services.Configure<MontagemFichaCliOptions>(configuration.GetSection(MontagemFichaCliOptions.Secao));
        services.AddSingleton<ClaudeCliMontagemFichaClient>();
        services.AddScoped<IMontagemFichaLlmClient>(sp =>
            sp.GetRequiredService<IOptions<MontagemFichaCliOptions>>().Value.Enabled
                ? sp.GetRequiredService<ClaudeCliMontagemFichaClient>()
                : new MontagemFichaApiClient(sp.GetRequiredService<IAgenteLlmClient>()));
        services.AddScoped<EasyStock.Application.UseCases.Atendimento.Producao.SugerirReceitaUseCase>();
        services.AddScoped<EasyStock.Application.UseCases.Acervo.AssistenteDoCardapioUseCase>();
        services.AddHttpClient<IPesquisaProducaoWeb, PesquisaProducaoWeb>(client =>
        {
            client.Timeout = TimeSpan.FromSeconds(12);
            client.DefaultRequestHeaders.UserAgent.ParseAdd("EasyStok/1.0 (+https://easystok.online)");
        }).ConfigurePrimaryHttpMessageHandler(() => new HttpClientHandler { AllowAutoRedirect = false });

        services.AddHttpClient<IAgenteLlmClient, AnthropicMessagesClient>((sp, client) =>
        {
            var opts = sp.GetRequiredService<IOptions<AnthropicAgenteOptions>>().Value;
            client.BaseAddress = new Uri(opts.BaseUrl.TrimEnd('/') + "/");
            client.Timeout = TimeSpan.FromSeconds(opts.TimeoutSegundos);
        });

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

    private sealed class MontagemFichaApiClient(IAgenteLlmClient client) : IMontagemFichaLlmClient
    {
        public bool Disponivel => client.Disponivel;
        public Task<RespostaLlm> EnviarAsync(RequisicaoLlm requisicao, CancellationToken ct = default) => client.EnviarAsync(requisicao, ct);
    }
}
