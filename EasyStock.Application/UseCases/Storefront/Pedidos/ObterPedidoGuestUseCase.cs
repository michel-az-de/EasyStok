using EasyStock.Application.Ports.Output.Persistence.Storefront;
using EasyStock.Domain.Exceptions.Storefront;

namespace EasyStock.Application.UseCases.Storefront.Pedidos;

/// <summary>
/// Obtem UM pedido GUEST (issue #684) — caminho sem login, autenticado por
/// token assinado HMAC entregue no checkout guest (#680).
///
/// <para>
/// Diferenca vs <see cref="ObterPedidoClienteUseCase"/>: posse e provada pelo
/// TOKEN (controller valida antes de chamar este use case), nao por
/// <c>ClienteId</c> de cookie de sessao. Use case so confirma que o pedido
/// existe + pertence ao storefront da rota + e mesmo um pedido guest.
/// </para>
///
/// <para>
/// Anti-enumeracao: pedido nao guest (logado) retorna null mesmo se o ID
/// existir — token de guest nao destrava pedido logado. Se Felipe quiser
/// expandir o link guest pra pedidos logados depois, mudar a regra aqui.
/// </para>
/// </summary>
public sealed class ObterPedidoGuestUseCase(
    IStorefrontRepository storefrontRepository,
    IPedidoStorefrontRepository pedidoRepository,
    IPedidoAvaliacaoRepository avaliacaoRepository,
    IVagaOcupadaRepository vagaOcupadaRepository,
    ILogger<ObterPedidoGuestUseCase> logger)
{
    private const string OrigemGuest = "storefront-guest";

    public async Task<ObterPedidoClienteResult?> ExecuteAsync(
        string slug,
        Guid pedidoId,
        CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(slug))
            throw new ArgumentException("Slug obrigatorio.", nameof(slug));
        if (pedidoId == Guid.Empty)
            throw new ArgumentException("PedidoId obrigatorio.", nameof(pedidoId));

        var storefront = await storefrontRepository.GetBySlugAsync(slug, ct);
        if (storefront is null || !storefront.Ativo)
            throw new StorefrontNaoEncontradoException(slug);

        var pedido = await pedidoRepository.GetByIdComItensAsync(pedidoId, ct);
        if (pedido is null
            || pedido.EmpresaId != storefront.EmpresaId
            || pedido.Origem != OrigemGuest)
        {
            logger.LogInformation(
                "ObterPedidoGuest nao encontrado/nao-guest empresaId={EmpresaId} pedidoId={PedidoId}",
                storefront.EmpresaId, pedidoId);
            return null;
        }

        var avaliacoes = await avaliacaoRepository.GetByPedidoIdsAsync(new[] { pedido.Id }, ct);
        var vagas = await vagaOcupadaRepository.GetByPedidoIdsAsync(new[] { pedido.Id }, ct);
        avaliacoes.TryGetValue(pedido.Id, out var avaliacao);
        vagas.TryGetValue(pedido.Id, out var vagaInfo);

        var dto = ListarPedidosClienteUseCase.MapearParaDto(pedido, avaliacao, vagaInfo);

        logger.LogInformation(
            "ObterPedidoGuest ok empresaId={EmpresaId} pedidoId={PedidoId}",
            storefront.EmpresaId, pedido.Id);

        return new ObterPedidoClienteResult(dto);
    }
}
