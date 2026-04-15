namespace EasyStock.Domain.Entities;

public class ProdutoAlteracao
{
    public Guid Id { get; set; }
    public Guid EmpresaId { get; set; }
    public Guid ProdutoId { get; set; }
    public Guid UsuarioId { get; set; }

    /// <summary>cadastrado | atualizado | inativado | restaurado</summary>
    public string Acao { get; set; } = null!;

    /// <summary>JSON array de {campo, de, para} — apenas para Acao = "atualizado"</summary>
    public string? AlteracoesJson { get; set; }

    public DateTime AlteradoEm { get; set; }

    // Navigation
    public Produto? Produto { get; set; }
    public Usuario? Usuario { get; set; }
}
