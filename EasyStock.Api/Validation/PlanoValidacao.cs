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

    // Teto de plano gratuito (ADM-07 / #639): R$0 nao pode liberar limites ilimitados nem altos —
    // senao vira "tudo de graca" (o QA achou um Starter R$0 com 999/999/99999/100). Tetos
    // calibraveis; limite >0 e dentro do teto = ok. So se aplica a plano sem custo (preco == 0).
    public const int GratuitoMaxLojas = 2;
    public const int GratuitoMaxUsuarios = 10;
    public const int GratuitoMaxProdutos = 1000;
    public const int GratuitoMaxIaMensal = 50;

    public static string? ValidarPlanoGratuito(
        decimal preco, int limiteLojas, int limiteUsuarios, int limiteProdutos, int limiteGeracoesIaMensais)
    {
        if (preco != 0) return null;

        if (limiteLojas == SemLimite || limiteUsuarios == SemLimite
            || limiteProdutos == SemLimite || limiteGeracoesIaMensais == SemLimite)
            return "Plano gratuito (R$0) não pode ter limites ilimitados (∞). Defina limites finitos.";

        if (limiteLojas > GratuitoMaxLojas || limiteUsuarios > GratuitoMaxUsuarios
            || limiteProdutos > GratuitoMaxProdutos || limiteGeracoesIaMensais > GratuitoMaxIaMensal)
            return $"Plano gratuito (R$0) excede o teto permitido (máx.: {GratuitoMaxLojas} lojas, "
                 + $"{GratuitoMaxUsuarios} usuários, {GratuitoMaxProdutos} produtos, {GratuitoMaxIaMensal} gerações de IA/mês).";

        return null;
    }

    // Politica de nome (#743): o catalogo de planos e global/publico — nao deve conter o nome de um
    // cliente especifico (ex.: "Demo Comercial Casa da Baba"). Bloqueia se o nome do plano contiver
    // o nome de um tenant existente com >=5 chars (o piso evita falso-positivo de nomes curtos, ex.:
    // tenant "Pro" bloquearia "Profissional"). Comparacao case-insensitive.
    public static string? ValidarNomeNaoColideComTenant(string? nomePlano, IEnumerable<string> nomesTenants)
    {
        var alvo = (nomePlano ?? "").Trim().ToLowerInvariant();
        if (alvo.Length == 0) return null;
        foreach (var t in nomesTenants)
        {
            var tn = (t ?? "").Trim();
            if (tn.Length >= 5 && alvo.Contains(tn.ToLowerInvariant()))
                return $"Nome de plano não pode conter o nome de um cliente (\"{tn}\"). Use um nome "
                     + "genérico de catálogo; condições especiais por cliente vão em override, não no catálogo.";
        }
        return null;
    }
}
