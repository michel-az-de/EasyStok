namespace EasyStock.Application.UseCases.Lotes;

/// <summary>
/// Regras de validacao de item de lote compartilhadas entre CriarLote e AdicionarItemLote,
/// para que as duas portas de entrada nao divirjam (#2605).
/// </summary>
internal static class LoteItemValidacao
{
    /// <summary>Teto de validade aceito (10 anos); acima disso o AddDays estoura o DateTime.</summary>
    internal const int ValidadeDiasMaxima = 3650;

    /// <summary>
    /// Regras do CriarLote: Quantidade, Nome, R1 (ProdutoId obrigatorio) e R5 (peso
    /// obrigatorio para produto Embalado, RDC 727/2022).
    /// </summary>
    internal static void Validar(
        string? nome,
        int quantidade,
        Guid? produtoId,
        int? pesoG,
        IReadOnlyDictionary<Guid, TipoEmbalagem> tipoEmbalagemMap)
    {
        if (quantidade <= 0)
            throw new UseCaseValidationException("Quantidade deve ser maior que zero.");

        if (string.IsNullOrWhiteSpace(nome))
            throw new UseCaseValidationException("Nome do item é obrigatório.");

        // R1: ProdutoId obrigatório — sem vínculo, não há como validar
        // TipoEmbalagem e o bypass silencioso da RDC 727 fica possível.
        if (!produtoId.HasValue)
            throw new UseCaseValidationException(
                $"Item '{nome}' precisa estar vinculado a um produto cadastrado.");

        // R5: peso obrigatório APENAS quando produto é Embalado (RDC 727/2022).
        if (tipoEmbalagemMap.TryGetValue(produtoId.Value, out var tipoEmb)
            && tipoEmb == TipoEmbalagem.Embalado
            && (!pesoG.HasValue || pesoG.Value <= 0))
        {
            throw new UseCaseValidationException(
                $"Peso por unidade obrigatório para o item '{nome}' " +
                $"(produto está marcado como Embalado — RDC 727/2022).");
        }
    }

    /// <summary>Faixas de PesoG e ValidadeDias informados.</summary>
    internal static void ValidarFaixas(int? pesoG, int? validadeDias)
    {
        if (pesoG.HasValue && pesoG.Value <= 0)
            throw new UseCaseValidationException("Peso por unidade deve ser maior que zero.");

        if (validadeDias.HasValue && (validadeDias.Value < 0 || validadeDias.Value > ValidadeDiasMaxima))
            throw new UseCaseValidationException(
                $"Validade em dias deve estar entre 0 e {ValidadeDiasMaxima}.");
    }
}
