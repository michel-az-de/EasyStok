using System.Security.Cryptography;
using System.Text;

namespace EasyStock.Api.Middleware;

/// <summary>
/// Middleware que implementa idempotencia em POSTs criticos (entrada/saida/
/// estorno/venda/pagamento). Cliente envia header Idempotency-Key (UUID); requests
/// repetidos com a mesma chave + empresa + recurso retornam a resposta
/// original sem reaplicar efeitos colaterais (R5: duplicidade por retry
/// de mobile/web sob rede instavel).
///
/// <para>
/// issue 917 Fase B: modelo INSERT-first. A Fase A (whitelist morta corrigida em
/// #920) so evitava retry de TRANSPORTE — o TOCTOU original (<c>GetActiveAsync</c>
/// ANTES de qualquer <c>SaveAsync</c>) deixava dois requests concorrentes com a MESMA
/// chave passarem ambos. Agora o INSERT em si (<c>TryBeginAsync</c>) e o ponto de
/// serializacao, via o indice unico <c>ux_idempotency_key_empresa_recurso</c>.
/// </para>
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
    private const int MaxBufferBytes = 1 * 1024 * 1024; // 1MB hard cap por request — evita OOM em response excepcionalmente grande.
    private const int CacheMaxBytes = 64 * 1024;        // 64KB cacheado em DB.
    private const int MaxPayloadHashBytes = 256 * 1024; // acima disso nao calcula hash (custo), so nao dedup fino.
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

        if (!context.Request.Headers.TryGetValue(HeaderName, out var keyValues))
        {
            // issue 917: degrau 1 do plano de header-obrigatorio em 3 degraus —
            // observabilidade sem quebra. Loga + sinaliza no header de resposta;
            // exigir o header (428) fica pra outro PR, quando os logs mostrarem
            // quem sao os clientes que ainda nao enviam.
            context.Response.OnStarting(static state =>
            {
                var ctx = (HttpContext)state;
                ctx.Response.Headers["X-Idempotency-Missing"] = "true";
                return Task.CompletedTask;
            }, context);
            logger.LogWarning("POST {Path} em rota idempotente sem header {Header}.", path, HeaderName);
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

        var payloadHash = await ComputePayloadHashAsync(context.Request, context.RequestAborted);

        var (entrada, reservado) = await repo.TryBeginAsync(key, empresaId, metodoRecurso, payloadHash, Ttl, context.RequestAborted);

        Guid idParaFinalizar;
        if (reservado)
        {
            idParaFinalizar = entrada.Id;
        }
        else
        {
            switch (entrada.Status)
            {
                case StatusIdempotencyKey.Concluido:
                    // issue 917: chave Concluido com corpo DIFERENTE do que a gerou seria
                    // replay silencioso de uma resposta de OUTRA operacao — classe de bug
                    // pior que a duplicata original. So faz replay se o payload bater.
                    if (payloadHash is not null && !string.Equals(entrada.PayloadHash, payloadHash, StringComparison.Ordinal))
                    {
                        await WriteMismatchAsync(context, key);
                        return;
                    }
                    await ReplayAsync(context, entrada);
                    return;

                case StatusIdempotencyKey.Pendente:
                    var agora = DateTime.UtcNow;
                    if (!entrada.LeaseExpirado(agora))
                    {
                        // Outra transacao com a MESMA chave ainda esta processando. 409 em
                        // vez de esperar: segurar a request prende thread/conexao do pool
                        // exatamente sob a rajada concorrente que motivou este fix.
                        await WriteInFlightAsync(context);
                        return;
                    }
                    // Lease expirado (processo morreu antes de finalizar) — CAS takeover.
                    var tomou = await repo.TryTakeoverAsync(entrada.Id, entrada.LockedAtUtc, agora, context.RequestAborted);
                    if (!tomou)
                    {
                        // Outro concorrente tomou o lease entre o SELECT e este CAS.
                        await WriteInFlightAsync(context);
                        return;
                    }
                    idParaFinalizar = entrada.Id;
                    break;

                case StatusIdempotencyKey.Falhou:
                    // Cliente corrigiu o payload e tenta de novo com a MESMA chave — reabre.
                    var reabriu = await repo.TryReabrirAsync(entrada.Id, payloadHash, DateTime.UtcNow, context.RequestAborted);
                    if (!reabriu)
                    {
                        await WriteInFlightAsync(context);
                        return;
                    }
                    idParaFinalizar = entrada.Id;
                    break;

                default:
                    idParaFinalizar = entrada.Id;
                    break;
            }
        }

        // Captura body da resposta para serializar no cache. Usa stream-mirror
        // que escreve no body original imediatamente E espelha em buffer ate
        // MaxBufferBytes; acima disso, descarta o mirror e segue so para o body.
        var originalBody = context.Response.Body;
        await using var mirror = new MirroringStream(originalBody, MaxBufferBytes);
        context.Response.Body = mirror;

        try
        {
            await next(context);
        }
        catch
        {
            // issue 917: com INSERT-first, uma falha sem finalizacao travaria a chave por
            // 24h (Status permanece Pendente). Marca Falhou pra permitir reenvio.
            context.Response.Body = originalBody;
            await MarcarFalhouSemQuebrarAsync(repo, idParaFinalizar, context.RequestAborted);
            throw;
        }
        finally
        {
            context.Response.Body = originalBody;
        }

        if (context.Response.StatusCode is >= 200 and < 300 && mirror.MirrorComplete)
        {
            string? respostaJson = null;
            var bytes = mirror.GetMirroredBytes();
            if (bytes is not null && bytes.Length > 0 && bytes.Length <= CacheMaxBytes)
            {
                respostaJson = Encoding.UTF8.GetString(bytes);
            }

            try
            {
                await repo.MarcarConcluidoAsync(idParaFinalizar, context.Response.StatusCode, respostaJson, context.RequestAborted);
            }
            catch (Exception ex)
            {
                // Falha ao persistir cache nao deve invalidar a operacao ja completada —
                // mas a chave fica Pendente ate expirar/o cleanup rodar. Logar como warning.
                logger.LogWarning(ex, "Falha ao marcar idempotency key {Id} como concluida. Retry pode reaplicar efeitos ate expirar.", idParaFinalizar);
            }
        }
        else
        {
            await MarcarFalhouSemQuebrarAsync(repo, idParaFinalizar, context.RequestAborted);
        }
    }

    private async Task MarcarFalhouSemQuebrarAsync(IIdempotencyKeyRepository repo, Guid id, CancellationToken ct)
    {
        try
        {
            await repo.MarcarFalhouAsync(id, ct);
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex, "Falha ao marcar idempotency key {Id} como Falhou. Chave pode ficar travada ate expirar.", id);
        }
    }

    private static async Task<string?> ComputePayloadHashAsync(HttpRequest request, CancellationToken ct)
    {
        if (request.ContentLength is > MaxPayloadHashBytes) return null;

        request.EnableBuffering();
        request.Body.Position = 0;
        using var sha = SHA256.Create();
        var hashBytes = await sha.ComputeHashAsync(request.Body, ct);
        request.Body.Position = 0; // rebobina pro handler real ler o body normalmente.
        return Convert.ToHexStringLower(hashBytes);
    }

    private static async Task ReplayAsync(HttpContext context, IdempotencyKey existing)
    {
        context.Response.StatusCode = existing.HttpStatus;
        context.Response.Headers["X-Idempotent-Replay"] = "true";
        if (!string.IsNullOrEmpty(existing.RespostaJson))
        {
            context.Response.ContentType = "application/json";
            await context.Response.WriteAsync(existing.RespostaJson);
        }
    }

    private static async Task WriteInFlightAsync(HttpContext context)
    {
        context.Response.StatusCode = StatusCodes.Status409Conflict;
        context.Response.Headers["Retry-After"] = "1";
        context.Response.ContentType = "application/json";
        await context.Response.WriteAsync(
            """{"error":{"code":"IDEMPOTENCY_IN_FLIGHT","message":"Outra operacao com esta chave ja esta em andamento. Tente novamente em instantes."}}""");
    }

    private static async Task WriteMismatchAsync(HttpContext context, string key)
    {
        context.Response.StatusCode = StatusCodes.Status422UnprocessableEntity;
        context.Response.ContentType = "application/json";
        await context.Response.WriteAsync(
            """{"error":{"code":"IDEMPOTENCY_PAYLOAD_MISMATCH","message":"A chave de idempotencia ja foi usada com um corpo diferente. Gere uma nova chave para esta operacao."}}""");
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

    // Stream que escreve direto no body original e espelha em memoria ate o
    // limite. Acima do limite, descarta o mirror (cache fica disabled pra
    // essa request) mas segue escrevendo no body — request nao quebra.
    private sealed class MirroringStream : Stream
    {
        private readonly Stream _primary;
        private readonly int _maxMirror;
        private MemoryStream? _mirror = new();

        public MirroringStream(Stream primary, int maxMirror)
        {
            _primary = primary;
            _maxMirror = maxMirror;
        }

        public bool MirrorComplete => _mirror is not null;
        public byte[]? GetMirroredBytes() => _mirror?.ToArray();

        public override bool CanRead => false;
        public override bool CanSeek => false;
        public override bool CanWrite => true;
        public override long Length => _primary.Length;
        public override long Position
        {
            get => _primary.Position;
            set => throw new NotSupportedException();
        }

        public override void Flush() => _primary.Flush();
        public override Task FlushAsync(CancellationToken cancellationToken) => _primary.FlushAsync(cancellationToken);

        public override int Read(byte[] buffer, int offset, int count) => throw new NotSupportedException();
        public override long Seek(long offset, SeekOrigin origin) => throw new NotSupportedException();
        public override void SetLength(long value) => throw new NotSupportedException();

        public override void Write(byte[] buffer, int offset, int count)
        {
            _primary.Write(buffer, offset, count);
            MirrorWrite(buffer.AsSpan(offset, count));
        }

        public override async Task WriteAsync(byte[] buffer, int offset, int count, CancellationToken cancellationToken)
        {
            await _primary.WriteAsync(buffer.AsMemory(offset, count), cancellationToken);
            MirrorWrite(buffer.AsSpan(offset, count));
        }

        public override async ValueTask WriteAsync(ReadOnlyMemory<byte> buffer, CancellationToken cancellationToken = default)
        {
            await _primary.WriteAsync(buffer, cancellationToken);
            MirrorWrite(buffer.Span);
        }

        private void MirrorWrite(ReadOnlySpan<byte> span)
        {
            if (_mirror is null) return;
            if (_mirror.Length + span.Length > _maxMirror)
            {
                _mirror.Dispose();
                _mirror = null;
                return;
            }
            _mirror.Write(span);
        }

        protected override void Dispose(bool disposing)
        {
            if (disposing)
            {
                _mirror?.Dispose();
                _mirror = null;
            }
            base.Dispose(disposing);
        }
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
