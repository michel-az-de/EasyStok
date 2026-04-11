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

    public async Task<DiagnosticoApiResult?> ObterDiagnosticoAsync()
    {
        try
        {
            var response = await httpClient.GetAsync("diagnostico");
            if (!response.IsSuccessStatusCode)
                return null;

            var json = await response.Content.ReadAsStringAsync();
            return JsonSerializer.Deserialize<DiagnosticoApiResult>(json, JsonOptions);
        }
        catch
        {
            return null;
        }
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
}

// DTOs espelhando a resposta da API
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
}

public sealed class BancoInfo
{
    public string Provider { get; set; } = "";
    public string ProviderConfigurado { get; set; } = "";
    public bool Fallback { get; set; }
    public string Conexao { get; set; } = "";
    public bool? MigrationsAplicadas { get; set; }
    public string? Erro { get; set; }
}

public sealed class RedisInfo
{
    public bool Configurado { get; set; }
    public string Conexao { get; set; } = "";
    public string? Erro { get; set; }
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
