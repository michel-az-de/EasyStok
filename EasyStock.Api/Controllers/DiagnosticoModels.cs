namespace EasyStock.Api.Controllers;

// ──────────────────────────────────────────────────────────────────────────
// DTOs — Existentes
// ──────────────────────────────────────────────────────────────────────────

public sealed class DiagnosticoResult
{
    public string Status { get; set; } = "ok";
    public DateTimeOffset Timestamp { get; set; }
    public string Ambiente { get; set; } = "";
    public string Uptime { get; set; } = "";
    public string Versao { get; set; } = "";
    public BancoStatus Banco { get; set; } = new();
    public RedisStatus Redis { get; set; } = new();
    public SmtpStatus Smtp { get; set; } = new();
    public StorageStatus Storage { get; set; } = new();
    public IaStatus Ia { get; set; } = new();
    public ConfiguracoesStatus Configuracoes { get; set; } = new();
    public List<CausaProvavel> CausasProvaveis { get; set; } = [];
}

public sealed class BancoStatus
{
    public string Provider { get; set; } = "";
    public string ProviderConfigurado { get; set; } = "";
    public bool Fallback { get; set; }
    public string Conexao { get; set; } = "ok";
    public bool? MigrationsAplicadas { get; set; }
    public string? Erro { get; set; }
    public long LatenciaMs { get; set; }
    public string? CausaProvavel { get; set; }
}

public sealed class RedisStatus
{
    public bool Configurado { get; set; }
    public string Conexao { get; set; } = "ok";
    public string? Erro { get; set; }
    public long LatenciaMs { get; set; }
    public string? CausaProvavel { get; set; }
}

public sealed class SmtpStatus
{
    public bool Configurado { get; set; }
    public string Tipo { get; set; } = "";
    public string? Host { get; set; }
}

public sealed class StorageStatus
{
    public string Provider { get; set; } = "";
    public bool Configurado { get; set; }
    public bool? DiretorioExiste { get; set; }
    public string? Erro { get; set; }
}

public sealed class IaStatus
{
    public bool Habilitado { get; set; }
    public bool ApiKeyPresente { get; set; }
}

public sealed class ConfiguracoesStatus
{
    public bool JwtSecretPresente { get; set; }
    public bool? JwtSecretSeguro { get; set; }
    public string[] CorsOrigins { get; set; } = [];
    public bool ConnectionStringPresente { get; set; }
}

public sealed class CausaProvavel
{
    public string Componente { get; set; } = "";
    public string Severidade { get; set; } = "warning";
    public string Descricao { get; set; } = "";
    public string Sugestao { get; set; } = "";
}

public sealed class LogsInfo
{
    public bool Disponivel { get; set; }
    public string? Motivo { get; set; }
    public string? Arquivo { get; set; }
    public int TotalLinhas { get; set; }
    public LogEntry[] Entradas { get; set; } = [];
}

public sealed class LogEntry
{
    public DateTimeOffset Timestamp { get; set; }
    public string Level { get; set; } = "";
    public string Message { get; set; } = "";
}

// ──────────────────────────────────────────────────────────────────────────
// DTOs — Central de Operações Inteligente
// ──────────────────────────────────────────────────────────────────────────

public sealed class EnhancedLogsResult
{
    public bool Disponivel { get; set; }
    public string? Motivo { get; set; }
    public DateTimeOffset QueryTimestamp { get; set; }
    public int PeriodoHoras { get; set; }
    public int TotalEntries { get; set; }
    public EnhancedLogEntry[] Entradas { get; set; } = [];
    public LogSummary Resumo { get; set; } = new();
    public DetectedPattern[] Padroes { get; set; } = [];
}

public sealed class EnhancedLogEntry
{
    public DateTimeOffset Timestamp { get; set; }
    public string Level { get; set; } = "";
    public string Message { get; set; } = "";
    public string? CorrelationId { get; set; }
    public string? Endpoint { get; set; }
    public string? HttpMethod { get; set; }
    public int? StatusCode { get; set; }
    public double? ElapsedMs { get; set; }
    public string? Exception { get; set; }
    public string Categoria { get; set; } = "general";
    public string? ClientIp { get; set; }
    public string? UserId { get; set; }
    public string? EmpresaId { get; set; }
}

public sealed class LogSummary
{
    public int TotalRequests { get; set; }
    public int TotalErrors { get; set; }
    public int TotalWarnings { get; set; }
    public double AvgResponseTimeMs { get; set; }
    public Dictionary<string, int> ErrorsByEndpoint { get; set; } = new();
    public Dictionary<string, int> RequestsByHour { get; set; } = new();
    public Dictionary<string, int> ErrorsByHour { get; set; } = new();
}

public sealed class DetectedPattern
{
    public string Tipo { get; set; } = "";
    public string Severidade { get; set; } = "info";
    public string Descricao { get; set; } = "";
    public string Sugestao { get; set; } = "";
    public int Ocorrencias { get; set; }
    public DateTimeOffset? PrimeiraOcorrencia { get; set; }
    public DateTimeOffset? UltimaOcorrencia { get; set; }
    /// <summary>ID estável para ack de alertas (SHA256 truncado).</summary>
    public string AlertaId { get; set; } = "";
}

public sealed class EndpointTestResult
{
    public string Rota { get; set; } = "";
    public string Metodo { get; set; } = "GET";
    public int StatusCode { get; set; }
    public long LatenciaMs { get; set; }
    public string Status { get; set; } = "ok";
    public string? Erro { get; set; }
    public DateTimeOffset TestadoEm { get; set; }
}

public sealed class EndpointsTestResponse
{
    public EndpointTestResult[] Resultados { get; set; } = [];
    public int Saudaveis { get; set; }
    public int Lentos { get; set; }
    public int Degradados { get; set; }
    public int Falhas { get; set; }
    public DateTimeOffset TestadoEm { get; set; }
}

public sealed class HealthHistoryResponse
{
    public EasyStock.Api.BackgroundServices.HealthSnapshot[] Snapshots { get; set; } = [];
    public DateTimeOffset Desde { get; set; }
    public int Total { get; set; }
}

// ── DTOs novos ────────────────────────────────────────────────────────────────

public sealed class AckAlertaRequest
{
    public string Status { get; set; } = "";
    public string? Observacao { get; set; }
}

public sealed class AlertaAck
{
    public string AlertaId { get; set; } = "";
    public string Status { get; set; } = "";
    public string? Observacao { get; set; }
    public DateTimeOffset AtualizadoEm { get; set; }
}
