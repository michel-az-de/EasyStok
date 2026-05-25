namespace EasyStock.Domain.Entities;

/// <summary>
/// Auditoria E2E de mudancas em receita (composicao). Segue padrao <see cref="ProdutoAlteracao"/>:
/// gravado explicitamente pelo use case que altera, nao via interceptor EF.
/// </summary>
public class ProdutoComposicaoAlteracao
{
    public Guid Id { get; set; }
    public Guid EmpresaId { get; set; }
    public Guid ProdutoFinalId { get; set; }
    public Guid? LojaId { get; set; }
    public Guid UsuarioId { get; set; }

    /// <summary>criada | atualizada | removida</summary>
    public string Acao { get; set; } = null!;

    /// <summary>
    /// JSON com diff: { added: [...], removed: [...], updated: [{ insumoId, campo, antes, depois }] }.
    /// Granularidade real do que mudou na substituicao replace-all.
    /// </summary>
    public string? AlteracoesJson { get; set; }

    /// <summary>Nota livre do operador sobre esta alteracao.</summary>
    public string? Observacao { get; set; }

    public DateTime AlteradoEm { get; set; }

    // Navigation
    public Produto? ProdutoFinal { get; set; }
    public Loja? Loja { get; set; }
    public Usuario? Usuario { get; set; }
}
