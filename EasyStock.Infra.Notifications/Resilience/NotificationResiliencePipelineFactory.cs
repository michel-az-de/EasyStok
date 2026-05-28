using Polly;
using Polly.Retry;

namespace EasyStock.Infra.Notifications.Resilience;

internal static class NotificationResiliencePipelineFactory
{
    public static ResiliencePipeline CreateHttpProviderPipeline() =>
        new ResiliencePipelineBuilder()
            .AddRetry(new RetryStrategyOptions
            {
                MaxRetryAttempts = 3,
                BackoffType = DelayBackoffType.Exponential,
                Delay = TimeSpan.FromSeconds(1),
                ShouldHandle = new PredicateBuilder().Handle<HttpRequestException>()
            })
            .Build();
}
