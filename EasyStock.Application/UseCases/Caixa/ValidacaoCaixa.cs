using EasyStock.Domain.Financeiro;

namespace EasyStock.Application.UseCases.Caixa;

/// <summary>Regras de entrada comuns a abertura, fechamento e movimento do caixa (#2305).</summary>
public static class ValidacaoCaixa
{
    /// <summary>Teto do valor: a coluna é numeric(18,2), mas nenhum lançamento real passa de um bilhão.</summary>
    public const decimal ValorMaximo = 999_999_999.99m;

    /// <summary>Valor com no máximo duas casas e dentro do teto; 0,004 gravava 0,00 e 1e13 estourava a coluna (500).</summary>
    public static void ExigirValorMonetario(decimal valor, string campo)
    {
        if (valor != decimal.Round(valor, 2))
            throw new UseCaseValidationException($"{campo} deve ter até duas casas decimais.");
        if (valor > ValorMaximo)
            throw new UseCaseValidationException($"{campo} não pode passar de R$ 999.999.999,99.");
    }

    /// <summary>Forma normalizada (a mesma que a gaveta usa) ou erro; vazio segue vazio.</summary>
    public static string? NormalizarMetodo(string? metodo)
    {
        if (string.IsNullOrWhiteSpace(metodo)) return null;
        return MetodosPagamentoCaixa.Normalizar(metodo)
            ?? throw new UseCaseValidationException(
                $"Forma de pagamento inválida. Use: {string.Join(", ", MetodosPagamentoCaixa.Todos)}.");
    }
}
