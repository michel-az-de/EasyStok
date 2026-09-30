using EasyStock.Application.Ports.Output.Persistence.Storefront;
using EasyStock.Domain.Entities.Storefront;
using EasyStock.Domain.Enums.Storefront;
using EasyStock.Domain.Exceptions.Storefront;
using EasyStock.Domain.Sales;

namespace EasyStock.Application.UseCases.Storefront.Avaliacao;

/// <summary>
/// Avaliação de um toque (S26, US-046, RN-34): positiva ou negativa, pelo botão do WhatsApp
/// (<c>acao:avaliacao:...</c>) ou pela ferramenta <c>registrar_avaliacao</c> do agente. Uma por pedido
/// (UNIQUE PedidoId): a segunda lança <see cref="AvaliacaoDuplicadaException"/>. Não faz commit.
/// </summary>
public sealed class RegistrarAvaliacaoSimplesUseCase(
    IPedidoStorefrontRepository pedidos,
    IPedidoAvaliacaoRepository avaliacoes)
{
    /// <summary>O pedido de avaliação sai 30 min após a entrega (S26).</summary>
    public static readonly TimeSpan AtrasoSolicitacao = TimeSpan.FromMinutes(30);

    public async Task<PedidoAvaliacao> ExecutarAsync(
        Guid empresaId,
        Guid clienteId,
        Guid pedidoId,
        ResultadoAvaliacao resultado,
        string? comentario,
        DateTime agoraUtc,
        CancellationToken ct = default)
    {
        var pedido = await pedidos.GetByIdAsync(pedidoId, ct);
        // Pedido de outra empresa ou de outro cliente responde como inexistente: o id vem do cliente.
        if (pedido is null || pedido.EmpresaId != empresaId || pedido.ClienteId != clienteId)
            throw new StorefrontNaoEncontradoException(pedidoId.ToString());

        if (pedido.Status != StatusPedidoMapper.Entregue)
            throw new PedidoNaoElegivelParaAvaliacaoException($"Pedido não está entregue (status={pedido.Status}).");

        var existente = await avaliacoes.GetByPedidoAsync(pedidoId, ct);
        if (existente is not null)
            throw new AvaliacaoDuplicadaException(existente.Id);

        var entregue = pedido.EntreguEm ?? agoraUtc;
        var solicitadoEm = pedido.AvaliacaoSolicitadaEm ?? entregue.Add(AtrasoSolicitacao);
        var avaliacao = PedidoAvaliacao.CriarSimples(pedidoId, clienteId, empresaId, resultado, comentario, solicitadoEm);

        await avaliacoes.AddAsync(avaliacao, ct);
        return avaliacao;
    }

    /// <summary>"positiva" | "negativa" (sem caixa); qualquer outro valor é inválido.</summary>
    public static bool TryInterpretarResultado(string? texto, out ResultadoAvaliacao resultado)
    {
        resultado = default;
        switch (texto?.Trim().ToLowerInvariant())
        {
            case "positiva": resultado = ResultadoAvaliacao.Positiva; return true;
            case "negativa": resultado = ResultadoAvaliacao.Negativa; return true;
            default: return false;
        }
    }
}
