using EasyStock.Domain.ValueObjects;

namespace EasyStock.Domain.Entities.Storefront;

/// <summary>
/// Opção selecionável de um <see cref="CardapioItem"/> guarda-chuva (ADR-0035).
/// Ex.: "Ravioli de Abóbora" com opções "300g" (R$28) e "800g" (R$42) — um único card
/// no cardápio, com preço próprio por opção. Funciona para itens avulsos (sem ERP) e vinculados.
///
/// <para>
/// <strong>Preço absoluto</strong>: <see cref="PrecoStorefront"/> é o preço final da opção em R$
/// (não um delta). Centavos só no DTO de saída.
/// </para>
///
/// <para>
/// <strong>Rastreabilidade</strong>: <see cref="ProdutoVariacaoId"/> liga, opcionalmente, a uma
/// <see cref="ProdutoVariacao"/> do ERP — só quando o <see cref="CardapioItem"/> pai é vinculado e a
/// variação é do mesmo Produto (validado na Application). <see cref="Sku"/> é congelado na linha de pedido.
/// </para>
///
/// <para>
/// <strong>Rótulo</strong> preserva a caixa digitada ("P", "G", "300g"); a unicidade por item é
/// case-insensitive (coluna gerada <c>rotulo_lower</c> + UNIQUE DEFERRABLE — ver migration).
/// <strong>EhPadrao</strong> é invariante de agregado (≤1 por item, orquestrado por
/// <see cref="CardapioItem.DefinirVariacaoPadrao"/>), sem índice de banco.
/// </para>
/// </summary>
public class CardapioItemVariacao
{
    public Guid Id { get; private set; }
    public Guid CardapioItemId { get; private set; }

    /// <summary>Rótulo exibido (ex.: "300g", "P", "G"). Preserva a caixa; unicidade compara em lowercase.</summary>
    public string Rotulo { get; private set; } = null!;

    /// <summary>Preço absoluto da opção em R$ (decimal(10,2), &gt;= 0).</summary>
    public decimal PrecoStorefront { get; private set; }

    /// <summary>SKU distinguível da opção (congelado na linha de pedido). Null = sem SKU próprio.</summary>
    public CodigoSku? Sku { get; private set; }

    /// <summary>FK opcional para <see cref="ProdutoVariacao"/> do ERP (rastreabilidade).</summary>
    public Guid? ProdutoVariacaoId { get; private set; }

    /// <summary>Peso/porção para exibição da opção (ex.: "300g"). Texto livre, max 50.</summary>
    public string? PesoExibicao { get; private set; }

    /// <summary>"Esgotado da opção". Default true.</summary>
    public bool Disponivel { get; private set; }

    /// <summary>Ordem de exibição no card (double — permite inserir entre 1.0 e 2.0 com 1.5).</summary>
    public double OrdemExibicao { get; private set; }

    /// <summary>Opção pré-selecionada no card. Invariante: ≤1 por item (controlado pelo agregado).</summary>
    public bool EhPadrao { get; private set; }

    public DateTime CriadoEm { get; private set; }
    public DateTime AlteradoEm { get; private set; }

    public CardapioItem? CardapioItem { get; set; }
    public ProdutoVariacao? ProdutoVariacao { get; set; }

    // EF Core ctor sem parâmetros
    private CardapioItemVariacao() { }

