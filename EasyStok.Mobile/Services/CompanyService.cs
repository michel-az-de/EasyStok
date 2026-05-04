using EasyStok.Mobile.Models;
using Microsoft.Extensions.Logging;
using System.Net.Http.Json;

namespace EasyStok.Mobile.Services;

/// <summary>
/// Cliente das rotas de empresa/loja autenticadas (consome /api/lojas).
/// Usa o HttpClient "easystok-api" (com AuthHandler), entao todo chamada
/// vai com Bearer + auto-refresh em 401.
/// </summary>
public sealed class CompanyService : ICompanyService
{
	private const string ClientName = "easystok-api";
	private readonly IHttpClientFactory _httpFactory;
	private readonly ILogger<CompanyService> _logger;

	public CompanyService(IHttpClientFactory httpFactory, ILogger<CompanyService> logger)
	{
		_httpFactory = httpFactory;
		_logger = logger;
	}

	public async Task<IReadOnlyList<LojaDto>> ListLojasAsync(Guid empresaId, CancellationToken ct = default)
	{
		var http = _httpFactory.CreateClient(ClientName);
		try
		{
			var env = await http.GetFromJsonAsync<ApiEnvelope<List<LojaDto>>>(
				$"/api/lojas?empresaId={empresaId}", ct);
			return env?.Data ?? new List<LojaDto>();
		}
		catch (Exception ex)
		{
			_logger.LogWarning(ex, "Falha ao listar lojas da empresa {EmpresaId}", empresaId);
			return Array.Empty<LojaDto>();
		}
	}
}

public interface ICompanyService
{
	Task<IReadOnlyList<LojaDto>> ListLojasAsync(Guid empresaId, CancellationToken ct = default);
}
