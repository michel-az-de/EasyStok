namespace EasyStock.Domain.Entities;

public class MovimentacaoEstoqueAlteracao
{
    public Guid Id { get; set; }
    public Guid EmpresaId { get; set; }
    public Guid MovimentacaoEstoqueId { get; set; }
    public Guid UsuarioId { get; set; }

    /// <summary>Snapshot do nome do usuário no momento da auditoria.</summary>
    public string? NomeUsuario { get; set; }

    /// <summary>Snapshot do email do usuário no momento da auditoria.</summary>
    public string? EmailUsuario { get; set; }

    /// <summary>criada | modificada | estornada</summary>
    public string Acao { get; set; } = null!;

    /// <summary>Razão selecionável da alteração (ex: "Ajuste de quantidade", "Corrigir estorno", etc.)</summary>
    public string? Motivo { get; set; }

    /// <summary>Nota livre do operador sobre esta alteração específica.</summary>
    public string? Observacao { get; set; }

    /// <summary>JSON array de {campo, de, para} — para rastrear mudanças em campos específicos.</summary>
    public string? AlteracoesJson { get; set; }

    /// <summary>IP da requisição que gerou a auditoria.</summary>
    public string? Ip { get; set; }

    /// <summary>UserAgent da requisição.</summary>
    public string? UserAgent { get; set; }

    public DateTime AlteradoEm { get; set; }

    // Navigation
    public MovimentacaoEstoque? MovimentacaoEstoque { get; set; }
    public Usuario? Usuario { get; set; }
}
