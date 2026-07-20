using EasyStok.Mobile.Models;
using EasyStok.Mobile.Storage;
using Microsoft.Extensions.Logging;
using System.Net.Http.Json;
using System.Text.Json;

namespace EasyStok.Mobile.Services;

/// <summary>
/// Drena o outbox enviando cada mutation pra rota REST correspondente.
/// Idempotente o suficiente: itens 2xx sao removidos, falhas registram
/// erro e ficam pra proxima rodada. Single-flight via SemaphoreSlim
/// pra evitar concorrencia entre flush manual e periodico (F4b).
/// </summary>
public sealed class OutboxFlushService : IOutboxFlushService
{
	private const string ClientName = "easystok-api";
	private static readonly SemaphoreSlim _flushLock = new(1, 1);
	private static readonly JsonSerializerOptions _jsonOptions = new() { PropertyNameCaseInsensitive = true };

	private readonly IHttpClientFactory _httpFactory;
	private readonly IOutboxRepository _outbox;
	private readonly AppIdentity _identity;
	private readonly ILogger<OutboxFlushService> _logger;

	public OutboxFlushService(IHttpClientFactory httpFactory, IOutboxRepository outbox, AppIdentity identity, ILogger<OutboxFlushService> logger)
	{
		_httpFactory = httpFactory;
		_outbox = outbox;
		_identity = identity;
		_logger = logger;
	}

	public async Task<FlushReport> FlushAsync(CancellationToken ct = default)
	{
		if (!await _flushLock.WaitAsync(0, ct))
			return new FlushReport(0, 0, 0); // outra thread ja esta drenando

		try
		{
			if (Connectivity.Current.NetworkAccess != NetworkAccess.Internet)
				return new FlushReport(0, 0, 0);

			var http = _httpFactory.CreateClient(ClientName);
			var pending = await _outbox.GetPendingAsync(50);

			int sent = 0, failed = 0;

			foreach (var item in pending)
			{
				ct.ThrowIfCancellationRequested();

				try
				{
					var resp = await SendAsync(http, item, ct);
					if (resp.IsSuccessStatusCode)
					{
						await _outbox.DeleteAsync(item.Id);
						sent++;
					}
					else
					{
						var body = await resp.Content.ReadAsStringAsync(ct);
						await _outbox.RecordFailureAsync(item.Id, $"HTTP {(int)resp.StatusCode}: {Truncate(body, 200)}");
						failed++;
						// Para o flush no primeiro 4xx/5xx pra nao martelar — proxima
						// rodada tenta de novo apos delay.
						break;
					}
				}
				catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException)
				{
					_logger.LogWarning(ex, "Falha de rede no flush do item {Id}", item.Id);
					await _outbox.RecordFailureAsync(item.Id, ex.Message);
					failed++;
					break;
				}
			}

			return new FlushReport(pending.Count, sent, failed);
		}
		finally
		{
			_flushLock.Release();
		}
	}

	private async Task<HttpResponseMessage> SendAsync(HttpClient http, OutboxItem item, CancellationToken ct)
	{
		var path = item.Type switch
		{
			OutboxTypes.EstoqueEntrada => "/api/estoque/entrada",
			OutboxTypes.EstoqueSaida => "/api/estoque/saida",
			_ => throw new InvalidOperationException($"Outbox type desconhecido: {item.Type}"),
		};
		// JsonContent.Create<T>() por branch (nao um object? comum) -- serializar via
		// tipo declarado "object" perderia as propriedades do record concreto (STJ usa o
		// tipo estatico, nao o runtime, no overload generico).
		var content = item.Type switch
		{
			OutboxTypes.EstoqueEntrada => JsonContent.Create(JsonSerializer.Deserialize<RegistrarEntradaCommand>(item.PayloadJson, _jsonOptions), options: _jsonOptions),
			OutboxTypes.EstoqueSaida => JsonContent.Create(JsonSerializer.Deserialize<RegistrarSaidaCommand>(item.PayloadJson, _jsonOptions), options: _jsonOptions),
			_ => throw new InvalidOperationException($"Outbox type desconhecido: {item.Type}"),
		};

		using var request = new HttpRequestMessage(HttpMethod.Post, path) { Content = content };
		// issue 917 Fase B: item.Id e' um int AutoIncrement LOCAL (por aparelho) -- sem o
		// prefixo do device, dois aparelhos da mesma empresa colidiriam na unicidade
		// (Key, EmpresaId, MetodoRecurso) do servidor e o segundo receberia replay da
		// venda do primeiro em vez de processar a sua propria. Retry do MESMO item (falha
		// de rede -> proxima rodada) reusa a MESMA chave -- e o resultado desejado.
		request.Headers.TryAddWithoutValidation("Idempotency-Key", $"outbox:{_identity.DeviceId}:{item.Id}");
		request.Headers.TryAddWithoutValidation("X-Device-Id", _identity.DeviceId);

		return await http.SendAsync(request, ct);
	}

	private static string Truncate(string s, int max) =>
		string.IsNullOrEmpty(s) ? "" : (s.Length <= max ? s : s[..max]);
}

public interface IOutboxFlushService
{
	Task<FlushReport> FlushAsync(CancellationToken ct = default);
}

public sealed record FlushReport(int Pending, int Sent, int Failed);
