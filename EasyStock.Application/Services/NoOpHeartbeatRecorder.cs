using EasyStock.Application.Ports.Output;

namespace EasyStock.Application.Services;

/// <summary>
/// Implementação default que não grava nada. Registrada na API (que compartilha
/// hosted services via Mode=Hosted opcional) pra evitar erros de DI sem produzir
/// heartbeats — o Worker é a fonte da verdade do dashboard.
/// </summary>
public sealed class NoOpHeartbeatRecorder : IHeartbeatRecorder
{
    public Task RecordAsync(
        string servico,
        string status = "OK",
        string? detalhe = null,
        int? itensProcessados = null,
        int? duracaoMs = null,
        CancellationToken ct = default) => Task.CompletedTask;
}
