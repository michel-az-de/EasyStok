namespace EasyStock.Domain.Entities.Storefront;

/// <summary>
/// Registro de idempotência client-side para checkout: previne que duplo-clique
/// em "Pagar" gere Faturas + InitPoints MercadoPago duplicados (R5).
///
/// <para>
/// <strong>Chave composta</strong>: <see cref="Key"/> (UUID enviado pelo client
/// no header <c>X-Idempotency-Key</c>) + <see cref="ContentHash"/> (SHA-256 hex
/// do payload do cart no momento do POST).
/// </para>
///
/// <para>
/// Semântica:
/// <list type="bullet">
///   <item>
///     Mesma <c>Key</c> + mesmo <c>ContentHash</c> ⇒ retorna <see cref="FaturaId"/>
///     e <see cref="InitPoint"/> originais (sem criar nada novo).
///   </item>
///   <item>
///     Mesma <c>Key</c> + <c>ContentHash</c> diferente ⇒ cliente alterou o cart
///     entre cliques: tratamos como checkout novo (cria Fatura nova).
///   </item>
/// </list>
/// </para>
///
/// <para>
/// <strong>TTL 24h</strong> definido na factory; cleanup job removerá expirados.
/// </para>
/// </summary>
public class CheckoutIdempotency
{
    private static readonly TimeSpan Ttl = TimeSpan.FromHours(24);

    public Guid Id { get; private set; }

    /// <summary>UUID enviado pelo cliente no header <c>X-Idempotency-Key</c>.</summary>
    public Guid Key { get; private set; }

    /// <summary>SHA-256 (hex lowercase, 64 chars) do payload do cart na hora do POST.</summary>
    public string ContentHash { get; private set; } = null!;

    /// <summary>Fatura criada para este checkout. Null até <see cref="VincularFatura"/>.</summary>
    public Guid? FaturaId { get; private set; }

    /// <summary>URL do MercadoPago para redirecionar o cliente. Null até <see cref="VincularFatura"/>.</summary>
    public string? InitPoint { get; private set; }

    public DateTime CriadoEm { get; private set; }
    public DateTime ExpiraEm { get; private set; }

    // EF Core ctor sem parâmetros
    private CheckoutIdempotency() { }

    /// <summary>
    /// Factory. TTL 24h definido aqui. <see cref="FaturaId"/>/<see cref="InitPoint"/>
    /// começam null — são preenchidos depois via <see cref="VincularFatura"/>.
    /// </summary>
    public static CheckoutIdempotency Criar(Guid key, string contentHash)
    {
        if (key == Guid.Empty)
            throw new RegraDeDominioVioladaException("Key é obrigatória.");

        var hashNormalizado = NormalizarHash(contentHash);
        ValidarContentHash(hashNormalizado);

        var agora = DateTime.UtcNow;
        return new CheckoutIdempotency
        {
            Id = Guid.NewGuid(),
            Key = key,
            ContentHash = hashNormalizado,
            CriadoEm = agora,
            ExpiraEm = agora.Add(Ttl),
        };
    }

    /// <summary>
    /// Vincula este registro à Fatura criada e InitPoint do MercadoPago.
    /// Idempotente para a mesma fatura (proteção contra retry); rejeita
    /// segunda vinculação para fatura diferente.
    /// </summary>
    public void VincularFatura(Guid faturaId, string initPoint)
    {
        if (faturaId == Guid.Empty)
            throw new RegraDeDominioVioladaException("FaturaId é obrigatória.");
        if (string.IsNullOrWhiteSpace(initPoint))
            throw new RegraDeDominioVioladaException("InitPoint é obrigatório.");

        if (FaturaId.HasValue)
        {
            if (FaturaId.Value != faturaId)
                throw new RegraDeDominioVioladaException(
                    $"FaturaId já vinculada ({FaturaId.Value}); não é possível vincular outra ({faturaId}).");
            return; // idempotente para mesma fatura
        }

        FaturaId = faturaId;
        InitPoint = initPoint.Trim();
    }

    /// <summary>Retorna true se <paramref name="referencia"/> &gt;= <see cref="ExpiraEm"/>.</summary>
    public bool Expirou(DateTime referencia) => referencia >= ExpiraEm;

    /// <summary>
    /// Confere se a tupla (key, hash) bate exatamente com este registro.
    /// Hash é case-insensitive (sempre comparado em lowercase).
    /// </summary>
    public bool Confere(Guid key, string contentHash)
    {
        if (Key != key) return false;
        return string.Equals(ContentHash, NormalizarHash(contentHash), StringComparison.Ordinal);
    }

    // ── Validações privadas ────────────────────────────────────────────

    private static string NormalizarHash(string? hash) =>
        (hash ?? string.Empty).Trim().ToLowerInvariant();

    private static void ValidarContentHash(string hashNormalizado)
    {
        if (string.IsNullOrWhiteSpace(hashNormalizado))
            throw new RegraDeDominioVioladaException("ContentHash é obrigatório.");

        if (hashNormalizado.Length != 64)
            throw new RegraDeDominioVioladaException(
                $"ContentHash deve ser SHA-256 hex (64 chars, recebido: {hashNormalizado.Length}).");

        foreach (var c in hashNormalizado)
        {
            var hex = (c >= '0' && c <= '9') || (c >= 'a' && c <= 'f');
            if (!hex)
                throw new RegraDeDominioVioladaException(
                    $"ContentHash deve conter apenas caracteres hex [0-9a-f] (recebido: '{c}').");
        }
    }
}
