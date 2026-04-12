using System.Diagnostics;
using System.Net.Http.Headers;
using System.Text.Json;

namespace EasyStock.Web.Services;

public sealed class DiagnosticoWebService(HttpClient httpClient, IConfiguration configuration)
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        PropertyNameCaseInsensitive = true
    };

    public string ApiBaseUrl => configuration["ApiSettings:BaseUrl"] ?? "não configurado";

    public async Task<(DiagnosticoApiResult? Result, long LatenciaMs)> ObterDiagnosticoComLatenciaAsync()
    {
        var sw = Stopwatch.StartNew();
        try
        {
            var response = await httpClient.GetAsync("diagnostico");
            sw.Stop();

            if (!response.IsSuccessStatusCode)
                return (null, sw.ElapsedMilliseconds);

            var json = await response.Content.ReadAsStringAsync();
            var result = JsonSerializer.Deserialize<DiagnosticoApiResult>(json, JsonOptions);
            return (result, sw.ElapsedMilliseconds);
        }
        catch
        {
            sw.Stop();
            return (null, sw.ElapsedMilliseconds);
        }
    }

    public async Task<DiagnosticoApiResult?> ObterDiagnosticoAsync()
    {
        var (result, _) = await ObterDiagnosticoComLatenciaAsync();
        return result;
    }

    public async Task<bool> PingApiAsync()
    {
        try
        {
            var response = await httpClient.GetAsync("diagnostico/ping");
            return response.IsSuccessStatusCode;
        }
        catch
        {
            return false;
        }
    }

    public async Task<LogsApiResult?> FetchLogsAsync(string bearerToken, int n = 100)
    {
        try
        {
            var request = new HttpRequestMessage(HttpMethod.Get, $"diagnostico/logs?n={n}");
            request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", bearerToken);
            var response = await httpClient.SendAsync(request);

            if (!response.IsSuccessStatusCode)
                return null;

            var json = await response.Content.ReadAsStringAsync();
            return JsonSerializer.Deserialize<LogsApiResult>(json, JsonOptions);
        }
        catch
        {
            return null;
        }
    }

    // ──────────────────────────────────────────────────────────────────────
    // Novos métodos — Central de Operações Inteligente
    // ──────────────────────────────────────────────────────────────────────

    public async Task<EnhancedLogsWebResult?> FetchEnhancedLogsAsync(string bearerToken, int hours = 24)
    {
        try
        {
            var request = new HttpRequestMessage(HttpMethod.Get, $"diagnostico/logs/enhanced?hours={hours}");
            request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", bearerToken);
            var response = await httpClient.SendAsync(request);

            if (!response.IsSuccessStatusCode)
                return null;

            var json = await response.Content.ReadAsStringAsync();
            return JsonSerializer.Deserialize<EnhancedLogsWebResult>(json, JsonOptions);
        }
        catch
        {
            return null;
        }
    }

    public async Task<EndpointsTestWebResponse?> FetchEndpointTestsAsync()
    {
        try
        {
            var response = await httpClient.GetAsync("diagnostico/endpoints");

            if (!response.IsSuccessStatusCode)
                return null;

            var json = await response.Content.ReadAsStringAsync();
            return JsonSerializer.Deserialize<EndpointsTestWebResponse>(json, JsonOptions);
        }
        catch
        {
            return null;
        }
    }

    public async Task<HealthHistoryWebResponse?> FetchHealthHistoryAsync()
    {
        try
        {
            var response = await httpClient.GetAsync("diagnostico/historico");

            if (!response.IsSuccessStatusCode)
                return null;

            var json = await response.Content.ReadAsStringAsync();
            return JsonSerializer.Deserialize<HealthHistoryWebResponse>(json, JsonOptions);
        }
        catch
        {
            return null;
        }
    }
}

// ──────────────────────────────────────────────────────────────────────────
// DTOs espelhando a resposta da API — Existentes
// ──────────────────────────────────────────────────────────────────────────

public sealed class DiagnosticoApiResult
{
    public string Status { get; set; } = "";
    public DateTimeOffset Timestamp { get; set; }
    public string Ambiente { get; set; } = "";
    public string Uptime { get; set; } = "";
    public string Versao { get; set; } = "";
    public BancoInfo Banco { get; set; } = new();
    public RedisInfo Redis { get; set; } = new();
    public SmtpInfo Smtp { get; set; } = new();
    public StorageInfo Storage { get; set; } = new();
    public IaInfo Ia { get; set; } = new();
    public ConfigInfo Configuracoes { get; set; } = new();
    public List<CausaProvavelInfo> CausasProvaveis { get; set; } = [];
}

