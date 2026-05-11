using EasyStock.Application.Ports.Output;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using Npgsql;
using NpgsqlTypes;

namespace EasyStock.Infra.Postgre.Heartbeats;

/// <summary>
/// Grava o heartbeat via UPSERT (1 linha por <c>Servico</c>). Usa conexão raw
/// dedicada (<see cref="NpgsqlConnection"/>) pra evitar interferir com transações
/// do tick do hosted service e isolar da policy de retry do EF.
/// <para>
/// A implementação NÃO propaga exceções — gravar heartbeat não pode derrubar
/// produção. Falhas são logadas como warning e retorna normalmente.
/// </para>
/// </summary>
public sealed class PostgresHeartbeatRecorder(
    IConfiguration configuration,
    ILogger<PostgresHeartbeatRecorder> logger) : IHeartbeatRecorder
{
    private const string UpsertSql = """
        INSERT INTO worker_heartbeats
            ("Id", "Servico", "UltimoTickEm", "Status", "Detalhe",
             "ItensProcessados", "DuracaoMs", "CriadoEm", "AlteradoEm")
        VALUES (gen_random_uuid(), @servico, @tick, @status, @detalhe,
                @itens, @duracao, @now, @now)
        ON CONFLICT ("Servico") DO UPDATE SET
            "UltimoTickEm"     = EXCLUDED."UltimoTickEm",
            "Status"           = EXCLUDED."Status",
            "Detalhe"          = EXCLUDED."Detalhe",
            "ItensProcessados" = EXCLUDED."ItensProcessados",
            "DuracaoMs"        = EXCLUDED."DuracaoMs",
            "AlteradoEm"       = EXCLUDED."AlteradoEm";
        """;

    public async Task RecordAsync(
        string servico,
        string status = "OK",
        string? detalhe = null,
        int? itensProcessados = null,
        int? duracaoMs = null,
        CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(servico)) return;

        var connStr = configuration.GetConnectionString("DefaultConnection");
        if (string.IsNullOrWhiteSpace(connStr)) return;

        try
        {
            var now = DateTime.UtcNow;
            await using var conn = new NpgsqlConnection(connStr);
            await conn.OpenAsync(ct);
            await using var cmd = new NpgsqlCommand(UpsertSql, conn);
            cmd.Parameters.AddWithValue("servico", NpgsqlDbType.Varchar, servico);
            cmd.Parameters.AddWithValue("tick", NpgsqlDbType.TimestampTz, now);
            cmd.Parameters.AddWithValue("status", NpgsqlDbType.Varchar, status ?? "OK");
            cmd.Parameters.AddWithValue("detalhe",
                NpgsqlDbType.Varchar, (object?)Trunc(detalhe, 500) ?? DBNull.Value);
            cmd.Parameters.AddWithValue("itens",
                NpgsqlDbType.Integer, (object?)itensProcessados ?? DBNull.Value);
            cmd.Parameters.AddWithValue("duracao",
                NpgsqlDbType.Integer, (object?)duracaoMs ?? DBNull.Value);
            cmd.Parameters.AddWithValue("now", NpgsqlDbType.TimestampTz, now);
            await cmd.ExecuteNonQueryAsync(ct);
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested)
        {
            // Shutdown limpo — não logar.
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex,
                "Falha ao gravar heartbeat de '{Servico}' — ignorando (heartbeat eh best-effort).",
                servico);
        }
    }

    private static string? Trunc(string? s, int max) =>
        string.IsNullOrEmpty(s) ? s : (s.Length <= max ? s : s[..max]);
}
