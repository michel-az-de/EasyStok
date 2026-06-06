namespace EasyStock.Application.Validators;

/// <summary>
/// Limites de sanidade para valores de produto. O teto NAO e regra de negocio: e guarda
/// anti-erro-de-digitacao (o "R$ 142 milhoes" do QA era fat-finger; decimal(18,2) nao
/// estoura em R$100M e o layout ja foi corrigido). O backend e a fonte de verdade; o
/// frontend (Form.cshtml) clampa no mesmo valor por CONVENCAO, pois o Web e um BFF sem
/// referencia ao EasyStock.Application — ao mudar aqui, atualizar o literal do Form.cshtml.
/// Aplicado so na criacao (validar): a edicao fica leniente para nao bloquear registro legado.
/// </summary>
public static class LimitesProduto
{
    /// <summary>Teto de sanidade para preco/custo de referencia: R$ 99.999.999,99.</summary>
    public const decimal ValorMaximo = 99_999_999.99m;
}
