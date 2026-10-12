using EasyStock.Domain.Enums.Notifications;

namespace EasyStock.Application.Services.Notifications;

/// <summary>Uma decisão do avaliador de saúde para um componente (N10).</summary>
public sealed record TransicaoSaude(
    ComponenteIncidente Componente, DecisaoIncidente Decisao, SeveridadeIncidente Severidade, DateTime? DesdeUtc);

/// <summary>
/// Avalia o snapshot de saúde da API a cada 60 s (N10). Só avisa transição para e de <c>critical</c> (banco: 2 snapshots
/// ruins seguidos abrem, 3 bons resolvem) e <c>degraded</c> por Redis (<c>RedisStatus = "falha"</c>, mesmos limiares).
/// <c>degraded</c> só por <c>ErrorCount &gt; 0</c> <b>não alerta</b>: qualquer linha de erro em 65 s alertaria o tempo
/// todo, e o pico de 5xx (com limiar) cobre essa causa. O estado fica em memória (o processo da API é o dono do loop), de
/// propósito: com o banco fora do ar não haveria onde ler nem gravar a sequência. Reiniciar a API zera as sequências.
/// </summary>
public sealed class AvaliadorSaudeSnapshot
{
    public static readonly LimiaresIncidente Limiares = new(FalhasParaAbrir: 2, BoasParaResolver: 3);

    private readonly EndpointHealthState _banco = new() { EndpointName = "sistema/db" };
    private readonly EndpointHealthState _redis = new() { EndpointName = "sistema/redis" };

    /// <param name="statusGeral"><c>ok</c>, <c>degraded</c> ou <c>critical</c>, como no snapshot.</param>
    /// <param name="redisStatus"><c>ok</c>, <c>falha</c> ou <c>null</c> quando o Redis não está configurado.</param>
    /// <returns>Só as decisões diferentes de <see cref="DecisaoIncidente.Nada"/>.</returns>
    public IReadOnlyList<TransicaoSaude> Avaliar(string statusGeral, string? redisStatus, DateTime agoraUtc)
    {
        var decisoes = new List<TransicaoSaude>(2);
        Acrescentar(decisoes, ComponenteIncidente.Banco, SeveridadeIncidente.Critica, _banco,
            saudavel: !string.Equals(statusGeral, "critical", StringComparison.OrdinalIgnoreCase), agoraUtc);
        Acrescentar(decisoes, ComponenteIncidente.Redis, SeveridadeIncidente.Media, _redis,
            saudavel: !string.Equals(redisStatus, "falha", StringComparison.OrdinalIgnoreCase), agoraUtc);
        return decisoes;
    }

    private static void Acrescentar(
        List<TransicaoSaude> decisoes, ComponenteIncidente componente, SeveridadeIncidente severidade,
        EndpointHealthState estado, bool saudavel, DateTime agoraUtc)
    {
        var avaliacao = AvaliadorIncidente.Avaliar(estado, saudavel, agoraUtc, Limiares);
        if (avaliacao.Decisao != DecisaoIncidente.Nada)
            decisoes.Add(new TransicaoSaude(componente, avaliacao.Decisao, severidade, avaliacao.DesdeUtc));
    }
}
