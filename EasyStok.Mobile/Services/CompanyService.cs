using EasyStok.Mobile.Models;
using Microsoft.Extensions.Logging;
using System.Net.Http.Json;

namespace EasyStok.Mobile.Services;

/// <summary>
/// Cliente das rotas de empresa/loja autenticadas (consome /api/lojas).
/// Usa o HttpClient "easystok-api" (com AutenticacaoHandler), entao todo chamada
/// vai com Bearer + auto-refresh em 401.
/// </summary>
public sealed class EmpresaService : IEmpresaService
{
    private const string ClientName = "easystok-api";
    private readonly IHttpClientFactory _httpFactory;
    private readonly ILogger<EmpresaService> _logger;

    public EmpresaService(IHttpClientFactory httpFactory, ILogger<EmpresaService> logger)
    {
        _httpFactory = httpFactory;
        _logger = logger;
    }

    public async Task<IReadOnlyList<Loja>> ListLojasAsync(Guid empresaId, CancellationToken ct = default)
    {
        var http = _httpFactory.CreateClient(ClientName);
        try
        {
            var env = await http.GetFromJsonAsync<EnvelopeApi<List<Loja>>>(
                $"/api/lojas?empresaId={empresaId}", ct);
            return env?.Data ?? new List<Loja>();
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Falha ao listar lojas da empresa {EmpresaId}", empresaId);
            return Array.Empty<Loja>();
        }
    }
}

public interface IEmpresaService
{
    Task<IReadOnlyList<Loja>> ListLojasAsync(Guid empresaId, CancellationToken ct = default);
}
