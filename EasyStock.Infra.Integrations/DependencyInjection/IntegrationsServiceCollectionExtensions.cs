using EasyStock.Infra.Integrations.Resilience;
using Microsoft.Extensions.DependencyInjection;
using Polly;
using Polly.CircuitBreaker;
using Polly.Retry;
using Polly.Timeout;

namespace EasyStock.Infra.Integrations.DependencyInjection;

/// <summary>
/// Extensão pra <see cref="IServiceCollection"/> que registra os pipelines
/// de resiliência usados por adapters de integração externa.
///
/// <para>
/// Cada categoria (<see cref="IntegrationCategories"/>) ganha um pipeline
/// próprio com retry exponencial + circuit breaker + timeout. Adapters
/// resolvem via <see cref="ResiliencePipelineProvider{TKey}"/> usando o
/// nome da categoria.
/// </para>
///
/// <para>
/// Defaults conservadores: 3 retries com backoff 200ms→1.6s + jitter,
/// circuit breaker abre por 60s após 50% de falhas em janela de 30s
/// (mín 8 chamadas), timeout total 30s.
/// </para>
///
/// <para>
/// Exceção: <see cref="IntegrationCategories.WhatsAppEnvio"/> tem só circuit breaker e timeout. Envio
/// que não é idempotente não pode ser repetido às cegas (#1292).
/// </para>
///
/// <para>
/// #2177: pagamento, rota e IA reagem também à resposta transitória (5xx, 408, 429), não só à exceção
/// (<see cref="FalhaHttpTransitoria"/>). Leitura repete; escrita do Mercado Pago só tem disjuntor e timeout; a
/// Anthropic só tem disjuntor, porque o timeout do agente é o do <c>HttpClient</c> (<c>Anthropic:TimeoutSegundos</c>).
/// </para>
/// </summary>
public static class IntegrationsServiceCollectionExtensions
{
    public static IServiceCollection AddEasyStockIntegrationResilience(this IServiceCollection services)
    {
        // #2177: quem registra um cliente que usa os pipelines também chama este método; a segunda chamada não faz nada.
        if (services.Any(d => d.ServiceType == typeof(MarcadorResiliencia))) return services;
        services.AddSingleton<MarcadorResiliencia>();

        foreach (var category in IntegrationCategories.All)
        {
            services.AddResiliencePipeline(category, builder =>
            {
                builder.AddRetry(Retry(tentativas: 3));
                AdicionarDisjuntorETimeout(builder);
            });
        }

        // #1292: envio não idempotente (POST /messages da Meta) não repete; falha volta para quem chamou.
        services.AddResiliencePipeline(IntegrationCategories.WhatsAppEnvio, AdicionarDisjuntorETimeout);
        services.AddResiliencePipeline(IntegrationCategories.MetaEnvio, AdicionarDisjuntorETimeout);
        services.AddResiliencePipeline(IntegrationCategories.WahaEnvio, AdicionarDisjuntorETimeout);

        services.AddResiliencePipeline(IntegrationCategories.Payments, builder =>
        {
            builder.AddRetry(RetryHttp(tentativas: 3));
            AdicionarDisjuntorETimeout(builder, FalhaHttpTransitoria.ContaNoDisjuntor);
        });
        services.AddResiliencePipeline(IntegrationCategories.PagamentosEscrita,
            builder => AdicionarDisjuntorETimeout(builder, FalhaHttpTransitoria.ContaNoDisjuntor));

        // Frete: o HttpClient tem 2 s e a cotação cai para haversine, então 1 retry curto basta.
        foreach (var rota in new[] { IntegrationCategories.GoogleRotas, IntegrationCategories.TomTom })
        {
            services.AddResiliencePipeline(rota, builder =>
            {
                builder.AddRetry(RetryHttp(tentativas: 1));
                AdicionarDisjuntorETimeout(builder, FalhaHttpTransitoria.ContaNoDisjuntor);
            });
        }

        services.AddResiliencePipeline(IntegrationCategories.Anthropic,
            builder => builder.AddCircuitBreaker(Disjuntor(FalhaHttpTransitoria.ContaNoDisjuntor)));

        return services;
    }

    private sealed class MarcadorResiliencia;

    private static RetryStrategyOptions Retry(int tentativas) => new()
    {
        MaxRetryAttempts = tentativas,
        BackoffType = DelayBackoffType.Exponential,
        UseJitter = true,
        Delay = TimeSpan.FromMilliseconds(200),
    };

    private static RetryStrategyOptions RetryHttp(int tentativas)
    {
        var opcoes = Retry(tentativas);
        opcoes.ShouldHandle = args => ValueTask.FromResult(FalhaHttpTransitoria.Repete(args.Outcome));
        return opcoes;
    }

    private static void AdicionarDisjuntorETimeout(ResiliencePipelineBuilder builder) =>
        AdicionarDisjuntorETimeout(builder, contaNoDisjuntor: null);

    private static void AdicionarDisjuntorETimeout(ResiliencePipelineBuilder builder, Func<Outcome<object>, bool>? contaNoDisjuntor) =>
        builder
            .AddCircuitBreaker(Disjuntor(contaNoDisjuntor))
            .AddTimeout(new TimeoutStrategyOptions
            {
                Timeout = TimeSpan.FromSeconds(30),
            });

    private static CircuitBreakerStrategyOptions Disjuntor(Func<Outcome<object>, bool>? contaNoDisjuntor)
    {
        var opcoes = new CircuitBreakerStrategyOptions
        {
            FailureRatio = 0.5,
            MinimumThroughput = 8,
            SamplingDuration = TimeSpan.FromSeconds(30),
            BreakDuration = TimeSpan.FromSeconds(60),
        };
        if (contaNoDisjuntor is not null)
            opcoes.ShouldHandle = args => ValueTask.FromResult(contaNoDisjuntor(args.Outcome));
        return opcoes;
    }
}
