using EasyStock.Domain.Entities.Atendimento;
using EasyStock.Domain.Enums.Atendimento;

namespace EasyStock.Application.Ports.Output.Persistence.Atendimento;

/// <summary>S67 (#2534): registro dos turnos do Assistente, sempre filtrado por empresa.</summary>
public interface ITurnoAgenteRepository
{
    Task AddAsync(TurnoAgente turno, CancellationToken ct = default);

    /// <summary>Mais recentes primeiro. O texto da pergunta e da resposta vem de <c>atendimento_mensagens</c> pelos ids.</summary>
    Task<PaginaTurnosAgente> ListarAsync(Guid empresaId, FiltroTurnosAgente filtro, CancellationToken ct = default);

    /// <summary>
    /// Soma os turnos do Assistente em [<paramref name="deUtc"/>, <paramref name="ateUtc"/>), com o piso das chamadas do
    /// Assistente em <c>chamada_ia</c>: as ligadas a um turno, mesmo o que não gravou (#3370,
    /// <see cref="SomaTurnosAgente.ComPisoDasChamadas"/>). A cota e o cartão de uso leem por aqui, então os dois mostram o
    /// mesmo consumo.
    /// </summary>
    Task<SomaTurnosAgente> SomarAsync(Guid empresaId, DateTime deUtc, DateTime ateUtc, CancellationToken ct = default);
}

/// <summary><paramref name="Pagina"/> começa em 1.</summary>
public sealed record FiltroTurnosAgente(Guid? ConversaId, DesfechoTurnoAgente? Desfecho, int Pagina, int TamanhoPagina);

public sealed record TurnoAgenteLido(
    Guid Id,
    Guid ConversaId,
    DateTime CriadoEm,
    string Modelo,
    int TokensEntrada,
    int TokensSaida,
    int TokensCacheLido,
    decimal? CustoEstimado,
    IReadOnlyList<FerramentaDoTurno> Ferramentas,
    int Passos,
    DesfechoTurnoAgente Desfecho,
    int DuracaoMs,
    string? PerguntaCliente,
    string? Resposta,
    string? Motivo = null);

public sealed record PaginaTurnosAgente(IReadOnlyList<TurnoAgenteLido> Itens, int Total);

/// <summary>
/// <paramref name="Custo"/> soma só os turnos com preço; <paramref name="TurnosSemPreco"/> diz quantos ficaram fora.
/// <paramref name="RespostasSemIa"/> (S78, #2896) conta, dentro de <paramref name="Turnos"/>, os de desfecho <c>SemIa</c>;
/// <paramref name="TurnosComTokens"/>, os que gastaram algum token (os que chamaram o modelo de fato).
/// </summary>
public sealed record SomaTurnosAgente(
    int Turnos,
    long TokensEntrada,
    long TokensSaida,
    long TokensCacheLido,
    decimal Custo,
    int TurnosSemPreco,
    int RespostasSemIa = 0,
    int TurnosComTokens = 0)
{
    public static readonly SomaTurnosAgente Vazia = new(0, 0, 0, 0, 0m, 0);

    public long TotalTokens => TokensEntrada + TokensSaida + TokensCacheLido;

    /// <summary>
    /// #3370: o total de tokens dos turnos quando o piso das chamadas o trocou por um valor maior; nulo quando os tokens já
    /// são os dos turnos. A estimativa de poupados usa este total, porque o divisor (<c>TurnosComTokens</c>) só tem turnos.
    /// </summary>
    public long? TokensDosTurnos { get; init; }

    /// <summary>
    /// #3370 (D2 da #3271): piso do consumo. Se as chamadas do Assistente em <c>chamada_ia</c> (as ligadas a um turno, na
    /// janela) somam mais tokens do que os turnos, os tokens passam a ser os das chamadas (o turno caiu depois de chamar o
    /// modelo e não gravou). É o maior dos dois registros, nunca a soma: cada chamada pertence a um turno e contaria duas
    /// vezes. Empate fica nos turnos. Só os tokens mudam; contagens e custo seguem dos turnos, então o cartão pode mostrar
    /// mais tokens do que o custo cobre. O piso não é por turno: uma chamada perdida num turno gravado esconde, até o tamanho
    /// dela, um turno que não gravou no mesmo período. Aplique uma vez por soma: numa segunda aplicação o total comparado já
    /// é o do piso.
    /// </summary>
    /// <param name="entrada">Entrada das chamadas, já com a escrita de cache (o turno também a soma na entrada, #2862).</param>
    public SomaTurnosAgente ComPisoDasChamadas(long entrada, long saida, long cacheLido)
    {
        var dosTurnos = TotalTokens;
        return entrada + saida + cacheLido <= dosTurnos
            ? this
            : this with { TokensEntrada = entrada, TokensSaida = saida, TokensCacheLido = cacheLido, TokensDosTurnos = dosTurnos };
    }

    /// <summary>
    /// S78 (#2896): as respostas sem IA vezes a média de tokens dos turnos que gastaram token no mesmo período. Turno de
    /// cota esgotada ou de erro sem consumo fica fora da média. Sem resposta sem IA ou sem turno com token não há o que
    /// estimar. #3370: a média é a dos turnos, não a do piso das chamadas.
    /// </summary>
    public long? TokensPoupadosEstimados =>
        RespostasSemIa > 0 && TurnosComTokens > 0 ? (TokensDosTurnos ?? TotalTokens) * RespostasSemIa / TurnosComTokens : null;
}
