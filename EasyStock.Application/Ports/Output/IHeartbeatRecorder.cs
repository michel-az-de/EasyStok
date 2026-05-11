namespace EasyStock.Application.Ports.Output;

/// <summary>
/// Port pra gravar heartbeats dos hosted services do Worker. Implementações:
/// <list type="bullet">
///   <item><c>NoOpHeartbeatRecorder</c> (default) — usado pela API e por testes; não escreve.</item>
///   <item><c>PostgresHeartbeatRecorder</c> — registrado apenas no Worker (Program.cs).</item>
/// </list>
/// <para>
/// O contrato é "fire-and-forget tolerante a falhas": implementações NÃO devem propagar
/// exceções (heartbeat falho não pode quebrar o tick de produção do job).
/// </para>
/// </summary>
public interface IHeartbeatRecorder
{
    /// <param name="servico">Identificador estável do job (ex: "SlaMonitor", "Dispatcher-shard-0").</param>
    /// <param name="status">"OK" | "Erro" | "Skip".</param>
    /// <param name="detalhe">Mensagem curta opcional (em erro: tipo + 1ª linha).</param>
    /// <param name="itensProcessados">Contador opcional (mensagens despachadas, tickets etc).</param>
    /// <param name="duracaoMs">Duração do tick em ms (opcional).</param>
    Task RecordAsync(
        string servico,
        string status = "OK",
        string? detalhe = null,
        int? itensProcessados = null,
        int? duracaoMs = null,
        CancellationToken ct = default);
}
