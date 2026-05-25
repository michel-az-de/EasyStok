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
/// </summary>
public static class IntegrationsServiceCollectionExtensions
{
    public static IServiceCollection AddEasyStockIntegrationResilience(this IServiceCollection services)
    {
        foreach (var category in IntegrationCategories.All)
        {
            services.AddResiliencePipeline(category, builder =>
            {
                builder
                    .AddRetry(new RetryStrategyOptions
                    {
                        MaxRetryAttempts = 3,
                        BackoffType = DelayBackoffType.Exponential,
                        UseJitter = true,
                        Delay = TimeSpan.FromMilliseconds(200),
                    })
                    .AddCircuitBreaker(new CircuitBreakerStrategyOptions
                    {
                        FailureRatio = 0.5,
                        MinimumThroughput = 8,
                        SamplingDuration = TimeSpan.FromSeconds(30),
                        BreakDuration = TimeSpan.FromSeconds(60),
                    })
                    .AddTimeout(new TimeoutStrategyOptions
                    {
                        Timeout = TimeSpan.FromSeconds(30),
                    });
            });
        }

        return services;
    }
}
