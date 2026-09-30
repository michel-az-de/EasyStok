namespace EasyStock.Application.Ports.Output.Persistence;

/// <summary>Saída que pode ter ido a descoberto (S22): instante, quantidade e referência do documento (pedido).</summary>
public sealed record SaidaDesacertoLinha(DateTime Em, decimal Quantidade, string? DocumentoReferencia);

/// <summary>Produto com <c>SUM(QuantidadeDescoberta) &gt; 0</c> e as saídas dos lotes descobertos desde o último ajuste.</summary>
public sealed record DesacertoProdutoLinha(
    Guid ProdutoId,
    string Nome,
    decimal QuantidadeDescoberta,
    decimal QuantidadeAtual,
    IReadOnlyList<SaidaDesacertoLinha> Saidas);

/// <summary>
/// Alerta de desacerto de estoque (S22, RN-48/RN-49): projeção sobre <c>ItemEstoque.QuantidadeDescoberta</c>,
/// sem tabela própria. <c>EmpresaId</c> no WHERE além do RLS (ADR-0010).
/// </summary>
public interface IDesacertosEstoqueQueries
{
    Task<IReadOnlyList<DesacertoProdutoLinha>> ListarAsync(Guid empresaId, Guid? lojaId, CancellationToken ct = default);
}
