using EasyStock.Domain.Enums.Atendimento;

namespace EasyStock.Domain.Entities.Atendimento;

/// <summary>
/// Lembrete interno da dona (S43, ADR-0051): nada sai para o cliente. O manual é escrito no console;
/// o automático nasce do avaliador e carrega a <see cref="Referencia"/> do fato que o gerou (o pedido
/// sem baixa, a mensagem do cliente sem resposta). O par <c>(Tipo, Referencia)</c> é único por
/// empresa: é o que torna o avaliador idempotente.
///
/// <para>O instante vem sempre por parâmetro (UTC): o domínio não lê relógio ambiente.</para>
/// </summary>
public class Lembrete
{
    public const int TextoTamanhoMaximo = 500;
    public const int ReferenciaTamanhoMaximo = 64;

    public Guid Id { get; private set; }
    public Guid EmpresaId { get; private set; }
    public Guid? ConversaId { get; private set; }
    public Guid? PedidoId { get; private set; }
    public TipoLembrete Tipo { get; private set; }

    /// <summary>Fato que gerou o automático (id do pedido ou da mensagem). Nulo no manual.</summary>
    public string? Referencia { get; private set; }

    public string Texto { get; private set; } = null!;
    public DateTime VenceEm { get; private set; }
    public SituacaoLembrete Situacao { get; private set; }

    /// <summary>Para quem é. Nulo = para toda a equipe do atendimento.</summary>
    public Guid? ParaUsuarioId { get; private set; }

    public Guid? CriadoPorUsuarioId { get; private set; }
    public DateTime CriadoEm { get; private set; }

    /// <summary>Quando o aviso (Web Push e SSE) saiu. Nulo = ainda não avisado.</summary>
    public DateTime? AvisadoEm { get; private set; }

    /// <summary>Quando alguém abriu o sininho com o lembrete lá.</summary>
    public DateTime? VistoEm { get; private set; }

    public DateTime? ConcluidoEm { get; private set; }

    /// <summary>Quem concluiu. Nulo em lembrete concluído: resolveu sozinho (o automático).</summary>
    public Guid? ConcluidoPorUsuarioId { get; private set; }

    public bool EstaAberto => Situacao == SituacaoLembrete.Aberto;

    // EF Core ctor sem parâmetros
    private Lembrete() { }

    /// <summary>Lembrete escrito pela dona. Sem <paramref name="venceEm"/>, vence agora.</summary>
    public static Lembrete Manual(
        Guid empresaId, string texto, DateTime? venceEm, Guid criadoPorUsuarioId, DateTime agora,
        Guid? paraUsuarioId = null, Guid? conversaId = null, Guid? pedidoId = null)
    {
        if (criadoPorUsuarioId == Guid.Empty) throw new RegraDeDominioVioladaException("Quem cria o lembrete é obrigatório.");
        return Novo(empresaId, TipoLembrete.Manual, null, texto, venceEm ?? agora, agora,
            paraUsuarioId, conversaId, pedidoId, criadoPorUsuarioId);
    }

    /// <summary>Lembrete do avaliador: vence na hora e carrega a referência do fato que o gerou.</summary>
    public static Lembrete Automatico(
        Guid empresaId, TipoLembrete tipo, string referencia, string texto, DateTime agora,
        Guid? paraUsuarioId = null, Guid? conversaId = null, Guid? pedidoId = null)
    {
        if (tipo == TipoLembrete.Manual || !Enum.IsDefined(tipo))
            throw new RegraDeDominioVioladaException($"Tipo de lembrete automático inválido: {tipo}.");
        if (string.IsNullOrWhiteSpace(referencia) || referencia.Trim().Length > ReferenciaTamanhoMaximo)
            throw new RegraDeDominioVioladaException("Lembrete automático precisa da referência do fato que o gerou.");
        return Novo(empresaId, tipo, referencia.Trim(), texto, agora, agora, paraUsuarioId, conversaId, pedidoId, null);
    }

    private static Lembrete Novo(
        Guid empresaId, TipoLembrete tipo, string? referencia, string texto, DateTime venceEm, DateTime agora,
        Guid? paraUsuarioId, Guid? conversaId, Guid? pedidoId, Guid? criadoPorUsuarioId)
    {
        if (empresaId == Guid.Empty) throw new RegraDeDominioVioladaException("EmpresaId é obrigatório.");
        if (string.IsNullOrWhiteSpace(texto)) throw new RegraDeDominioVioladaException("Texto do lembrete é obrigatório.");
        var limpo = texto.Trim();
        if (limpo.Length > TextoTamanhoMaximo)
            throw new RegraDeDominioVioladaException($"Texto do lembrete acima de {TextoTamanhoMaximo} caracteres.");

        return new Lembrete
        {
            Id = Guid.NewGuid(),
            EmpresaId = empresaId,
            Tipo = tipo,
            Referencia = referencia,
            Texto = limpo,
            VenceEm = Utc(venceEm),
            Situacao = SituacaoLembrete.Aberto,
            ParaUsuarioId = SemVazio(paraUsuarioId),
            ConversaId = SemVazio(conversaId),
            PedidoId = SemVazio(pedidoId),
            CriadoPorUsuarioId = criadoPorUsuarioId,
            CriadoEm = Utc(agora),
        };
    }

    /// <summary>Venceu e ainda não foi avisado: é a vez do aviso.</summary>
    public bool DeveAvisar(DateTime agora) => EstaAberto && AvisadoEm is null && VenceEm <= Utc(agora);

    public void MarcarAvisado(DateTime em) => AvisadoEm ??= Utc(em);

    /// <summary>Idempotente: preserva o primeiro carimbo.</summary>
    public void MarcarVisto(DateTime em) => VistoEm ??= Utc(em);

    /// <summary>
    /// Idempotente. Sem <paramref name="usuarioId"/> é a resolução sozinha do automático (a condição
    /// deixou de valer: o pagamento baixou, a dona respondeu).
    /// </summary>
    public void Concluir(DateTime em, Guid? usuarioId = null)
    {
        if (!EstaAberto) return;
        Situacao = SituacaoLembrete.Concluido;
        ConcluidoEm = Utc(em);
        ConcluidoPorUsuarioId = SemVazio(usuarioId);
    }

    private static Guid? SemVazio(Guid? id) => id == Guid.Empty ? null : id;

    private static DateTime Utc(DateTime d) => d.Kind switch
    {
        DateTimeKind.Utc => d,
        DateTimeKind.Local => d.ToUniversalTime(),
        _ => DateTime.SpecifyKind(d, DateTimeKind.Utc),
    };
}