    /// <summary>
    /// Factory: cria uma opção do item guarda-chuva.
    /// </summary>
    /// <param name="cardapioItemId">Item de cardápio dono da opção.</param>
    /// <param name="rotulo">Rótulo exibido (ex.: "300g"). Obrigatório, max 60. Preserva a caixa.</param>
    /// <param name="precoEmReais">Preço absoluto em R$ (&gt;= 0).</param>
    /// <param name="ordemExibicao">Ordem no card (&gt;= 0).</param>
    /// <param name="ehPadrao">Marca como pré-selecionada (use o agregado para garantir ≤1 por item).</param>
    /// <param name="pesoExibicao">Peso/porção opcional para exibição.</param>
    /// <param name="sku">SKU opcional.</param>
    /// <param name="produtoVariacaoId">Link opcional à variação do ERP.</param>
    public static CardapioItemVariacao Criar(
        Guid cardapioItemId,
        string rotulo,
        decimal precoEmReais,
        double ordemExibicao = 0,
        bool ehPadrao = false,
        string? pesoExibicao = null,
        CodigoSku? sku = null,
        Guid? produtoVariacaoId = null)
    {
        if (cardapioItemId == Guid.Empty)
            throw new RegraDeDominioVioladaException("CardapioItemId é obrigatório.");

        var rotuloLimpo = NormalizarRotulo(rotulo);

        if (precoEmReais < 0m)
            throw new RegraDeDominioVioladaException(
                $"Preço da opção não pode ser negativo (recebido: {precoEmReais:0.00}).");

        if (ordemExibicao < 0)
            throw new RegraDeDominioVioladaException(
                $"Ordem de exibição não pode ser negativa (recebido: {ordemExibicao}).");

        var pesoLimpo = NormalizarPeso(pesoExibicao);

        var agora = DateTime.UtcNow;
        return new CardapioItemVariacao
        {
            Id = Guid.NewGuid(),
            CardapioItemId = cardapioItemId,
            Rotulo = rotuloLimpo,
            PrecoStorefront = decimal.Round(precoEmReais, 2, MidpointRounding.AwayFromZero),
            Sku = sku,
            ProdutoVariacaoId = produtoVariacaoId,
            PesoExibicao = pesoLimpo,
            Disponivel = true,
            OrdemExibicao = ordemExibicao,
            EhPadrao = ehPadrao,
            CriadoEm = agora,
            AlteradoEm = agora,
        };
    }

    /// <summary>Atualiza os campos editáveis da opção (usado pela reconciliação keyed-by-Id do admin).</summary>
    public void Atualizar(
        string rotulo,
        decimal precoEmReais,
        double ordemExibicao,
        string? pesoExibicao = null,
        CodigoSku? sku = null,
        Guid? produtoVariacaoId = null)
    {
        var rotuloLimpo = NormalizarRotulo(rotulo);

        if (precoEmReais < 0m)
            throw new RegraDeDominioVioladaException(
                $"Preço da opção não pode ser negativo (recebido: {precoEmReais:0.00}).");

        if (ordemExibicao < 0)
            throw new RegraDeDominioVioladaException(
                $"Ordem de exibição não pode ser negativa (recebido: {ordemExibicao}).");

        Rotulo = rotuloLimpo;
        PrecoStorefront = decimal.Round(precoEmReais, 2, MidpointRounding.AwayFromZero);
        OrdemExibicao = ordemExibicao;
        PesoExibicao = NormalizarPeso(pesoExibicao);
        Sku = sku;
        ProdutoVariacaoId = produtoVariacaoId;
        AlteradoEm = DateTime.UtcNow;
    }

    public void MarcarEsgotado()
    {
        if (!Disponivel) return;
        Disponivel = false;
        AlteradoEm = DateTime.UtcNow;
    }

    public void MarcarDisponivel()
    {
        if (Disponivel) return;
        Disponivel = true;
        AlteradoEm = DateTime.UtcNow;
    }

    /// <summary>Define/limpa o flag de padrão. Use <see cref="CardapioItem.DefinirVariacaoPadrao"/> para garantir ≤1 por item.</summary>
    public void DefinirPadrao(bool valor)
    {
        if (EhPadrao == valor) return;
        EhPadrao = valor;
        AlteradoEm = DateTime.UtcNow;
    }

    private static string NormalizarRotulo(string rotulo)
    {
        var limpo = (rotulo ?? string.Empty).Trim();
        if (limpo.Length == 0)
            throw new RegraDeDominioVioladaException("Rótulo da opção é obrigatório.");
        if (limpo.Length > 60)
            throw new RegraDeDominioVioladaException(
                $"Rótulo da opção não pode exceder 60 caracteres (recebido: {limpo.Length}).");
        return limpo;
    }

    private static string? NormalizarPeso(string? pesoExibicao)
    {
        if (pesoExibicao is null) return null;
        var limpo = pesoExibicao.Trim();
        if (limpo.Length == 0) return null;
        if (limpo.Length > 50)
            throw new RegraDeDominioVioladaException("Peso de exibição não pode exceder 50 caracteres.");
        return limpo;
    }
}
