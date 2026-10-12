using System.Reflection;
using FluentAssertions;
using NetArchTest.Rules;

namespace EasyStock.ArchitectureTests;

/// <summary>
/// N6: o WhatsApp de plataforma nunca toca o atendimento. O acoplamento do provider antigo estava em namespaces
/// (<c>Application.Services.Atendimento</c>), que o <c>csproj</c> não enxerga; este teste o guarda no IL. Vale para o
/// provider (<c>Infra.Notifications.WhatsApp.Plataforma</c>) e para os casos de uso do webhook
/// (<c>Application.UseCases.Notifications.Plataforma</c>).
/// </summary>
public class PlataformaNaoDependeDoAtendimentoTests
{
    private const string InfraPlataforma = "EasyStock.Infra.Notifications.WhatsApp.Plataforma";
    private const string UseCasesPlataforma = "EasyStock.Application.UseCases.Notifications.Plataforma";

    private static readonly string[] NamespacesDoAtendimento =
    [
        "EasyStock.Application.Ports.Output.Atendimento",
        "EasyStock.Application.Services.Atendimento",
        "EasyStock.Application.UseCases.Atendimento",
        "EasyStock.Application.Ports.Output.Persistence.Atendimento",
        "EasyStock.Domain.Entities.Atendimento",
        "EasyStock.Domain.Enums.Atendimento",
    ];

    private static Assembly[] Assemblies =>
    [
        typeof(EasyStock.Infra.Notifications.WhatsApp.Plataforma.MetaCloudWhatsAppPlataformaProvider).Assembly,
        typeof(EasyStock.Application.UseCases.Notifications.Plataforma.CamposWebhookMeta).Assembly,
    ];

    private static PredicateList TiposDePlataforma() =>
        Types.InAssemblies(Assemblies).That()
            .ResideInNamespaceStartingWith(InfraPlataforma)
            .Or().ResideInNamespaceStartingWith(UseCasesPlataforma);

    [Fact]
    public void TiposDePlataformaNaoDependemDoAtendimento()
    {
        var resultado = TiposDePlataforma()
            .ShouldNot().HaveDependencyOnAny(NamespacesDoAtendimento)
            .GetResult();

        resultado.IsSuccessful.Should().BeTrue(
            "WhatsApp de plataforma não conversa com a Conversa nem com o número da loja. Violadores: "
            + string.Join(", ", resultado.FailingTypeNames ?? []));
    }

    [Fact]
    public void CanarioVeOsTiposDePlataforma()
    {
        // Sanidade do varredor: sem tipos varridos o teste acima passaria sem provar nada (molde da sanidade do varredor
        // de NotaInternaNaoVazaParaStorefront).
        var tipos = TiposDePlataforma().GetTypes().ToList();

        tipos.Count.Should().BeGreaterThanOrEqualTo(5, "o varredor precisa enxergar o provider e os casos de uso de plataforma");
        tipos.Select(t => t.Name).Should().Contain(
            ["MetaCloudWhatsAppPlataformaProvider", "ProcessarStatusWhatsAppPlataformaUseCase"]);

        // E o varredor precisa pegar uma violação de verdade: um tipo do atendimento deve ser reconhecido como tal.
        Types.InAssembly(typeof(EasyStock.Application.UseCases.Atendimento.Webhook.ProcessarEventoWhatsAppUseCase).Assembly)
            .That().HaveName("ProcessarEventoWhatsAppUseCase")
            .Should().HaveDependencyOnAny(NamespacesDoAtendimento)
            .GetResult().IsSuccessful.Should().BeTrue("o detector de dependência precisa reconhecer o atendimento");
    }
}
