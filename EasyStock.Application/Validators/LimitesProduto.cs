namespace EasyStock.Application.Validators;

/// <summary>
/// Limites de sanidade para valores de produto. O teto NAO e regra de negocio: e guarda
/// anti-erro-de-digitacao (o "R$ 142 milhoes" do QA era fat-finger; decimal(18,2) nao
/// estoura em R$100M e o layout ja foi corrigido). O backend e a fonte de verdade; o
/// frontend (Form.cshtml) clampa no mesmo valor por CONVENCAO, pois o Web e um BFF sem
/// referencia ao EasyStock.Application — ao mudar aqui, atualizar o literal do Form.cshtml.
///
/// Enforcement (ponto unico via <see cref="EnsurePreco"/>):
///  - Criacao: sempre (CadastrarProduto.Validar + CadastrarProdutoCommandValidator no BFF).
///  - Edicao: SO quando o preco/custo muda (on-change) — preserva registro legado acima do
///    teto intocado, mas barra fat-finger novo na edicao (QA PROD-02). Ver AtualizarProdutoUseCase.
/// </summary>
public static class LimitesProduto
{
    /// <summary>Teto de sanidade para preco/custo de referencia: R$ 99.999.999,99.</summary>
    public const decimal ValorMaximo = 99_999_999.99m;

    /// <summary>
    /// Ponto unico de enforcement do teto. Lanca <see cref="UseCaseValidationException"/>
    /// (HTTP 400 limpo) quando o valor excede <see cref="ValorMaximo"/>. No-op para null
    /// (ausencia de valor) — piso (&gt; 0) e regra de outras camadas; aqui so o teto importa.
    /// </summary>
    public static void EnsurePreco(decimal? valor, string campoAmigavel)
    {
        if (valor.HasValue && valor.Value > ValorMaximo)
            throw new UseCaseValidationException(
                $"{campoAmigavel} deve ser no máximo R$ 99.999.999,99.");
    }
}
