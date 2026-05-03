using System.Text;
using EasyStock.Application.Ports.Output;
using EasyStock.Application.Ports.Output.Persistence;
using EasyStock.Domain.Entities;

namespace EasyStock.Api.Middleware;

/// <summary>
/// Middleware que implementa idempotencia em POSTs criticos (entrada/saida/
/// estorno/venda). Cliente envia header Idempotency-Key (UUID); requests
/// repetidos com a mesma chave + empresa + recurso retornam a resposta
/// original sem reaplicar efeitos colaterais (R5: duplicidade por retry
/// de mobile/web sob rede instavel).
///
/// Aplicado SOMENTE em rotas registradas via <see cref="IdempotencyOptions"/>
/// (whitelist explicita) — nao queremos cachear por header em endpoints
/// idempotentes por natureza (GET/PUT) ou onde duplicidade e' aceitavel.
///
/// TTL: 24h. Cleanup periodico via <see cref="IIdempotencyKeyRepository.CleanupExpiredAsync"/>.
/// </summary>
public sealed class IdempotencyMiddleware(RequestDelegate next, IdempotencyOptions options, ILogger<IdempotencyMiddleware> logger)
{
    private const string HeaderName = "Idempotency-Key";
    private static readonly TimeSpan Ttl = TimeSpan.FromHours(24);

    public async Task InvokeAsync(HttpContext context)
    {
        // So aplica em metodos com efeitos colaterais.
        if (!HttpMethods.IsPost(context.Request.Method))
        {
            await next(context);
            return;
        }

        var path = context.Request.Path.Value ?? string.Empty;
        if (!options.PathMatchesAny(path))
        {
            await next(context);
            return;
        }

        // Header opcional — sem ele, fluxo segue normal (sem cache).
        if (!context.Request.Headers.TryGetValue(HeaderName, out var keyValues))
        {
            await next(context);
            return;
        }

        var key = keyValues.ToString().Trim();
        if (string.IsNullOrWhiteSpace(key) || key.Length > 120)
        {
            context.Response.StatusCode = StatusCodes.Status400BadRequest;
            await context.Response.WriteAsync($"Header {HeaderName} invalido (vazio ou >120 chars).");
            return;
        }

        var currentUser = context.RequestServices.GetRequiredService<ICurrentUserAccessor>();
        var empresaId = currentUser.EmpresaId;
        if (empresaId == Guid.Empty)
        {
            // Sem empresa identificada (ex.: rota publica), fluxo segue normal.
            await next(context);
            return;
        }

        var metodoRecurso = $"{context.Request.Method} {NormalizePath(path)}";
        var repo = context.RequestServices.GetRequiredService<IIdempotencyKeyRepository>();

        var existing = await repo.GetActiveAsync(key, empresaId, metodoRecurso, context.RequestAborted);
        if (existing is not null)
        {
            // Replay: devolve resposta original sem executar handler.
            context.Response.StatusCode = existing.HttpStatus;
            context.Response.Headers["X-Idempotent-Replay"] = "true";
            if (!string.IsNullOrEmpty(existing.RespostaJson))
            {
                context.Response.ContentType = "application/json";
                await context.Response.WriteAsync(existing.RespostaJson);
            }
            return;
        }

        // Captura body da resposta para serializar no cache.
        var originalBody = context.Response.Body;
        await using var buffer = new MemoryStream();
        context.Response.Body = buffer;

        try
        {
            await next(context);
        }
        finally
        {
            buffer.Seek(0, SeekOrigin.Begin);
            await buffer.CopyToAsync(originalBody);
            context.Response.Body = originalBody;
        }

        // Cacheia somente respostas bem-sucedidas (2xx).
        if (context.Response.StatusCode is >= 200 and < 300)
        {
            buffer.Seek(0, SeekOrigin.Begin);
            string? respostaJson = null;
            if (buffer.Length > 0 && buffer.Length <= 64 * 1024) // limite 64KB
            {
                respostaJson = Encoding.UTF8.GetString(buffer.ToArray());
            }

            var entry = IdempotencyKey.Criar(key, empresaId, metodoRecurso, context.Response.StatusCode, respostaJson, Ttl);
            try
            {
                await repo.SaveAsync(entry, context.RequestAborted);
            }
            catch (Exception ex)
            {
                // Falha ao persistir cache nao deve invalidar a operacao
                // ja completada — seguinte request fara o mesmo trabalho.
                // Logar como warning porque pode mascarar duplicacoes futuras.
                logger.LogWarning(ex, "Falha ao persistir idempotency key. Retry pode reaplicar efeitos.");
            }
        }
    }

    private static string NormalizePath(string path)
    {
        // Normaliza GUIDs no path para padrao "{id}" — assim retry com
        // mesmo body em mesmo recurso reusa a chave mesmo que IDs sejam
        // diferentes em outros endpoints semanticamente equivalentes.
        // (Conservador: por agora retornamos path original; refinar quando
        // necessario.)
        return path;
    }
}

/// <summary>
/// Configuracao do <see cref="IdempotencyMiddleware"/>: lista de prefixos
/// de path que devem ser cobertos.
/// </summary>
public sealed class IdempotencyOptions
{
    private readonly List<string> _prefixes = new();

    public IdempotencyOptions Add(string pathPrefix)
    {
        if (!string.IsNullOrWhiteSpace(pathPrefix))
            _prefixes.Add(pathPrefix.TrimEnd('/').ToLowerInvariant());
        return this;
    }

    public bool PathMatchesAny(string path)
    {
        if (_prefixes.Count == 0) return false;
        var lower = path.ToLowerInvariant();
        foreach (var p in _prefixes)
        {
            if (lower.StartsWith(p, StringComparison.Ordinal))
                return true;
        }
        return false;
    }
}

public static class IdempotencyMiddlewareExtensions
{
    public static IApplicationBuilder UseIdempotency(this IApplicationBuilder app, Action<IdempotencyOptions> configure)
    {
        var options = new IdempotencyOptions();
        configure(options);
        return app.UseMiddleware<IdempotencyMiddleware>(options);
    }
}
