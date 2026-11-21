namespace EasyStock.Application.Services.Atendimento;

/// <summary>
/// #2285: anti-loop de custo do agente. Na mesma conversa, a partir de <see cref="MaximoPorHora"/> respostas do agente
/// na última hora, o turno não chama o LLM: a conversa passa para a dona, que é avisada pela escalada. Configuração em
/// <c>Atendimento:LimiteRespostasAgente:MaximoPorHora</c>; zero ou negativo desliga.
/// </summary>
public sealed class LimiteRespostasAgenteOptions
{
    public const string ChaveMaximoPorHora = "Atendimento:LimiteRespostasAgente:MaximoPorHora";
    public const int PadraoPorHora = 20;

    public int MaximoPorHora { get; set; } = PadraoPorHora;
}
