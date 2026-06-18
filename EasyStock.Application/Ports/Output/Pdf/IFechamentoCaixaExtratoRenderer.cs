namespace EasyStock.Application.Ports.Output.Pdf;

/// <summary>
/// Renderer do extrato de fechamento de caixa em PDF (issue #642) — abstrai a lib (QuestPDF)
/// da camada Application/Api. Implementação em
/// <c>EasyStock.Infra.Async/Pdf/FechamentoCaixaExtratoRenderer.cs</c>.
///
/// <para>O DTO é flat (sem entidades de Domínio): o use case mapeia
/// FechamentoCaixa/MovimentoCaixa/Loja/Empresa → <see cref="FechamentoCaixaExtratoPdfData"/>,
/// e o logo já chega resolvido em bytes (busca anti-SSRF fica fora do renderer).</para>
/// </summary>
public interface IFechamentoCaixaExtratoRenderer
{
    /// <summary>Renderiza o extrato para um buffer PDF em memória. Stateless/threadsafe;
    /// determinístico para o mesmo input (datas estáveis no metadata).</summary>
    Task<byte[]> RenderAsync(FechamentoCaixaExtratoPdfData data, CancellationToken ct = default);
}

/// <summary>Dados do extrato de fechamento de caixa para o PDF timbrado. Camada 1 (MVP):
/// cabeçalho + extrato + totais. Seções futuras (espelho de pedidos, SKUs/estoque, balanço,
/// de-para) entram como coleções adicionais sem quebrar este contrato.</summary>
public sealed record FechamentoCaixaExtratoPdfData(
    string EmpresaNome,
    string? EmpresaDocumento,
    string? LojaNome,
    string? LojaEndereco,
    byte[]? LogoPng,
    DateOnly Data,
    decimal SaldoInicial,
    decimal TotalVendas,
    decimal TotalPagamentosPedidos,
    decimal TotalEntradasExtras,
    decimal TotalSaidasExtras,
    decimal SaldoFinal,
    string? FechadoPorNome,
    DateTime? FechadoEm,
    string? Observacoes,
    IReadOnlyList<FechamentoCaixaExtratoMovimento> Movimentos);

/// <summary>Linha do extrato (movimento de caixa do dia).</summary>
public sealed record FechamentoCaixaExtratoMovimento(
    DateTime DataMovimento,
    string Tipo,
    string? Descricao,
    string? Metodo,
    string? Categoria,
    decimal Valor,
    decimal SinalNoCaixa,
    bool Estornado);
