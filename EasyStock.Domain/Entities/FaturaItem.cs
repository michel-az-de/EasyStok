namespace EasyStock.Domain.Entities;

/// <summary>
/// Item de uma <see cref="Fatura"/>. Snapshot de descricao+preco — nao referencia
/// <see cref="Produto"/> diretamente para preservar historico mesmo se produto
/// for renomeado/excluido.
/// </summary>
public class FaturaItem
{
    public Guid Id { get; set; }
    public Guid FaturaId { get; set; }
    public string Descricao { get; set; } = null!;
    public decimal Quantidade { get; set; }
    public decimal PrecoUnitario { get; set; }
    public decimal Subtotal { get; set; }
    public TipoItemFatura Tipo { get; set; } = TipoItemFatura.Servico;

    /// <summary>Ordem estavel de exibicao (pdf, ui).</summary>
    public int Ordem { get; set; }

    /// <summary>Referencia opcional ao produto/servico no catalogo (auditoria).</summary>
    public Guid? ProdutoId { get; set; }

    public DateTime CriadoEm { get; set; }

    public Fatura? Fatura { get; set; }

    public static FaturaItem Criar(
        Guid faturaId,
        string descricao,
        decimal quantidade,
        decimal precoUnitario,
        TipoItemFatura tipo,
        int ordem = 0,
        Guid? produtoId = null)
    {
        if (string.IsNullOrWhiteSpace(descricao))
            throw new ArgumentException("Descricao do item nao pode ser vazia.", nameof(descricao));
        if (quantidade <= 0m && tipo != TipoItemFatura.Desconto)
            throw new RegraDeDominioVioladaException("Quantidade deve ser maior que zero.");
        if (precoUnitario < 0m && tipo != TipoItemFatura.Desconto)
            throw new RegraDeDominioVioladaException("Preco unitario nao pode ser negativo.");

        var subtotal = Math.Round(quantidade * precoUnitario, 2, MidpointRounding.AwayFromZero);
        if (tipo == TipoItemFatura.Desconto && subtotal > 0m)
            subtotal = -subtotal;

        return new FaturaItem
        {
            Id = Guid.NewGuid(),
            FaturaId = faturaId,
            Descricao = descricao.Trim(),
            Quantidade = quantidade,
            PrecoUnitario = precoUnitario,
            Subtotal = subtotal,
            Tipo = tipo,
            Ordem = ordem,
            ProdutoId = produtoId,
            CriadoEm = DateTime.UtcNow
        };
    }
}