public sealed class BancoInfo
{
    public string Provider { get; set; } = "";
    public string ProviderConfigurado { get; set; } = "";
    public bool Fallback { get; set; }
    public string Conexao { get; set; } = "";
    public bool? MigrationsAplicadas { get; set; }
    public string? Erro { get; set; }
    public long LatenciaMs { get; set; }
    public string? CausaProvavel { get; set; }
}

public sealed class RedisInfo
{
    public bool Configurado { get; set; }
    public string Conexao { get; set; } = "";
    public string? Erro { get; set; }
    public long LatenciaMs { get; set; }
    public string? CausaProvavel { get; set; }
}

public sealed class SmtpInfo
{
    public bool Configurado { get; set; }
    public string Tipo { get; set; } = "";
    public string? Host { get; set; }
}

public sealed class StorageInfo
{
    public string Provider { get; set; } = "";
    public bool Configurado { get; set; }
    public bool? DiretorioExiste { get; set; }
}

public sealed class IaInfo
{
    public bool Habilitado { get; set; }
    public bool ApiKeyPresente { get; set; }
}

public sealed class ConfigInfo
{
    public bool JwtSecretPresente { get; set; }
    public bool? JwtSecretSeguro { get; set; }
    public string[] CorsOrigins { get; set; } = [];
    public bool ConnectionStringPresente { get; set; }
}

public sealed class CausaProvavelInfo
{
    public string Componente { get; set; } = "";
    public string Severidade { get; set; } = "warning";
    public string Descricao { get; set; } = "";
    public string Sugestao { get; set; } = "";
}

public sealed class LogsApiResult
{
    public bool Disponivel { get; set; }
    public string? Motivo { get; set; }
    public string? Arquivo { get; set; }
    public int TotalLinhas { get; set; }
    public LogEntryInfo[] Entradas { get; set; } = [];
}

public sealed class LogEntryInfo
{
    public DateTimeOffset Timestamp { get; set; }
    public string Level { get; set; } = "";
    public string Message { get; set; } = "";
}

// ──────────────────────────────────────────────────────────────────────────
// DTOs — Central de Operações Inteligente
// ──────────────────────────────────────────────────────────────────────────

public sealed class EnhancedLogsWebResult
{
    public bool Disponivel { get; set; }
    public string? Motivo { get; set; }
    public DateTimeOffset QueryTimestamp { get; set; }
    public int PeriodoHoras { get; set; }
    public int TotalEntries { get; set; }
    public EnhancedLogEntryInfo[] Entradas { get; set; } = [];
    public LogSummaryInfo Resumo { get; set; } = new();
    public DetectedPatternInfo[] Padroes { get; set; } = [];
}

public sealed class EnhancedLogEntryInfo
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
}

public sealed class LogSummaryInfo
{
    public int TotalRequests { get; set; }
    public int TotalErrors { get; set; }
    public int TotalWarnings { get; set; }
    public double AvgResponseTimeMs { get; set; }
    public Dictionary<string, int> ErrorsByEndpoint { get; set; } = new();
    public Dictionary<string, int> RequestsByHour { get; set; } = new();
    public Dictionary<string, int> ErrorsByHour { get; set; } = new();
}

public sealed class DetectedPatternInfo
{
    public string Tipo { get; set; } = "";
    public string Severidade { get; set; } = "info";
    public string Descricao { get; set; } = "";
    public string Sugestao { get; set; } = "";
    public int Ocorrencias { get; set; }
    public DateTimeOffset? PrimeiraOcorrencia { get; set; }
    public DateTimeOffset? UltimaOcorrencia { get; set; }
}

public sealed class EndpointTestWebResult
{
    public string Rota { get; set; } = "";
    public string Metodo { get; set; } = "GET";
    public int StatusCode { get; set; }
    public long LatenciaMs { get; set; }
    public string Status { get; set; } = "ok";
    public string? Erro { get; set; }
    public DateTimeOffset TestadoEm { get; set; }
}

public sealed class EndpointsTestWebResponse
{
    public EndpointTestWebResult[] Resultados { get; set; } = [];
    public int Saudaveis { get; set; }
    public int Lentos { get; set; }
    public int Falhas { get; set; }
    public DateTimeOffset TestadoEm { get; set; }
}

public sealed class HealthSnapshotInfo
{
    public DateTimeOffset Timestamp { get; set; }
    public long DbLatencyMs { get; set; }
    public string DbStatus { get; set; } = "ok";
    public long? RedisLatencyMs { get; set; }
    public string? RedisStatus { get; set; }
    public int ErrorCount { get; set; }
    public string OverallStatus { get; set; } = "ok";
}

public sealed class HealthHistoryWebResponse
{
    public HealthSnapshotInfo[] Snapshots { get; set; } = [];
    public DateTimeOffset Desde { get; set; }
    public int Total { get; set; }
}
