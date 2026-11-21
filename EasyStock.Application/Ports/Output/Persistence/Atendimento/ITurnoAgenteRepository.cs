using EasyStock.Domain.Entities.Atendimento;
using EasyStock.Domain.Enums.Atendimento;

namespace EasyStock.Application.Ports.Output.Persistence.Atendimento;

/// <summary>S67 (#2534): registro dos turnos do Assistente, sempre filtrado por empresa.</summary>
public interface ITurnoAgenteRepository
{
    Task AddAsync(TurnoAgente turno, CancellationToken ct = default);

    /// <summary>Mais recentes primeiro. O texto da pergunta e da resposta vem de <c>atendimento_mensagens</c> pelos ids.</summary>
    Task<PaginaTurnosAgente> ListarAsync(Guid empresaId, FiltroTurnosAgente filtro, CancellationToken ct = default);

    /// <summary>Soma só os turnos do Assistente em [<paramref name="deUtc"/>, <paramref name="ateUtc"/>).</summary>
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
    string? Resposta);

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
    /// S78 (#2896): as respostas sem IA vezes a média de tokens dos turnos que gastaram token no mesmo período. Turno de
    /// cota esgotada ou de erro sem consumo fica fora da média. Sem resposta sem IA ou sem turno com token não há o que
    /// estimar.
    /// </summary>
    public long? TokensPoupadosEstimados =>
        RespostasSemIa > 0 && TurnosComTokens > 0 ? TotalTokens * RespostasSemIa / TurnosComTokens : null;
}
