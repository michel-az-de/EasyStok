using EasyStock.Domain.Exceptions;
using EasyStock.Domain.ValueObjects;

namespace EasyStock.Domain.Fiscal;

/// <summary>
/// Item de uma <see cref="NfeDocumento"/>. Snapshot dos dados de produto no
/// momento da emissao — preserva NCM/CFOP/nome mesmo se Produto for editado
/// depois. Subtotal calculado em construcao (Quantidade * PrecoUnitario.Valor).
/// </summary>
public class NfeItem
{
    public Guid Id { get; set; }
    public Guid NfeDocumentoId { get; set; }
    public NfeDocumento? NfeDocumento { get; set; }

    /// <summary>Ordem (1-based) dentro do documento. Indice (NfeDocumentoId, Ordem) garante leitura estavel.</summary>
    public int Ordem { get; set; }

    /// <summary>FK opcional ao Produto (snapshot — pode ser null para item ad-hoc).</summary>
    public Guid? ProdutoIdSnapshot { get; set; }

    public string NomeSnapshot { get; set; } = null!;

    /// <summary>NCM (Nomenclatura Comum do Mercosul) — 8 digitos. Obrigatorio em SEFAZ; nullable aqui pois Produto ainda nao tem campo (Corte 2 corrige).</summary>
    public string? NcmSnapshot { get; set; }

    /// <summary>CFOP (Codigo Fiscal de Operacoes) — 4 digitos. Ex: 5102 venda dentro do estado.</summary>
    public string? CfopSnapshot { get; set; }

    /// <summary>Origem da mercadoria (0..8). 0 = nacional. Ver tabela SEFAZ A.</summary>
    public byte OrigemMercadoria { get; set; }

    public decimal Quantidade { get; set; }
    public string Unidade { get; set; } = "UN";
    public Dinheiro PrecoUnitario { get; set; } = Dinheiro.Zero;
    public Dinheiro Subtotal { get; set; } = Dinheiro.Zero;

    /// <summary>CST (regime nao-Simples) ou CSOSN (regime Simples). 4 digitos. Determinado pelo regime tributario do emitente.</summary>
    public string? CstOuCsosn { get; set; }

    // ── Tributos por linha (§F-1 / PR-D) ────────────────────────────────────
    // Populados pelo parser/emissor da NFC-e a partir da Fase F2.
    // NULL em NFC-e legadas (antes da migration AddNfceTaxFields) — exibir como "Não rastreado".

    /// <summary>Base de cálculo do ICMS em R$. NULL para NFC-e legadas.</summary>
    public decimal? BaseIcms { get; set; }

    /// <summary>Valor do ICMS em R$. NULL para NFC-e legadas.</summary>
    public decimal? ValorIcms { get; set; }

    /// <summary>Valor do PIS em R$. NULL para NFC-e legadas.</summary>
    public decimal? Pis { get; set; }

    /// <summary>Valor do COFINS em R$. NULL para NFC-e legadas.</summary>
    public decimal? Cofins { get; set; }

    public DateTime CriadoEm { get; set; }

    public static NfeItem Criar(
        Guid nfeDocumentoId,
        int ordem,
        string nomeSnapshot,
        decimal quantidade,
        Dinheiro precoUnitario,
        string unidade,
        string? ncm = null,
        string? cfop = null,
        Guid? produtoIdSnapshot = null,
        byte origemMercadoria = 0,
        string? cstOuCsosn = null)
    {
        if (nfeDocumentoId == Guid.Empty)
            throw new ArgumentException("NfeDocumentoId obrigatorio.", nameof(nfeDocumentoId));
        if (string.IsNullOrWhiteSpace(nomeSnapshot))
            throw new ArgumentException("NomeSnapshot obrigatorio.", nameof(nomeSnapshot));
        if (quantidade <= 0m)
            throw new RegraDeDominioVioladaException("Quantidade deve ser maior que zero.");
        if (precoUnitario.Valor <= 0m)
            throw new RegraDeDominioVioladaException("PrecoUnitario deve ser maior que zero.");
        if (string.IsNullOrWhiteSpace(unidade))
            throw new ArgumentException("Unidade obrigatoria.", nameof(unidade));
        if (origemMercadoria > 8)
            throw new ArgumentOutOfRangeException(nameof(origemMercadoria), "OrigemMercadoria deve estar entre 0 e 8.");
        if (ncm is { } n && n.Length != 8)
            throw new ArgumentException("NCM deve ter 8 digitos quando informado.", nameof(ncm));
        if (cfop is { } c && c.Length != 4)
            throw new ArgumentException("CFOP deve ter 4 digitos quando informado.", nameof(cfop));

        return new NfeItem
        {
            Id = Guid.NewGuid(),
            NfeDocumentoId = nfeDocumentoId,
            Ordem = ordem,
            NomeSnapshot = nomeSnapshot.Trim(),
            NcmSnapshot = ncm,
            CfopSnapshot = cfop,
            OrigemMercadoria = origemMercadoria,
            Quantidade = quantidade,
            Unidade = unidade.Trim().ToUpperInvariant(),
            PrecoUnitario = precoUnitario,
            Subtotal = Dinheiro.FromDecimal(quantidade * precoUnitario.Valor),
            CstOuCsosn = cstOuCsosn,
            ProdutoIdSnapshot = produtoIdSnapshot,
            CriadoEm = DateTime.UtcNow,
        };
    }
}
