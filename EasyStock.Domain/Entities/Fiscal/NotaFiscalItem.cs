using System;
using EasyStock.Domain.Enums.Fiscal;
using EasyStock.Domain.ValueObjects;
using EasyStock.Domain.ValueObjects.Fiscal;

namespace EasyStock.Domain.Entities.Fiscal;

/// <summary>
/// Item de uma NotaFiscal. Carrega snapshot do produto vendido (descrição,
/// código, EAN) e classificação fiscal (NCM, CFOP, CST/CSOSN, tributos).
/// Quantidade é decimal com 4 casas (numeric(15,4)) — fiscal aceita
/// fracionários (kg, m, etc).
/// </summary>
public sealed class NotaFiscalItem
{
    public Guid Id { get; private set; }
    public Guid NotaFiscalId { get; private set; }
    public Guid EmpresaId { get; private set; }
    public int Ordem { get; private set; }
    public Guid? ProdutoId { get; private set; }
    public string DescricaoSnapshot { get; private set; } = null!;
    public string CodigoProduto { get; private set; } = null!;
    public string? Ean { get; private set; }
    public NCM Ncm { get; private set; } = null!;
    public CFOP Cfop { get; private set; } = null!;
    public string? Cest { get; private set; }
    public string UnidadeComercial { get; private set; } = null!;
    public decimal Quantidade { get; private set; }
    public decimal PrecoUnitario { get; private set; }
    public decimal Desconto { get; private set; }
    public Dinheiro Subtotal { get; private set; } = Dinheiro.Zero;
    public OrigemMercadoria OrigemMercadoria { get; private set; }
    public CSTouCSOSN CstCsosn { get; private set; } = null!;

    public int? IcmsModalidadeBC { get; private set; }
    public decimal? IcmsAliquota { get; private set; }
    public Dinheiro? IcmsValor { get; private set; }

    public string CstPis { get; private set; } = null!;
    public decimal? PisAliquota { get; private set; }
    public Dinheiro? PisValor { get; private set; }

    public string CstCofins { get; private set; } = null!;
    public decimal? CofinsAliquota { get; private set; }
    public Dinheiro? CofinsValor { get; private set; }

    public string? IbsCst { get; private set; }
    public decimal? IbsAliquota { get; private set; }
    public Dinheiro? IbsValor { get; private set; }

    public string? CbsCst { get; private set; }
    public decimal? CbsAliquota { get; private set; }
    public Dinheiro? CbsValor { get; private set; }

    public string? IsCst { get; private set; }
    public decimal? IsAliquota { get; private set; }
    public Dinheiro? IsValor { get; private set; }

    private NotaFiscalItem() { }

    public static NotaFiscalItem Criar(
        Guid notaFiscalId,
        Guid empresaId,
        int ordem,
        Guid? produtoId,
        string descricaoSnapshot,
        string codigoProduto,
        string? ean,
        NCM ncm,
        CFOP cfop,
        string? cest,
        string unidadeComercial,
        decimal quantidade,
        decimal precoUnitario,
        decimal desconto,
        OrigemMercadoria origem,
        CSTouCSOSN cstCsosn,
        string cstPis,
        string cstCofins)
    {
        if (notaFiscalId == Guid.Empty)
            throw new ArgumentException("NotaFiscalId é obrigatório.", nameof(notaFiscalId));
        if (empresaId == Guid.Empty)
            throw new ArgumentException("EmpresaId é obrigatório.", nameof(empresaId));
        if (ordem <= 0)
            throw new ArgumentOutOfRangeException(nameof(ordem), "Ordem deve ser positiva.");
        if (string.IsNullOrWhiteSpace(descricaoSnapshot))
            throw new ArgumentException("Descrição é obrigatória.", nameof(descricaoSnapshot));
        if (descricaoSnapshot.Length > 120)
            throw new ArgumentException("Descrição não pode exceder 120 caracteres.", nameof(descricaoSnapshot));
        if (string.IsNullOrWhiteSpace(codigoProduto))
            throw new ArgumentException("Código do produto é obrigatório.", nameof(codigoProduto));
        if (ncm is null)
            throw new ArgumentNullException(nameof(ncm));
        if (cfop is null)
            throw new ArgumentNullException(nameof(cfop));
        if (cstCsosn is null)
            throw new ArgumentNullException(nameof(cstCsosn));
        if (string.IsNullOrWhiteSpace(unidadeComercial))
            throw new ArgumentException("Unidade comercial é obrigatória.", nameof(unidadeComercial));
        if (quantidade <= 0)
            throw new ArgumentOutOfRangeException(nameof(quantidade), "Quantidade deve ser positiva.");
        if (precoUnitario < 0)
            throw new ArgumentOutOfRangeException(nameof(precoUnitario), "Preço unitário não pode ser negativo.");
        if (desconto < 0)
            throw new ArgumentOutOfRangeException(nameof(desconto), "Desconto não pode ser negativo.");
        if (string.IsNullOrWhiteSpace(cstPis))
            throw new ArgumentException("CST PIS é obrigatório.", nameof(cstPis));
        if (string.IsNullOrWhiteSpace(cstCofins))
            throw new ArgumentException("CST COFINS é obrigatório.", nameof(cstCofins));

        var subtotalBruto = Math.Round(quantidade * precoUnitario - desconto, 2, MidpointRounding.AwayFromZero);
        if (subtotalBruto < 0)
            throw new InvalidOperationException("Subtotal não pode ficar negativo (desconto maior que total).");

        return new NotaFiscalItem
        {
            Id = Guid.NewGuid(),
            NotaFiscalId = notaFiscalId,
            EmpresaId = empresaId,
            Ordem = ordem,
            ProdutoId = produtoId,
            DescricaoSnapshot = descricaoSnapshot.Trim(),
            CodigoProduto = codigoProduto.Trim(),
            Ean = string.IsNullOrWhiteSpace(ean) ? null : ean.Trim(),
            Ncm = ncm,
            Cfop = cfop,
            Cest = string.IsNullOrWhiteSpace(cest) ? null : cest.Trim(),
            UnidadeComercial = unidadeComercial.Trim(),
            Quantidade = quantidade,
            PrecoUnitario = precoUnitario,
            Desconto = desconto,
            Subtotal = Dinheiro.FromDecimal(subtotalBruto),
            OrigemMercadoria = origem,
            CstCsosn = cstCsosn,
            CstPis = cstPis.Trim(),
            CstCofins = cstCofins.Trim(),
        };
    }

    public void DefinirIcms(int? modalidadeBC, decimal? aliquota, Dinheiro? valor)
    {
        IcmsModalidadeBC = modalidadeBC;
        IcmsAliquota = aliquota;
        IcmsValor = valor;
    }

    public void DefinirPis(decimal? aliquota, Dinheiro? valor)
    {
        PisAliquota = aliquota;
        PisValor = valor;
    }

    public void DefinirCofins(decimal? aliquota, Dinheiro? valor)
    {
        CofinsAliquota = aliquota;
        CofinsValor = valor;
    }

    public void DefinirIbs(string? cst, decimal? aliquota, Dinheiro? valor)
    {
        IbsCst = cst;
        IbsAliquota = aliquota;
        IbsValor = valor;
    }

    public void DefinirCbs(string? cst, decimal? aliquota, Dinheiro? valor)
    {
        CbsCst = cst;
        CbsAliquota = aliquota;
        CbsValor = valor;
    }

    public void DefinirIs(string? cst, decimal? aliquota, Dinheiro? valor)
    {
        IsCst = cst;
        IsAliquota = aliquota;
        IsValor = valor;
    }
}
