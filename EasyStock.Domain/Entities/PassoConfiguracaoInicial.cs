namespace EasyStock.Domain.Entities;

/// <summary>Como a loja deixou um passo da configuração inicial (#2070).</summary>
public enum SituacaoDoPasso
{
    /// <summary>Quem configura conferiu o passo e seguiu.</summary>
    Conferido = 1,

    /// <summary>Ficou para depois; concluir aceita.</summary>
    Depois = 2,
}

/// <summary>Texto da situação no contrato e na coluna <c>Situacao</c>: <c>conferido</c> ou <c>depois</c>.</summary>
public static class SituacoesDoPasso
{
    public const string TextoConferido = "conferido";
    public const string TextoDepois = "depois";

    public static string Texto(SituacaoDoPasso situacao) => situacao switch
    {
        SituacaoDoPasso.Conferido => TextoConferido,
        SituacaoDoPasso.Depois => TextoDepois,
        _ => throw new ArgumentOutOfRangeException(nameof(situacao), situacao, "Situação do passo inválida."),
    };

    /// <summary>Lê o texto do contrato; nulo para qualquer outro valor.</summary>
    public static SituacaoDoPasso? Ler(string? texto) => texto switch
    {
        TextoConferido => SituacaoDoPasso.Conferido,
        TextoDepois => SituacaoDoPasso.Depois,
        _ => null,
    };
}

/// <summary>
/// Marca de um passo da configuração inicial da loja (#2070): só diz que o passo foi conferido ou deixado para depois,
/// quando e por quem. Nunca guarda configuração; cada passo grava pelas rotas que já existem. Uma linha por empresa e
/// passo, gravada por upsert no banco.
/// </summary>
public class PassoConfiguracaoInicial
{
    public Guid EmpresaId { get; private set; }

    /// <summary>Id do passo no catálogo (<see cref="EasyStock.Domain.Services.ConfiguracaoInicial.Passos"/>).</summary>
    public string Passo { get; private set; } = null!;

    public SituacaoDoPasso Situacao { get; private set; }
    public DateTime Em { get; private set; }
    public Guid PorUsuarioId { get; private set; }

    private PassoConfiguracaoInicial() { }

    public static PassoConfiguracaoInicial Marcar(
        Guid empresaId, string passo, SituacaoDoPasso situacao, Guid porUsuarioId, DateTime agoraUtc)
    {
        if (empresaId == Guid.Empty)
            throw new RegraDeDominioVioladaException("EmpresaId é obrigatório.");
        if (EasyStock.Domain.Services.ConfiguracaoInicial.Encontrar(passo) is null)
            throw new RegraDeDominioVioladaException($"Passo desconhecido: '{passo}'.");
        if (!Enum.IsDefined(situacao))
            throw new RegraDeDominioVioladaException("Situação do passo inválida.");
        if (porUsuarioId == Guid.Empty)
            throw new RegraDeDominioVioladaException("Informe quem marcou o passo.");

        return new PassoConfiguracaoInicial
        {
            EmpresaId = empresaId,
            Passo = passo,
            Situacao = situacao,
            Em = agoraUtc,
            PorUsuarioId = porUsuarioId,
        };
    }
}
