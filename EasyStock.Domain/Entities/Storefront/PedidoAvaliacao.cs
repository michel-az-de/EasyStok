namespace EasyStock.Domain.Entities.Storefront;

/// <summary>
/// Avaliação do cliente sobre um pedido entregue.
///
/// <para>
/// <strong>Fluxo:</strong> a Babá solicita a avaliação +24h após a entrega
/// (<see cref="SolicitadoEm"/>). O cliente preenche estrelas + comentário,
/// e a entity é criada nesse momento — <see cref="RespondidoEm"/> reflete
/// quando o cliente respondeu. A Babá pode opcionalmente responder o
/// comentário público (<see cref="Responder"/>) ou ocultar a avaliação
/// por moderação (<see cref="Ocultar"/>).
/// </para>
///
/// <para>
/// <strong>Invariante de DB:</strong> 1 avaliação por pedido (UNIQUE PedidoId).
/// Garantido pelo EF Config — a entity não duplica essa proteção (Application
/// layer já cobre via repo + transação).
/// </para>
/// </summary>
public class PedidoAvaliacao
{
    private const int LimiteComentario = 500;
    private const int LimiteResposta = 500;

    public Guid Id { get; private set; }
    public Guid PedidoId { get; private set; }
    public Guid ClienteId { get; private set; }
    public Guid EmpresaId { get; private set; }

    /// <summary>Nota de 1 a 5 estrelas — invariante validado no factory.</summary>
    public int Estrelas { get; private set; }

    /// <summary>Comentário público do cliente. Null = não comentou.</summary>
    public string? Comentario { get; private set; }

    public bool RecomendariaParaAmigos { get; private set; }

    /// <summary>URL da foto do prato enviada pelo cliente (opcional).</summary>
    public string? FotoUrl { get; private set; }

    /// <summary>Quando a Babá solicitou a avaliação (gerado por evento +24h após entrega).</summary>
    public DateTime SolicitadoEm { get; private set; }

    /// <summary>Quando o cliente respondeu. Setado no factory (UTC).</summary>
    public DateTime? RespondidoEm { get; private set; }

    /// <summary>Quando a Babá ocultou a avaliação por moderação. Null = visível.</summary>
    public DateTime? OcultadoEm { get; private set; }

    /// <summary>Resposta pública da Babá ao comentário do cliente. Null = sem resposta.</summary>
    public string? RespostaDaBaba { get; private set; }

    /// <summary>Quando a Babá respondeu. Atualizado a cada chamada de <see cref="Responder"/>.</summary>
    public DateTime? RespondidaEmPorBaba { get; private set; }

    // EF Core ctor sem parâmetros
    private PedidoAvaliacao() { }

    public static PedidoAvaliacao Criar(
        Guid pedidoId,
        Guid clienteId,
        Guid empresaId,
        int estrelas,
        string? comentario,
        bool recomendariaParaAmigos,
        string? fotoUrl,
        DateTime solicitadoEm)
    {
        if (pedidoId == Guid.Empty)
            throw new RegraDeDominioVioladaException("PedidoId é obrigatório.");
        if (clienteId == Guid.Empty)
            throw new RegraDeDominioVioladaException("ClienteId é obrigatório.");
        if (empresaId == Guid.Empty)
            throw new RegraDeDominioVioladaException("EmpresaId é obrigatório.");

        ValidarEstrelas(estrelas);
        var comentarioNormalizado = NormalizarComentario(comentario);

        return new PedidoAvaliacao
        {
            Id = Guid.NewGuid(),
            PedidoId = pedidoId,
            ClienteId = clienteId,
            EmpresaId = empresaId,
            Estrelas = estrelas,
            Comentario = comentarioNormalizado,
            RecomendariaParaAmigos = recomendariaParaAmigos,
            FotoUrl = fotoUrl?.Trim(),
            SolicitadoEm = DateTime.SpecifyKind(solicitadoEm, DateTimeKind.Utc),
            RespondidoEm = DateTime.UtcNow,
            OcultadoEm = null,
            RespostaDaBaba = null,
            RespondidaEmPorBaba = null,
        };
    }

    /// <summary>
    /// Resposta pública da Babá ao comentário. Permite edição — cada chamada
    /// substitui o texto e atualiza <see cref="RespondidaEmPorBaba"/>.
    /// </summary>
    public void Responder(string textoResposta)
    {
        if (string.IsNullOrWhiteSpace(textoResposta))
            throw new RegraDeDominioVioladaException("Resposta da Babá é obrigatória.");

        var textoTrim = textoResposta.Trim();
        if (textoTrim.Length > LimiteResposta)
            throw new RegraDeDominioVioladaException(
                $"Resposta da Babá não pode exceder {LimiteResposta} caracteres (recebido: {textoTrim.Length}).");

        RespostaDaBaba = textoTrim;
        RespondidaEmPorBaba = DateTime.UtcNow;
    }

    /// <summary>
    /// Oculta a avaliação por moderação (cliente abusivo, conteúdo impróprio).
    /// Idempotente: chamadas subsequentes preservam o timestamp original.
    /// </summary>
    public void Ocultar()
    {
        if (OcultadoEm.HasValue) return;
        OcultadoEm = DateTime.UtcNow;
    }

    private static void ValidarEstrelas(int estrelas)
    {
        if (estrelas is < 1 or > 5)
            throw new RegraDeDominioVioladaException(
                $"Estrelas deve estar entre 1 e 5 (recebido: {estrelas}).");
    }

    private static string? NormalizarComentario(string? comentario)
    {
        if (string.IsNullOrWhiteSpace(comentario)) return null;

        var trim = comentario.Trim();
        if (trim.Length > LimiteComentario)
            throw new RegraDeDominioVioladaException(
                $"Comentário não pode exceder {LimiteComentario} caracteres (recebido: {trim.Length}).");

        return trim;
    }
}
