namespace EasyStock.Domain.Entities.Atendimento;

/// <summary>
/// Link do cardápio enviado numa conversa (S48): amarra o carrinho que o cliente monta no site à
/// <see cref="Conversa"/>. Vale 24 h e serve para um pedido só. Guarda apenas o hash SHA-256 (hex)
/// do token que vai na URL (<c>?c=</c>), que é aleatório e não carrega telefone, nome nem id.
/// </summary>
public class LinkCardapioConversa
{
    public static readonly TimeSpan Validade = TimeSpan.FromHours(24);
    public const int TokenHashTamanho = 64;

    public Guid Id { get; private set; }
    public Guid EmpresaId { get; private set; }
    public Guid ConversaId { get; private set; }
    public string TokenHash { get; private set; } = null!;
    public DateTime CriadoEm { get; private set; }
    public DateTime ExpiraEm { get; private set; }

    /// <summary>Quando o pedido foi enviado pela página. Preenchido, o link não serve mais.</summary>
    public DateTime? UsadoEm { get; private set; }

    // EF Core ctor sem parametros
    private LinkCardapioConversa() { }

    public static LinkCardapioConversa Gerar(Guid empresaId, Guid conversaId, string tokenHash, DateTime agora)
    {
        if (empresaId == Guid.Empty)
            throw new RegraDeDominioVioladaException("EmpresaId e obrigatorio.");
        if (conversaId == Guid.Empty)
            throw new RegraDeDominioVioladaException("ConversaId e obrigatorio.");
        if (string.IsNullOrEmpty(tokenHash) || tokenHash.Length != TokenHashTamanho)
            throw new RegraDeDominioVioladaException("Hash do token do link invalido.");

        var instante = Utc(agora);
        return new LinkCardapioConversa
        {
            Id = Guid.NewGuid(),
            EmpresaId = empresaId,
            ConversaId = conversaId,
            TokenHash = tokenHash,
            CriadoEm = instante,
            ExpiraEm = instante + Validade,
        };
    }

    public bool EstaValido(DateTime agora) => UsadoEm is null && Utc(agora) < ExpiraEm;

    /// <summary>Marca o link como usado. Vencido ou já usado não volta.</summary>
    public void Consumir(DateTime agora)
    {
        if (!EstaValido(agora))
            throw new RegraDeDominioVioladaException("Link do cardapio vencido ou ja usado.");
        UsadoEm = Utc(agora);
    }

    /// <summary>Devolve o uso quando o pedido não chegou a ser criado (carrinho recusado).</summary>
    public void Liberar() => UsadoEm = null;

    private static DateTime Utc(DateTime d) => d.Kind switch
    {
        DateTimeKind.Utc => d,
        DateTimeKind.Local => d.ToUniversalTime(),
        _ => DateTime.SpecifyKind(d, DateTimeKind.Utc),
    };
}
