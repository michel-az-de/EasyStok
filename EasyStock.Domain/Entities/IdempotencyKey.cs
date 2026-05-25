namespace EasyStock.Domain.Entities;

/// <summary>
/// Registra a chave de idempotencia de um POST critico (entrada/saida/estorno
/// de estoque, venda etc) junto com a resposta serializada. Permite que o
/// cliente reenviar a mesma requisicao receba a resposta original sem
/// reaplicar efeitos colaterais (R5: duplicidade por retry de mobile/web).
/// </summary>
public class IdempotencyKey
{
    public Guid Id { get; set; }

    /// <summary>UUID enviado pelo cliente no header Idempotency-Key.</summary>
    public string Key { get; set; } = null!;

    /// <summary>Empresa do request — chave e' unica por empresa.</summary>
    public Guid EmpresaId { get; set; }

    /// <summary>Metodo HTTP + path normalizado (ex.: "POST /api/itensestoque").</summary>
    public string MetodoRecurso { get; set; } = null!;

    /// <summary>Status HTTP da resposta original.</summary>
    public int HttpStatus { get; set; }

    /// <summary>Body JSON da resposta original (truncado em 64KB).</summary>
    public string? RespostaJson { get; set; }

    public DateTime CriadoEm { get; set; }
    public DateTime ExpiraEm { get; set; }

    public static IdempotencyKey Criar(string key, Guid empresaId, string metodoRecurso, int httpStatus, string? respostaJson, TimeSpan ttl)
    {
        var agora = DateTime.UtcNow;
        return new IdempotencyKey
        {
            Id = Guid.NewGuid(),
            Key = key,
            EmpresaId = empresaId,
            MetodoRecurso = metodoRecurso,
            HttpStatus = httpStatus,
            RespostaJson = respostaJson,
            CriadoEm = agora,
            ExpiraEm = agora.Add(ttl)
        };
    }

    public bool Expirou(DateTime referencia) => referencia >= ExpiraEm;
}
