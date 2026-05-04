using EasyStok.Mobile.Models;
using EasyStok.Mobile.Storage;
using Microsoft.Extensions.Logging;
using System.Net.Http.Json;

namespace EasyStok.Mobile.Services;

/// <summary>
/// Orquestra o consumo de <c>GET /api/estoque?empresaId=&amp;page=</c>.
/// Faz pull paginado do backend e popula o cache local SQLite. UI le do
/// cache. Offline-first: se rede falha, cache eh devolvido mesmo assim.
/// </summary>
public sealed class EstoqueService : IEstoqueService
{
	private const string ClientName = "easystok-api";
	private const int PageSize = 100;

	private readonly IHttpClientFactory _httpFactory;
	private readonly IEstoqueCache _cache;
	private readonly ILogger<EstoqueService> _logger;

	public EstoqueService(IHttpClientFactory httpFactory, IEstoqueCache cache, ILogger<EstoqueService> logger)
	{
		_httpFactory = httpFactory;
		_cache = cache;
		_logger = logger;
	}

	public Task<IReadOnlyList<CachedItemEstoque>> GetCachedAsync(Guid empresaId) =>
		_cache.GetAllAsync(empresaId);

	public async Task<RefreshResult> RefreshAsync(Guid empresaId, CancellationToken ct = default)
	{
		var http = _httpFactory.CreateClient(ClientName);
		var page = 1;
		var totalImported = 0;

		try
		{
			while (true)
			{
				ct.ThrowIfCancellationRequested();
				var url = $"/api/estoque?empresaId={empresaId}&page={page}&pageSize={PageSize}";
				var env = await http.GetFromJsonAsync<PagedEnvelope<ItemEstoqueDto>>(url, ct);
				if (env is null || env.Data is null || env.Data.Count == 0)
					break;

				await _cache.UpsertManyAsync(env.Data, empresaId);
				totalImported += env.Data.Count;

				if (page >= env.Meta.Pages || env.Data.Count < env.Meta.Limit)
					break;

				page++;
			}

			return RefreshResult.Ok(totalImported);
		}
		catch (HttpRequestException ex)
		{
			_logger.LogWarning(ex, "Falha de rede ao atualizar estoque");
			return RefreshResult.Fail("Sem conexao com o servidor. Mostrando dados em cache.");
		}
		catch (TaskCanceledException) when (ct.IsCancellationRequested)
		{
			return RefreshResult.Fail("Atualizacao cancelada.");
		}
		catch (Exception ex)
		{
			_logger.LogError(ex, "Erro inesperado no refresh de estoque");
			return RefreshResult.Fail("Erro ao atualizar. Mostrando dados em cache.");
		}
	}
}

public interface IEstoqueService
{
	Task<IReadOnlyList<CachedItemEstoque>> GetCachedAsync(Guid empresaId);
	Task<RefreshResult> RefreshAsync(Guid empresaId, CancellationToken ct = default);
}

public sealed record RefreshResult(bool Success, int Imported, string? Error)
{
	public static RefreshResult Ok(int imported) => new(true, imported, null);
	public static RefreshResult Fail(string error) => new(false, 0, error);
}
