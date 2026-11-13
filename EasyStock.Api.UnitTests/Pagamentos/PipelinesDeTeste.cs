using EasyStock.Infra.Integrations.DependencyInjection;
using Microsoft.Extensions.DependencyInjection;
using Polly.Registry;

namespace EasyStock.Api.UnitTests.Pagamentos;

/// <summary>
/// Pipelines de produção (#2177) para construir cliente HTTP à mão. Um provider novo por chamada: o disjuntor é estado
/// do registro e não pode vazar entre testes.
/// </summary>
internal static class PipelinesDeTeste
{
    public static ResiliencePipelineProvider<string> Reais() =>
        new ServiceCollection().AddEasyStockIntegrationResilience().BuildServiceProvider()
            .GetRequiredService<ResiliencePipelineProvider<string>>();
}