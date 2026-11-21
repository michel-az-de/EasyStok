using EasyStock.Application.Ports.Output.Ai;
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
        // #3063 (S2a de #2948): toda chamada passa pelo registro de uso em chamada_ia.
        services.AddTransient<IAgenteLlmClient>(sp => ComRegistro(sp, sp.GetRequiredService<AnthropicMessagesClient>()));

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

    /// <summary>Sem persistência registrada (host sem banco), segue o cliente puro.</summary>
    private static IAgenteLlmClient ComRegistro(IServiceProvider sp, IAgenteLlmClient interno) =>
        sp.GetService<IRegistroChamadaIa>() is { } registro
            ? new RegistroChamadaIaLlmClient(interno, registro, sp.GetService<TimeProvider>() ?? TimeProvider.System,
                sp.GetRequiredService<ILogger<RegistroChamadaIaLlmClient>>())
            : interno;

    internal sealed class MontagemFichaApiClient(IAgenteLlmClient client) : IMontagemFichaLlmClient
    {
        internal IAgenteLlmClient Interno => client;
        public bool Disponivel => client.Disponivel;
        public Task<RespostaLlm> EnviarAsync(RequisicaoLlm requisicao, CancellationToken ct = default) => client.EnviarAsync(requisicao, ct);
    }
}
