using System.Globalization;
using EasyStock.Application.Ports.Output.Persistence.Atendimento;

namespace EasyStock.Application.UseCases.Atendimento.Entregas;

public sealed record EntregasPorBairroLinha(string Bairro, int Pedidos, decimal Valor);

/// <summary>
/// S44: pedidos entregues e valor por bairro no período [de, ate), do maior valor para o menor.
/// Bairros iguais sem diferença de caixa ou espaço somam juntos; sem bairro vira "(sem bairro)".
/// </summary>
public sealed class EntregasPorBairroUseCase(IEntregasPorBairroQuery query)
{
    public const string SemBairro = "(sem bairro)";

    public async Task<IReadOnlyList<EntregasPorBairroLinha>> ExecuteAsync(Guid empresaId, DateTime de, DateTime ate, CancellationToken ct = default)
    {
        UseCaseGuards.EnsureEmpresaId(empresaId);
        if (ate <= de) throw new UseCaseValidationException("Período inválido: 'ate' precisa ser depois de 'de'.");

        var linhas = await query.ListarEntreguesAsync(empresaId, de, ate, ct);
        return linhas
            .GroupBy(l => Chave(l.Bairro))
            .Select(g => new EntregasPorBairroLinha(Rotulo(g), g.Count(), g.Sum(l => l.Valor)))
            .OrderByDescending(l => l.Valor)
            .ThenBy(l => l.Bairro, StringComparer.Create(new CultureInfo("pt-BR"), ignoreCase: true))
            .ToList();
    }

    private static string Chave(string? bairro) =>
        string.IsNullOrWhiteSpace(bairro) ? string.Empty : bairro.Trim().ToLower(CultureInfo.GetCultureInfo("pt-BR"));

    private static string Rotulo(IGrouping<string, PedidoEntregueLinha> g)
    {
        if (g.Key.Length == 0) return SemBairro;
        // Rótulo: a grafia mais frequente, com a primeira em maiúscula por desempate.
        return g.Select(l => l.Bairro!.Trim())
            .GroupBy(b => b)
            .OrderByDescending(x => x.Count())
            .ThenBy(x => char.IsUpper(x.Key[0]) ? 0 : 1)
            .First().Key;
    }
}
