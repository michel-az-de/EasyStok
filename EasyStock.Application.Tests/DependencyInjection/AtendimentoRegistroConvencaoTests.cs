using EasyStock.Application.DependencyInjection;
using EasyStock.Application.Services.Atendimento.AcoesBotao;
using EasyStock.Application.Services.Atendimento.Ferramentas;
using Microsoft.Extensions.DependencyInjection;

namespace EasyStock.Application.Tests.DependencyInjection;

// #1178: use case, ferramenta do agente e tratador de botão do atendimento entram por convenção,
// para uma spec nova não precisar editar o arquivo de registro (conflito entre PRs paralelas).
public class AtendimentoRegistroConvencaoTests
{
    private const string NamespaceUseCases = "EasyStock.Application.UseCases.Atendimento";

    private static IServiceCollection Registrar() => new ServiceCollection().AddEasyStockAtendimentoUseCases();

    private static IEnumerable<Type> TiposDaApplication() =>
        typeof(ServiceCollectionExtensions).Assembly.GetTypes()
            .Where(t => t is { IsClass: true, IsAbstract: false, IsPublic: true, IsGenericTypeDefinition: false });

    [Fact]
    public void TodoUseCaseDoAtendimentoFicaRegistrado()
    {
        var registrados = Registrar().Select(d => d.ServiceType).ToHashSet();
        var useCases = TiposDaApplication()
            .Where(t => t.Name.EndsWith("UseCase", StringComparison.Ordinal)
                && (t.Namespace == NamespaceUseCases || t.Namespace?.StartsWith(NamespaceUseCases + ".", StringComparison.Ordinal) == true))
            .ToList();

        useCases.Should().HaveCountGreaterThanOrEqualTo(33, "havia 33 use cases do atendimento registrados à mão em 2026-09-30");
        useCases.Where(t => !registrados.Contains(t)).Should().BeEmpty();
    }

    [Fact]
    public void FerramentasDoAgenteEntramTodasUmaVezSo()
    {
        var ferramentas = Registrar()
            .Where(d => d.ServiceType == typeof(IFerramentaAgente))
            .Select(d => d.ImplementationType!.Name)
            .ToList();

        ferramentas.Should().OnlyHaveUniqueItems();
        ferramentas.Should().Contain([
            "ConsultarCardapioFerramenta", "ConsultarPedidoFerramenta", "CriarPedidoFerramenta",
            "EncerrarConversaFerramenta", "EnviarCardapioImagemFerramenta", "EscalarParaDonaFerramenta",
            "ListarJanelasFerramenta", "RegistrarNotaFerramenta", "RegistrarRestricaoFerramenta",
        ]);
        ferramentas.Should().HaveCount(TiposDaApplication().Count(typeof(IFerramentaAgente).IsAssignableFrom));
    }

    [Fact]
    public void TratadoresDeBotaoEntramTodosUmaVezSo()
    {
        var tratadores = Registrar()
            .Where(d => d.ServiceType == typeof(IAcaoBotaoHandler))
            .Select(d => d.ImplementationType!.Name)
            .ToList();

        tratadores.Should().OnlyHaveUniqueItems();
        tratadores.Should().Contain(["ConfirmarEnderecoAcaoBotao", "EscolherJanelaAcaoBotao"]);
        tratadores.Should().HaveCount(TiposDaApplication().Count(typeof(IAcaoBotaoHandler).IsAssignableFrom));
    }
}
