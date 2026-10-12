namespace EasyStock.Api.Services.Operacao;

/// <summary>
/// Laço de escrita do SSE de operação (S18): um único escritor na resposta, que manda os eventos da fila
/// assim que chegam e um comentário <c>: heartbeat</c> quando a fila fica parada pelo intervalo informado
/// (proxies derrubam conexão ociosa acima de 30 s).
/// <para>
/// #1352 (N7): o stream não vive mais que o JWT que o abriu. Ele fecha ao chegar no <c>exp</c> do token (sem a
/// tolerância de 5 min do ClockSkew) e quando o corte de sessão do usuário o alcança, conferido a cada
/// <c>intervaloHeartbeat</c> pelo relógio, haja ou não evento na fila: com pedido entrando o tempo todo o
/// heartbeat nunca sai, e a conferência não pode depender dele. Fechado o stream, o cliente reconecta, recebe
/// 401 e o console volta ao login.
/// </para>
/// </summary>
public static class TransmissaoSse
{
    public const string Heartbeat = ": heartbeat\n\n";

    /// <param name="expiraEm">Instante do <c>exp</c> do JWT; nulo não limita o stream.</param>
    /// <param name="sessaoAindaValida">Confere o corte de sessão; devolve falso quando revogou. Nulo não confere.</param>
    /// <param name="relogio">Mede <paramref name="expiraEm"/> e o intervalo entre conferências.</param>
    public static async Task TransmitirAsync(
        HttpResponse response,
        OperacaoEventBroker.ListenerSlot slot,
        TimeSpan intervaloHeartbeat,
        DateTimeOffset? expiraEm,
        Func<CancellationToken, Task<bool>>? sessaoAindaValida,
        TimeProvider relogio,
        CancellationToken ct)
    {
        ArgumentOutOfRangeException.ThrowIfLessThanOrEqual(intervaloHeartbeat, TimeSpan.Zero);

        try
        {
            // Os marcos de tempo nascem antes do primeiro byte: quem espera o "ready" para mexer no relógio
            // (os testes) nunca corre contra a leitura inicial.
            var ultimaEscrita = relogio.GetUtcNow();
            var ultimaConferencia = ultimaEscrita;

            // Confirmação inicial para o cliente tratar a conexão como aberta.
            await response.WriteAsync("event: ready\ndata: {}\n\n", ct);
            await response.Body.FlushAsync(ct);

            while (!ct.IsCancellationRequested && !slot.Cancelled)
            {
                var agora = relogio.GetUtcNow();
                if (expiraEm is { } exp && agora >= exp)
                    break; // JWT vencido

                if (sessaoAindaValida is not null && agora >= ultimaConferencia + intervaloHeartbeat)
                {
                    if (!await sessaoAindaValida(ct))
                        break; // o corte de sessão alcançou o token
                    ultimaConferencia = agora;
                }

                // Dorme até o primeiro prazo: heartbeat, conferência ou exp. Nunca mais que o intervalo.
                var proximoHeartbeat = ultimaEscrita + intervaloHeartbeat;
                var prazo = proximoHeartbeat;
                if (sessaoAindaValida is not null && ultimaConferencia + intervaloHeartbeat < prazo)
                    prazo = ultimaConferencia + intervaloHeartbeat;
                if (expiraEm is { } vencimento && vencimento < prazo)
                    prazo = vencimento;
                var espera = prazo > agora ? prazo - agora : TimeSpan.Zero;

                if (await slot.Signal.WaitAsync(espera, ct))
                {
                    while (slot.Queue.TryDequeue(out var mensagem))
                        await response.WriteAsync(mensagem.ParaFrame(), ct);
                    await response.Body.FlushAsync(ct);
                    ultimaEscrita = relogio.GetUtcNow();
                }
                else if (relogio.GetUtcNow() >= proximoHeartbeat)
                {
                    // Fila parada pelo intervalo inteiro: heartbeat. Acordar só para conferir ou vencer não conta.
                    await response.WriteAsync(Heartbeat, ct);
                    await response.Body.FlushAsync(ct);
                    ultimaEscrita = relogio.GetUtcNow();
                }
            }
        }
        catch (OperationCanceledException)
        {
            // Cliente desconectou: fim normal do stream.
        }
    }
}
