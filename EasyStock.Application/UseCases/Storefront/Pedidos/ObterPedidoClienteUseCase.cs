using EasyStock.Application.Ports.Output.Persistence.Storefront;
using EasyStock.Domain.Exceptions.Storefront;

namespace EasyStock.Application.UseCases.Storefront.Pedidos;

/// <summary>
/// Obtém UM pedido storefront do cliente autenticado, no mesmo shape do
/// <see cref="ListarPedidosClienteUseCase"/>. Consumido pelo endpoint
/// <c>GET /api/storefront/{slug}/pedidos/{id}</c> (tela de acompanhamento — issue #670).
///
/// <para>
/// Reusa <see cref="ListarPedidosClienteUseCase.MapearParaDto"/> — mesma regra de
/// itens/frete/status/janela/avaliação/motivo. As mesmas limitações de MVP do list
/// valem aqui (Endereco e InitPointUrl null — ver TASK-EZ-PEDIDOS-002/003).
/// </para>
///
/// <para>
/// Posse é garantida no repositório (<c>EmpresaId + ClienteId</c>): se o pedido não
/// existe OU não é do cliente, devolve <c>null</c> e o controller responde 404 nos
/// dois casos (anti-enumeração — não revela existência de pedido alheio).
/// </para>
/// </summary>
public sealed class ObterPedidoClienteUseCase(
    IStorefrontRepository storefrontRepository,
    IPedidoStorefrontRepository pedidoRepository,
    IPedidoAvaliacaoRepository avaliacaoRepository,
    IVagaOcupadaRepository vagaOcupadaRepository,
    ILogger<ObterPedidoClienteUseCase> logger)
{
    public async Task<ObterPedidoClienteResult?> ExecuteAsync(
        ObterPedidoClienteInput input,
        CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(input);

        if (input.ClienteId == Guid.Empty)
            throw new ArgumentException("ClienteId obrigatório.", nameof(input));
        if (input.PedidoId == Guid.Empty)
            throw new ArgumentException("PedidoId obrigatório.", nameof(input));

        // ── Resolver storefront ───────────────────────────────────────────
        var storefront = await storefrontRepository.GetBySlugAsync(input.Slug, ct);
        if (storefront is null || !storefront.Ativo)
            throw new StorefrontNaoEncontradoException(input.Slug);

        // ── Carregar o pedido do cliente (+ itens), com filtro de posse ────
        var pedido = await pedidoRepository.ObterDoClienteAsync(
            storefront.EmpresaId, input.ClienteId, input.PedidoId, ct);

        if (pedido is null)
        {
            logger.LogInformation(
                "ObterPedidoCliente nao encontrado empresaId={EmpresaId} clienteId={ClienteId} pedidoId={PedidoId}",
                storefront.EmpresaId, input.ClienteId, input.PedidoId);
            return null;
        }

        // ── Lookups de avaliação + vaga/janela (reuso do mapper do list) ──
        var avaliacoes = await avaliacaoRepository.GetByPedidoIdsAsync(new[] { pedido.Id }, ct);
        var vagas = await vagaOcupadaRepository.GetByPedidoIdsAsync(new[] { pedido.Id }, ct);

        avaliacoes.TryGetValue(pedido.Id, out var avaliacao);
        vagas.TryGetValue(pedido.Id, out var vagaInfo);

        var dto = ListarPedidosClienteUseCase.MapearParaDto(pedido, avaliacao, vagaInfo);

        logger.LogInformation(
            "ObterPedidoCliente ok empresaId={EmpresaId} clienteId={ClienteId} pedidoId={PedidoId}",
            storefront.EmpresaId, input.ClienteId, pedido.Id);

        return new ObterPedidoClienteResult(dto);
    }
}
