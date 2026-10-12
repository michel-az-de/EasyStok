namespace EasyStock.Domain.Entities.Campanhas;

/// <summary>Quem registrou o interesse (S31): o agente de atendimento ou a dona pelo console.</summary>
public static class OrigemInteresse
{
    public const string Agente = "agente";
    public const string Dona = "dona";

    public static bool Valida(string? origem) => origem is Agente or Dona;
}

/// <summary>
/// Interesse do cliente num item indisponível (S31, US-057, UC-01 E1). Quando o item volta ao
/// cardápio, o console é avisado e a dona decide se avisa o cliente: nada é enviado sozinho (D8).
/// <see cref="CardapioItemId"/> vem quando o item foi identificado; senão vale a <see cref="Descricao"/>.
/// Aberto enquanto <see cref="AtendidoEm"/> é nulo.
/// </summary>
public class InteresseItem
{
    public const int DescricaoTamanhoMaximo = 200;
    public const int OrigemTamanhoMaximo = 20;

    public Guid Id { get; private set; }
    public Guid EmpresaId { get; private set; }
    public Guid ClienteId { get; private set; }
    public Guid? CardapioItemId { get; private set; }
    public string? Descricao { get; private set; }
    public string Origem { get; private set; } = null!;
    public DateTime RegistradoEm { get; private set; }
    public DateTime? AtendidoEm { get; private set; }

    public bool Aberto => AtendidoEm is null;

    // EF Core ctor sem parâmetros
    private InteresseItem() { }

    public static InteresseItem Registrar(
        Guid empresaId, Guid clienteId, Guid? cardapioItemId, string? descricao, string origem, DateTime em)
    {
        if (empresaId == Guid.Empty)
            throw new RegraDeDominioVioladaException("EmpresaId é obrigatório.");
        if (clienteId == Guid.Empty)
            throw new RegraDeDominioVioladaException("ClienteId é obrigatório.");
        if (cardapioItemId == Guid.Empty)
            throw new RegraDeDominioVioladaException("CardapioItemId inválido.");
        if (!OrigemInteresse.Valida(origem))
            throw new RegraDeDominioVioladaException($"Origem do interesse inválida: '{origem}'.");

        var texto = string.IsNullOrWhiteSpace(descricao) ? null : descricao.Trim();
        if (cardapioItemId is null && texto is null)
            throw new RegraDeDominioVioladaException("Informe o item do cardápio ou descreva o que o cliente quer.");
        if (texto is { Length: > DescricaoTamanhoMaximo })
            texto = texto[..DescricaoTamanhoMaximo];

        return new InteresseItem
        {
            Id = Guid.NewGuid(),
            EmpresaId = empresaId,
            ClienteId = clienteId,
            CardapioItemId = cardapioItemId,
            Descricao = texto,
            Origem = origem,
            RegistradoEm = Utc(em),
        };
    }

    /// <summary>Fecha o interesse (cliente avisado ou atendido). Idempotente: vale o primeiro instante.</summary>
    public void MarcarAtendido(DateTime em)
    {
        if (AtendidoEm is not null) return;
        AtendidoEm = Utc(em);
    }

    private static DateTime Utc(DateTime em) => em.Kind == DateTimeKind.Utc ? em : DateTime.SpecifyKind(em, DateTimeKind.Utc);
}
