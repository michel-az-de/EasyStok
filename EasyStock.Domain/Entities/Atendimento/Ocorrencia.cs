using EasyStock.Domain.Enums.Atendimento;

namespace EasyStock.Domain.Entities.Atendimento;

/// <summary>
/// Reclamação ligada a um pedido (S27, US-049, US-050, RN-35, RN-36). Entidade própria e enxuta: não
/// reaproveita <c>AdminTicket</c> (suporte SaaS com SLA). Abre sozinha (avaliação negativa, agente) ou pela
/// dona; só a dona resolve, com ou sem reembolso. Comida não volta: o reembolso devolve o dinheiro e o
/// motivo fica gravado aqui e numa <c>ClienteNota</c>. O instante vem sempre por parâmetro.
/// </summary>
public class Ocorrencia
{
    public const int RelatoTamanhoMaximo = 1000;
    public const int ResolucaoTamanhoMaximo = 1000;
    public const int ReembolsoIdTamanhoMaximo = 120;

    public Guid Id { get; private set; }
    public Guid EmpresaId { get; private set; }
    public Guid PedidoId { get; private set; }
    public Guid ClienteId { get; private set; }
    public Guid? ConversaId { get; private set; }
    public OrigemOcorrencia Origem { get; private set; }
    public CategoriaOcorrencia Categoria { get; private set; }
    public string Relato { get; private set; } = null!;
    public StatusOcorrencia Status { get; private set; }
    public string? Resolucao { get; private set; }

    /// <summary>Valor devolvido; no reembolso manual (pago fora do gateway) guarda o valor para conferência.</summary>
    public decimal? ReembolsoValor { get; private set; }

    /// <summary>Id do estorno no gateway.</summary>
    public string? ReembolsoIdSolicitacao { get; private set; }

    /// <summary>Quando o gateway confirmou o estorno; null no reembolso manual.</summary>
    public DateTime? ReembolsoEm { get; private set; }

    public DateTime CriadaEm { get; private set; }
    public DateTime? ApuradaEm { get; private set; }
    public Guid? ApuradaPorUsuarioId { get; private set; }
    public string? ApuradaPorNome { get; private set; }
    public DateTime? ReembolsoSolicitadoEm { get; private set; }
    public DateTime? ResolvidaEm { get; private set; }
    public Guid? ResolvidaPorUsuarioId { get; private set; }
    public string? ResolvidaPorNome { get; private set; }

    public bool EstaAberta => Status == StatusOcorrencia.Aberta;

    // EF Core
    private Ocorrencia() { }

    public static Ocorrencia Abrir(
        Guid empresaId, Guid pedidoId, Guid clienteId, Guid? conversaId,
        OrigemOcorrencia origem, CategoriaOcorrencia categoria, string relato, DateTime agora)
    {
        if (empresaId == Guid.Empty) throw new RegraDeDominioVioladaException("EmpresaId é obrigatório.");
        if (pedidoId == Guid.Empty) throw new RegraDeDominioVioladaException("PedidoId é obrigatório.");
        if (clienteId == Guid.Empty) throw new RegraDeDominioVioladaException("ClienteId é obrigatório.");
        if (conversaId == Guid.Empty) throw new RegraDeDominioVioladaException("ConversaId não pode ser Guid.Empty; use null.");
        if (!Enum.IsDefined(origem)) throw new RegraDeDominioVioladaException("Origem da ocorrência inválida.");
        if (!Enum.IsDefined(categoria)) throw new RegraDeDominioVioladaException("Categoria da ocorrência inválida.");

        return new Ocorrencia
        {
            Id = Guid.NewGuid(),
            EmpresaId = empresaId,
            PedidoId = pedidoId,
            ClienteId = clienteId,
            ConversaId = conversaId,
            Origem = origem,
            Categoria = categoria,
            Relato = Texto(relato, RelatoTamanhoMaximo, "Relato"),
            Status = StatusOcorrencia.Aberta,
            CriadaEm = agora,
        };
    }

    public bool Apurar(Guid usuarioId, string? nome, DateTime agora)
    {
        if (usuarioId == Guid.Empty) throw new RegraDeDominioVioladaException("Quem apura é obrigatório.");
        if (!EstaAberta || ApuradaEm is not null) return false;
        ApuradaEm = agora;
        ApuradaPorUsuarioId = usuarioId;
        ApuradaPorNome = nome;
        return true;
    }

    public void IniciarReembolso(string resolucao, decimal valor, DateTime agora)
    {
        if (!EstaAberta) throw new RegraDeDominioVioladaException("Ocorrência já resolvida.");
        GarantirSemReembolso(valor);
        Resolucao = Texto(resolucao, ResolucaoTamanhoMaximo, "Resolução");
        ReembolsoValor = valor;
        ReembolsoSolicitadoEm ??= agora;
    }

    public void LimparReembolsoRecusado()
    {
        if (ReembolsoEm is not null) throw new RegraDeDominioVioladaException("Reembolso já confirmado.");
        ReembolsoSolicitadoEm = null;
        ReembolsoValor = null;
    }

    public void Resolver(string resolucao, Guid usuarioId, DateTime agora, string? nome = null)
    {
        if (!EstaAberta) throw new RegraDeDominioVioladaException("Ocorrência já resolvida.");
        if (usuarioId == Guid.Empty) throw new RegraDeDominioVioladaException("Quem resolve é obrigatório.");

        Resolucao = Texto(resolucao, ResolucaoTamanhoMaximo, "Resolução");
        Status = StatusOcorrencia.Resolvida;
        ResolvidaEm = agora;
        ResolvidaPorUsuarioId = usuarioId;
        ResolvidaPorNome = nome;
    }

    /// <summary>Estorno confirmado pelo gateway.</summary>
    public void RegistrarReembolso(decimal valor, string idSolicitacao, DateTime em)
    {
        GarantirSemReembolso(valor);
        if (string.IsNullOrWhiteSpace(idSolicitacao))
            throw new RegraDeDominioVioladaException("Id do estorno é obrigatório.");
        var id = idSolicitacao.Trim();
        ReembolsoValor = valor;
        ReembolsoIdSolicitacao = id.Length > ReembolsoIdTamanhoMaximo ? id[..ReembolsoIdTamanhoMaximo] : id;
        ReembolsoEm = em;
    }

    /// <summary>Pedido pago fora do gateway (dinheiro, Pix manual): guarda o valor para conferência.</summary>
    public void RegistrarReembolsoManual(decimal valor)
    {
        GarantirSemReembolso(valor);
        ReembolsoValor = valor;
    }

    private void GarantirSemReembolso(decimal valor)
    {
        if (ReembolsoEm is not null) throw new RegraDeDominioVioladaException("Ocorrência já reembolsada.");
        if (valor <= 0m) throw new RegraDeDominioVioladaException("Valor do reembolso deve ser maior que zero.");
    }

    private static string Texto(string? texto, int maximo, string campo)
    {
        var limpo = texto?.Trim() ?? string.Empty;
        if (limpo.Length == 0) throw new RegraDeDominioVioladaException($"{campo} é obrigatório.");
        return limpo.Length > maximo ? limpo[..maximo] : limpo;
    }
}
