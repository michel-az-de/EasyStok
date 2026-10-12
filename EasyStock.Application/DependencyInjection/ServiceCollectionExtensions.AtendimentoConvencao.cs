// Registro por convenção do atendimento (#1178). Toda spec acrescentava linhas no mesmo trecho de
// ServiceCollectionExtensions.Atendimento.cs e as PRs paralelas conflitavam ali. Agora entra sozinho:
//  - classe pública concreta `*UseCase` em EasyStock.Application.UseCases.Atendimento(.*) → Scoped;
//  - implementação de IFerramentaAgente ou IAcaoBotaoHandler → Scoped, uma vez só.
// Registro com interface própria ou outro ciclo de vida continua explícito no arquivo do módulo.

using EasyStock.Application.Services.Atendimento.AcoesBotao;
using EasyStock.Application.Services.Atendimento.Ferramentas;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace EasyStock.Application.DependencyInjection;

public static partial class ServiceCollectionExtensions
{
    private const string NamespaceUseCasesAtendimento = "EasyStock.Application.UseCases.Atendimento";

    private static IServiceCollection AddAtendimentoPorConvencao(this IServiceCollection services)
    {
        // Ordem estável (nome completo): a lista de ferramentas vira a ordem das ferramentas do agente.
        var tipos = typeof(ServiceCollectionExtensions).Assembly.GetTypes()
            .Where(t => t is { IsClass: true, IsAbstract: false, IsPublic: true, IsGenericTypeDefinition: false })
            .OrderBy(t => t.FullName, StringComparer.Ordinal)
            .ToList();

        foreach (var useCase in tipos.Where(EhUseCaseDoAtendimento))
            services.TryAddScoped(useCase);

        foreach (var ferramenta in tipos.Where(typeof(IFerramentaAgente).IsAssignableFrom))
            services.TryAddEnumerable(ServiceDescriptor.Scoped(typeof(IFerramentaAgente), ferramenta));

        foreach (var tratador in tipos.Where(typeof(IAcaoBotaoHandler).IsAssignableFrom))
            services.TryAddEnumerable(ServiceDescriptor.Scoped(typeof(IAcaoBotaoHandler), tratador));

        return services;
    }

    private static bool EhUseCaseDoAtendimento(Type tipo) =>
        tipo.Name.EndsWith("UseCase", StringComparison.Ordinal)
        && (tipo.Namespace == NamespaceUseCasesAtendimento
            || tipo.Namespace?.StartsWith(NamespaceUseCasesAtendimento + ".", StringComparison.Ordinal) == true);
}
