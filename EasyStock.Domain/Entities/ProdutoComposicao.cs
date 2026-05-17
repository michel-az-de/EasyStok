using EasyStock.Domain.Enums;

namespace EasyStock.Domain.Entities;

/// <summary>
/// Linha da receita (BOM) de um produto-final: identifica quanto de cada insumo
/// e necessario para produzir uma unidade-base do produto-final.
///
/// Multi-loja: <see cref="LojaId"/> null = receita padrao (fallback); preenchido = override
/// daquela loja. Chave unique (EmpresaId, ProdutoFinalId, InsumoId, LojaId) permite ambas.
/// </summary>
public class ProdutoComposicao
{
    public Guid Id { get; set; }
    public Guid EmpresaId { get; set; }
    public Guid ProdutoFinalId { get; set; }
    public Guid InsumoId { get; set; }

    /// <summary>Null = receita padrao da empresa; preenchido = override por loja.</summary>
    public Guid? LojaId { get; set; }

    /// <summary>Quantidade do insumo necessaria para 1 unidade-base do produto-final (escala via Produto.RendimentoBase).</summary>
    public decimal Quantidade { get; set; }

    /// <summary>Unidade da quantidade — pode diferir da <see cref="Produto.UnidadeMedidaBase"/> do insumo; calculadora converte.</summary>
    public UnidadeMedida Unidade { get; set; }

    public string? Observacao { get; set; }
    public int OrdemExibicao { get; set; }

    public DateTime CriadoEm { get; set; }
    public DateTime AlteradoEm { get; set; }
    public Guid? CriadoPor { get; set; }
    public Guid? AlteradoPor { get; set; }

    // Navigation
    public Empresa? Empresa { get; set; }
    public Produto? ProdutoFinal { get; set; }
    public Produto? Insumo { get; set; }
    public Loja? Loja { get; set; }
}
