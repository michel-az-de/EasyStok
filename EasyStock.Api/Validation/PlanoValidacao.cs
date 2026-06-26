namespace EasyStock.Api.Validation;

/// <summary>
/// Regras de validacao de plano na fronteira da API, extraidas do AdminPlanosController
/// para ficarem testaveis em isolamento. Retornam a mensagem de erro (pt-BR) ou null
/// quando valido. Espelha <see cref="CupomValidacao"/> (INV-001 / #463): fecha a
/// inconsistencia em que Cupom validava e Plano aceitava qualquer numero (negativos
/// persistiam: preco -50, limites -5/-10/-100).
///
/// Limite -1 = ilimitado (sentinela <c>Plano.SemLimite</c> no Domain). Valores menores
/// que -1 sao invalidos; >= 0 sao limites finitos. Preco nao tem sentinela de ilimitado:
/// deve ser >= 0.
/// </summary>
public static class PlanoValidacao
{
    // Espelha EasyStock.Domain.Entities.Plano.SemLimite. Const local para manter a
    // classe pura e testavel sem acoplar a regra de validacao a entidade do Domain.
    public const int SemLimite = -1;

    public static string? ValidarNome(string? nome)
    {
        var n = (nome ?? "").Trim();
        if (n.Length is < 2 or > 80)
            return "Nome do plano deve ter entre 2 e 80 caracteres.";
        return null;
    }

    public static string? ValidarLimite(int valor, string campo)
    {
        if (valor < SemLimite)
            return $"{campo} deve ser -1 (ilimitado) ou um valor não-negativo.";
        return null;
    }

    public static string? ValidarPreco(decimal preco)
    {
        if (preco < 0)
            return "Preço mensal não pode ser negativo.";
        return null;
    }
}
