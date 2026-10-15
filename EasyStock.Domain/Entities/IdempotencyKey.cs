namespace EasyStock.Domain.Entities;

/// <summary>
/// Registra a chave de idempotencia de um POST critico (entrada/saida/estorno
/// de estoque, venda, pagamento de pedido etc) junto com a resposta serializada.
/// Permite que o cliente reenviar a mesma requisicao receba a resposta original
/// sem reaplicar efeitos colaterais (R5: duplicidade por retry de mobile/web).
///
/// <para>
/// issue 917 Fase B: modelo INSERT-first. A linha é criada com <see cref="Status"/>
/// = <see cref="StatusIdempotencyKey.Pendente"/> ANTES do handler rodar (não depois,
/// como na Fase A) — o índice único <c>ux_idempotency_key_empresa_recurso</c> é o
/// ponto de serialização real: dois requests concorrentes com a mesma chave, um
/// vence o INSERT, o outro recebe <c>unique_violation</c> e decide replay/409/takeover
/// a partir da linha existente. Isso fecha o TOCTOU do middleware original, que fazia
/// <c>GetActiveAsync</c> ANTES de qualquer <c>SaveAsync</c>.
/// </para>
/// </summary>
public class IdempotencyKey
{
    public Guid Id { get; set; }

    /// <summary>UUID enviado pelo cliente no header Idempotency-Key.</summary>
    public string Key { get; set; } = null!;

    /// <summary>Empresa do request — chave e' unica por empresa.</summary>
    public Guid EmpresaId { get; set; }

    /// <summary>Metodo HTTP + path normalizado (ex.: "POST /api/estoque/saida").</summary>
    public string MetodoRecurso { get; set; } = null!;

    /// <summary>Estado atual — ver <see cref="StatusIdempotencyKey"/>.</summary>
    public StatusIdempotencyKey Status { get; set; } = StatusIdempotencyKey.Pendente;

    /// <summary>
    /// Hash (SHA-256) do corpo do request que reservou esta chave. Se um segundo
    /// request chegar com a MESMA chave mas corpo DIFERENTE, é um mismatch — a chave
    /// foi reusada indevidamente (bug de cliente) e não deve receber replay silencioso
    /// da resposta de uma operação diferente.
    /// </summary>
    public string? PayloadHash { get; set; }

    /// <summary>
    /// Timestamp do último "lock" desta linha em <see cref="StatusIdempotencyKey.Pendente"/>.
    /// Usado como lease: se um processo morre antes de finalizar (sem marcar Concluido/Falhou),
    /// outra request com a mesma chave pode fazer takeover via CAS depois que o lease expira
    /// (ver <see cref="LeaseExpirado"/>), evitando que a chave fique travada para sempre.
    /// </summary>
    public DateTime LockedAtUtc { get; set; }

    /// <summary>Status HTTP da resposta original (só significativo quando Status=Concluido/Falhou).</summary>
    public int HttpStatus { get; set; }

    /// <summary>Body JSON da resposta original (truncado em 64KB).</summary>
    public string? RespostaJson { get; set; }

    public DateTime CriadoEm { get; set; }
    public DateTime ExpiraEm { get; set; }

    /// <summary>Duração do lease de "processando" antes de permitir takeover por outra request.</summary>
    public static readonly TimeSpan LeaseDuration = TimeSpan.FromSeconds(60);

    public static IdempotencyKey CriarPendente(string key, Guid empresaId, string metodoRecurso, string? payloadHash, TimeSpan ttl)
    {
        var agora = DateTime.UtcNow;
        return new IdempotencyKey
        {
            Id = Guid.NewGuid(),
            Key = key,
            EmpresaId = empresaId,
            MetodoRecurso = metodoRecurso,
            Status = StatusIdempotencyKey.Pendente,
            PayloadHash = payloadHash,
            LockedAtUtc = agora,
            HttpStatus = 0,
            RespostaJson = null,
            CriadoEm = agora,
            ExpiraEm = agora.Add(ttl)
        };
    }

    public bool Expirou(DateTime referencia) => referencia >= ExpiraEm;

    /// <summary>True quando o lease de "processando" já passou — outra request pode
    /// fazer takeover via CAS em vez de esperar indefinidamente (processo morto).</summary>
    public bool LeaseExpirado(DateTime referencia) => referencia >= LockedAtUtc.Add(LeaseDuration);

    public void MarcarConcluido(int httpStatus, string? respostaJson, DateTime referencia)
    {
        Status = StatusIdempotencyKey.Concluido;
        HttpStatus = httpStatus;
        RespostaJson = respostaJson;
        LockedAtUtc = referencia;
    }

    public void MarcarFalhou(DateTime referencia)
    {
        Status = StatusIdempotencyKey.Falhou;
        LockedAtUtc = referencia;
    }
}
