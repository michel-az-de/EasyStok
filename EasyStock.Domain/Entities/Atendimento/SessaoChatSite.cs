using System.Security.Cryptography;

namespace EasyStock.Domain.Entities.Atendimento;

/// <summary>
/// Sessão anônima do chat do site (S36, ADR-0051). Nasce quando o visitante abre o chat numa loja e
/// vale 24 h a partir do último uso. Guarda só o hash SHA-256 (hex) do token entregue ao visitante:
/// quem lê o banco não consegue falar como ele. O contato da conversa no canal
/// <c>ChatSite</c> é o próprio id da sessão.
/// </summary>
public class SessaoChatSite
{
    public static readonly TimeSpan Validade = TimeSpan.FromHours(24);
    public const int TokenHashTamanho = 64;

    public Guid Id { get; private set; }
    public Guid EmpresaId { get; private set; }
    public Guid StorefrontId { get; private set; }
    public string TokenHash { get; private set; } = null!;
    public Guid? ConversaId { get; private set; }
    public DateTime CriadaEm { get; private set; }
    public DateTime UltimoUsoEm { get; private set; }
    public DateTime ExpiraEm { get; private set; }

    // Formulário antes do chat (#1430): o que o visitante informou, com o instante do aceite da política.
    public string? VisitanteNome { get; private set; }
    public string? VisitanteTelefone { get; private set; }
    public string? VisitanteEmail { get; private set; }
    public DateTime? VisitanteInformadoEm { get; private set; }

    /// <summary>Nulo enquanto o visitante não preencheu o formulário.</summary>
    public ContatoInformadoVisitante? ContatoInformado =>
        ContatoInformadoVisitante.Gravado(VisitanteNome, VisitanteTelefone, VisitanteEmail, VisitanteInformadoEm);

    /// <summary>Identificador opaco do contato na conversa do canal <c>ChatSite</c>.</summary>
    public string ContatoIdExterno => Id.ToString("N");

    // EF Core ctor sem parametros
    private SessaoChatSite() { }

    public static SessaoChatSite Abrir(Guid empresaId, Guid storefrontId, string tokenHash, DateTime agora)
    {
        if (empresaId == Guid.Empty)
            throw new RegraDeDominioVioladaException("EmpresaId e obrigatorio.");
        if (storefrontId == Guid.Empty)
            throw new RegraDeDominioVioladaException("StorefrontId e obrigatorio.");
        if (string.IsNullOrEmpty(tokenHash) || tokenHash.Length != TokenHashTamanho)
            throw new RegraDeDominioVioladaException("Hash do token da sessao invalido.");

        var instante = Utc(agora);
        return new SessaoChatSite
        {
            Id = Guid.NewGuid(),
            EmpresaId = empresaId,
            StorefrontId = storefrontId,
            TokenHash = tokenHash,
            CriadaEm = instante,
            UltimoUsoEm = instante,
            ExpiraEm = instante + Validade,
        };
    }

    public bool EstaValida(DateTime agora) => Utc(agora) < ExpiraEm;

    /// <summary>Cada uso renova a validade. Sessao vencida nao volta: o visitante abre outra.</summary>
    public void RegistrarUso(DateTime agora)
    {
        if (!EstaValida(agora))
            throw new RegraDeDominioVioladaException("Sessao do chat vencida.");
        var instante = Utc(agora);
        UltimoUsoEm = instante;
        ExpiraEm = instante + Validade;
    }

    public void Encerrar(DateTime agora)
    {
        // Renovacoes ja em voo podem gravar datas antigas. Trocar o hash torna o bearer
        // irrecuperavel mesmo nesses casos; a renovacao nunca altera esta propriedade.
        TokenHash = Convert.ToHexStringLower(RandomNumberGenerator.GetBytes(TokenHashTamanho / 2));
        if (Utc(agora) < ExpiraEm) ExpiraEm = Utc(agora);
    }

    /// <summary>O visitante pode corrigir o que escreveu: vale o último envio.</summary>
    public void Identificar(ContatoInformadoVisitante contato)
    {
        ArgumentNullException.ThrowIfNull(contato);
        VisitanteNome = contato.Nome;
        VisitanteTelefone = contato.Telefone;
        VisitanteEmail = contato.Email;
        VisitanteInformadoEm = contato.InformadoEm;
    }

    public void VincularConversa(Guid conversaId)
    {
        if (conversaId == Guid.Empty)
            throw new RegraDeDominioVioladaException("ConversaId e obrigatorio.");
        ConversaId = conversaId;
    }

    private static DateTime Utc(DateTime d) => d.Kind switch
    {
        DateTimeKind.Utc => d,
        DateTimeKind.Local => d.ToUniversalTime(),
        _ => DateTime.SpecifyKind(d, DateTimeKind.Utc),
    };
}
