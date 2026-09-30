using System.Globalization;

namespace EasyStock.Application.UseCases.Inventario.Desacertos;

public sealed record ListarDesacertosEstoqueInput(Guid EmpresaId, Guid? LojaId = null);

/// <summary>Um alerta de desacerto (S22): texto legível para a dona e os pedidos que venderam sem saldo.</summary>
public sealed record DesacertoEstoqueDto(
    Guid ProdutoId,
    string Nome,
    decimal QuantidadeDescoberta,
    decimal QuantidadeAtual,
    string Texto,
    IReadOnlyList<string> Pedidos,
    DateTime? PrimeiroEm,
    DateTime? UltimoEm);

/// <summary>
/// Lista os desacertos de estoque (S22, RN-48/RN-49, UC-09). Sem tabela: o alerta é
/// <c>SUM(QuantidadeDescoberta) &gt; 0</c> por produto e fecha por construção quando o ajuste zera o descoberto.
/// </summary>
public sealed class ListarDesacertosEstoqueUseCase(IDesacertosEstoqueQueries queries)
{
    private static readonly CultureInfo PtBr = CultureInfo.GetCultureInfo("pt-BR");

    public async Task<IReadOnlyList<DesacertoEstoqueDto>> ExecuteAsync(
        ListarDesacertosEstoqueInput input, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(input);
        UseCaseGuards.EnsureEmpresaId(input.EmpresaId);

        var linhas = await queries.ListarAsync(input.EmpresaId, input.LojaId, ct);
        return linhas.Select(Mapear).ToList();
    }

    private static DesacertoEstoqueDto Mapear(DesacertoProdutoLinha l)
    {
        var saidas = l.Saidas.OrderBy(s => s.Em).ToList();
        var pedidos = saidas
            .Select(s => NumeroPedido(s.DocumentoReferencia))
            .OfType<string>()
            .Distinct()
            .ToList();
        DateTime? primeiro = saidas.Count > 0 ? saidas[0].Em : null;
        DateTime? ultimo = saidas.Count > 0 ? saidas[^1].Em : null;

        var texto = $"Vendeu {l.QuantidadeDescoberta.ToString("0.###", PtBr)} de {l.Nome} sem produção lançada";
        if (ultimo is not null)
            texto += $" em {HorarioBrasil.DataOperacional(ultimo.Value).ToString("dd/MM", PtBr)}";
        if (pedidos.Count > 0)
            texto += $" (pedidos {string.Join(", ", pedidos.Select(p => "#" + p))})";

        return new DesacertoEstoqueDto(l.ProdutoId, l.Nome, l.QuantidadeDescoberta, l.QuantidadeAtual,
            texto, pedidos, primeiro, ultimo);
    }

    /// <summary>
    /// A baixa do pedido grava <c>DocumentoReferencia = "{pedidoId}:{itemId}"</c> (S17); o número curto é o
    /// mesmo que o cliente vê (8 primeiros caracteres do id, maiúsculos). Aceita também <c>"pedido:{id}"</c>.
    /// Outras referências (nota fiscal, contagem) ficam de fora.
    /// </summary>
    internal static string? NumeroPedido(string? documentoReferencia)
    {
        if (string.IsNullOrWhiteSpace(documentoReferencia)) return null;
        var partes = documentoReferencia.Split(':');
        var candidato = partes[0].Equals("pedido", StringComparison.OrdinalIgnoreCase) && partes.Length > 1
            ? partes[1]
            : partes[0];
        return Guid.TryParse(candidato, out var id)
            ? id.ToString("N")[..8].ToUpperInvariant()
            : null;
    }
}
