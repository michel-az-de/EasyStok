using System.Reflection;
using EasyStock.Application.Ports.Output.Persistence.Atendimento;
using EasyStock.Application.Services.Atendimento;
using EasyStock.Application.Services.Ia;
using EasyStock.Application.UseCases.Atendimento;
using FluentAssertions;
using Microsoft.Extensions.Options;

namespace EasyStock.ArchitectureTests;

/// <summary>
/// Guard (#3298, R7 da revisão do harness do Assistente): o controle de custo não pode falhar aberto. Cota mensal, limite
/// de respostas por hora e registro do turno dependem do repositório de turnos e das opções de limite; o custo da chamada, das de preços. Com
/// qualquer um deles opcional (padrão nulo), uma fiação esquecida deixava a cota virar "não se aplica" e o turno sem
/// registro, sem erro algum. Obrigatória, a fiação errada falha na hora de montar o serviço.
/// #3365 (D2): o preço mora só na chamada. Quem registra a chamada em <c>chamada_ia</c> (<see cref="RegistroChamadaIaLlmClient"/>)
/// calcula o custo uma vez e o devolve; o turno apenas o soma, sem tabela de preço, para o número do turno ser o das linhas.
/// </summary>
[Trait("Category", "Architecture")]
public class ControleDeCustoDoAssistenteTests
{
    // O fim do turno (registro e envio) é interno ao Application: acha-se pelo nome, e o guarda falha alto se ele mudar.
    private static readonly Type Finalizacao = typeof(AgenteAtendimentoService).Assembly
        .GetType("EasyStock.Application.Services.Atendimento.FinalizacaoDoTurno", throwOnError: true)!;

    private static readonly Type[] TiposQueAplicamOCusto =
    [
        typeof(AgenteAtendimentoService),
        typeof(PortoesDoAssistente),
        typeof(RespostaSemIa),
        typeof(ChecarAssistenteConversaUseCase),
        Finalizacao,
        typeof(RegistroChamadaIaLlmClient),
    ];

    private static readonly Type[] RegrasDeCusto =
    [
        typeof(ITurnoAgenteRepository),
        typeof(IOptions<LimiteRespostasAgenteOptions>),
        typeof(IOptions<PrecosModelosAgenteOptions>),
    ];

    private static IEnumerable<(Type Tipo, ParameterInfo Parametro)> ParametrosDeCusto() =>
        TiposQueAplicamOCusto.SelectMany(t => t.GetConstructors().SelectMany(c => c.GetParameters()
            .Where(p => RegrasDeCusto.Contains(p.ParameterType)).Select(p => (t, p))));

    [Fact]
    public void RegraDeCusto_NuncaEhDependenciaOpcional()
    {
        var contexto = new NullabilityInfoContext();
        var opcionais = ParametrosDeCusto()
            .Where(x => x.Parametro.HasDefaultValue || contexto.Create(x.Parametro).WriteState != NullabilityState.NotNull)
            .Select(x => $"{x.Tipo.Name}({x.Parametro.ParameterType.Name} {x.Parametro.Name})")
            .ToList();

        opcionais.Should().BeEmpty(
            "sem elas a cota vira 'não se aplica' e o turno fica sem registro, sem erro (R7, #3298)");
    }

    [Fact]
    public void QuemAplicaOLimiteEACota_RecebeOsRepositoriosEasOpcoes()
    {
        // Evita o guarda acima passar vazio se um tipo deixar de receber a dependência (por exemplo ao mover a regra).
        var porTipo = ParametrosDeCusto().GroupBy(x => x.Tipo).ToDictionary(g => g.Key, g => g.Select(x => x.Parametro.ParameterType).ToList());

        porTipo[typeof(AgenteAtendimentoService)].Should().Contain(typeof(ITurnoAgenteRepository));
        foreach (var tipo in new[] { typeof(AgenteAtendimentoService), typeof(PortoesDoAssistente), typeof(RespostaSemIa), typeof(ChecarAssistenteConversaUseCase) })
            porTipo[tipo].Should().Contain(typeof(IOptions<LimiteRespostasAgenteOptions>), $"{tipo.Name} aplica o limite de respostas");
        foreach (var tipo in new[] { typeof(PortoesDoAssistente), typeof(RespostaSemIa), typeof(ChecarAssistenteConversaUseCase) })
            porTipo[tipo].Should().Contain(typeof(ITurnoAgenteRepository), $"{tipo.Name} aplica a cota do mês");
        porTipo[Finalizacao].Should().Contain(typeof(ITurnoAgenteRepository), "o fim do turno registra o turno");
    }

    [Fact]
    public void OPrecoSoMoraNaChamada_OTurnoSomaOCustoSemTabelaPropria()
    {
        // #3365 (D2): sem esta asserção o guarda acima passa vazio se o construtor da chamada perder o IOptions de preços, e a
        // tabela de preço de volta no turno recriaria o segundo cálculo que o D2 eliminou.
        var porTipo = ParametrosDeCusto().GroupBy(x => x.Tipo).ToDictionary(g => g.Key, g => g.Select(x => x.Parametro.ParameterType).ToList());

        porTipo[typeof(RegistroChamadaIaLlmClient)].Should().Contain(typeof(IOptions<PrecosModelosAgenteOptions>),
            "o custo da chamada nasce no registro dela, com a tabela Anthropic:Precos");
        foreach (var tipo in new[] { typeof(AgenteAtendimentoService), Finalizacao })
            porTipo[tipo].Should().NotContain(typeof(IOptions<PrecosModelosAgenteOptions>),
                $"{tipo.Name} soma o custo que cada chamada trouxe e não recalcula (D2, #3365)");
    }
}
