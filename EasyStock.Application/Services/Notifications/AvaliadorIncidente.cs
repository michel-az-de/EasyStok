namespace EasyStock.Application.Services.Notifications;

/// <summary>O que fazer com o incidente depois de uma verificação (N10).</summary>
public enum DecisaoIncidente
{
    Nada = 0,
    /// <summary>Atingiu o número de falhas seguidas: abre o incidente e avisa.</summary>
    Abrir = 1,
    /// <summary>Segue aberto e falhando: publica de novo (a dedupe do outbox transforma em lembrete de "ainda fora").</summary>
    Reavisar = 2,
    /// <summary>Voltou ao normal por tempo suficiente: avisa "normalizado" e fecha.</summary>
    Resolver = 3,
}

/// <summary>Limiares de uma fonte: quantas falhas seguidas abrem e quantas verificações boas seguidas resolvem.</summary>
public sealed record LimiaresIncidente(int FalhasParaAbrir, int BoasParaResolver)
{
    /// <summary>Endpoint do Worker: abre na 3ª falha, resolve na 2ª boa.</summary>
    public static readonly LimiaresIncidente Endpoint = new(3, 2);
}

/// <summary>Decisão e o instante em que o incidente abriu (<c>null</c> quando não há incidente em curso).</summary>
public sealed record AvaliacaoIncidente(DecisaoIncidente Decisao, DateTime? DesdeUtc);

/// <summary>Códigos fechados de falha; é só o que vai para <c>LastFailureMessage</c>, nunca <c>ex.Message</c>.</summary>
public static class CodigoFalhaIncidente
{
    public const string Timeout = "TIMEOUT";
    public const string ConexaoRecusada = "CONEXAO_RECUSADA";
    public const string Outro = "OUTRO";

    public static string DeStatusHttp(int status) => $"HTTP_{status}";

    /// <summary>Classifica a exceção sem olhar a mensagem: só o tipo decide o código.</summary>
    public static string DeExcecao(Exception excecao) => excecao switch
    {
        TimeoutException or OperationCanceledException => Timeout,
        HttpRequestException => ConexaoRecusada,
        _ => Outro,
    };
}

/// <summary>
/// Máquina de estado do incidente (N10), pura e sem IO, sobre o <see cref="EndpointHealthState"/> que já existe (mesma
/// tabela, sem coluna nova):
/// <list type="bullet">
/// <item><c>ConsecutiveFailures &gt; 0</c>: falhas seguidas. <c>&lt; 0</c>: verificações boas seguidas desde que o incidente abriu.</item>
/// <item><c>LastAlertedAt</c> preenchido: incidente aberto, desde esse instante.</item>
/// </list>
/// <see cref="Avaliar"/> muda o estado e devolve a decisão; quem chama persiste e publica.
/// </summary>
public static class AvaliadorIncidente
{
    public static AvaliacaoIncidente Avaliar(
        EndpointHealthState estado, bool saudavel, DateTime agora, LimiaresIncidente limiares)
    {
        ArgumentNullException.ThrowIfNull(estado);
        ArgumentNullException.ThrowIfNull(limiares);

        estado.LastCheckAt = agora;
        estado.AtualizadoEm = agora;
        var aberto = estado.LastAlertedAt is not null;

        if (saudavel)
        {
            if (!aberto)
            {
                estado.ConsecutiveFailures = 0;
                return new AvaliacaoIncidente(DecisaoIncidente.Nada, null);
            }

            estado.ConsecutiveFailures = estado.ConsecutiveFailures > 0 ? -1 : estado.ConsecutiveFailures - 1;
            var desde = estado.LastAlertedAt!.Value;
            if (-estado.ConsecutiveFailures < limiares.BoasParaResolver)
                return new AvaliacaoIncidente(DecisaoIncidente.Nada, desde);

            estado.ConsecutiveFailures = 0;
            estado.LastAlertedAt = null;
            return new AvaliacaoIncidente(DecisaoIncidente.Resolver, desde);
        }

        estado.ConsecutiveFailures = estado.ConsecutiveFailures > 0 ? estado.ConsecutiveFailures + 1 : 1;
        estado.LastFailureAt = agora;

        if (aberto)
            return new AvaliacaoIncidente(DecisaoIncidente.Reavisar, estado.LastAlertedAt);

        if (estado.ConsecutiveFailures < limiares.FalhasParaAbrir)
            return new AvaliacaoIncidente(DecisaoIncidente.Nada, null);

        estado.LastAlertedAt = agora;
        return new AvaliacaoIncidente(DecisaoIncidente.Abrir, agora);
    }
}
