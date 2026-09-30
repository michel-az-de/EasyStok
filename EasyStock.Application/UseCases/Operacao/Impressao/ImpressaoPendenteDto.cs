using EasyStock.Domain.Entities.Operacao;

namespace EasyStock.Application.UseCases.Operacao.Impressao;

/// <summary>
/// Item da fila como o consumidor vê (S20). O conteúdo vem de <c>GET api/pedidos/{PedidoId}/canhoto</c>
/// no formato que o dispositivo imprime (<c>texto</c> para ESC/POS, <c>html</c> para o navegador).
/// </summary>
/// <param name="Numero">Número curto do pedido (8 primeiros caracteres do id, maiúsculos).</param>
/// <param name="Tipo"><c>canhoto</c>.</param>
/// <param name="Status"><c>pendente</c>, <c>impressa</c> ou <c>falhou</c>.</param>
public sealed record ImpressaoPendenteDto(
    Guid Id,
    Guid PedidoId,
    string Numero,
    string Tipo,
    string Status,
    DateTime CriadaEm,
    DateTime? ImpressaEm,
    int Tentativas,
    string? Erro)
{
    public static ImpressaoPendenteDto De(ImpressaoPendente i) => new(
        i.Id,
        i.PedidoId,
        i.PedidoId.ToString("N")[..8].ToUpperInvariant(),
        i.Tipo.ToString().ToLowerInvariant(),
        i.Status.ToString().ToLowerInvariant(),
        i.CriadaEm,
        i.ImpressaEm,
        i.Tentativas,
        i.Erro);
}
