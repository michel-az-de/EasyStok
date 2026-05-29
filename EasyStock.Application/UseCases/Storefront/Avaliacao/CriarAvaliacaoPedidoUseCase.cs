using EasyStock.Application.Ports.Output.Persistence.Storefront;
using EasyStock.Domain.Entities.Storefront;
using EasyStock.Domain.Exceptions.Storefront;
using EasyStock.Domain.Sales;

namespace EasyStock.Application.UseCases.Storefront.Avaliacao;

/// <summary>
/// Cria uma <see cref="PedidoAvaliacao"/> após validar cookie, status do pedido e unicidade.
/// </summary>
public sealed class CriarAvaliacaoPedidoUseCase(
    IPedidoStorefrontRepository pedidoRepo,
    IPedidoAvaliacaoRepository avaliacaoRepo,
    AvaliacaoCookieStore cookieStore,
    ComentarioSanitizer sanitizer,
    TimeProvider timeProvider,
    ILogger<CriarAvaliacaoPedidoUseCase> logger)
{
    private static readonly TimeSpan JanelaMinima = TimeSpan.FromHours(24);

    public async Task<AvaliacaoCriadaDto> ExecuteAsync(
        CriarAvaliacaoPedidoInput input,
        CancellationToken ct = default)
    {
        // ── 1. Validar cookie ────────────────────────────────────────────
        if (!await cookieStore.EhValidoAsync(input.PedidoId, input.CookieValue))
            throw new AvaliacaoCookieAusenteException();

        // ── 2. Buscar pedido ─────────────────────────────────────────────
        var pedido = await pedidoRepo.GetByIdAsync(input.PedidoId, ct);
        if (pedido is null)
            throw new StorefrontNaoEncontradoException(input.PedidoId.ToString());

        // ── 3. Validar elegibilidade ─────────────────────────────────────
        if (pedido.Status != StatusPedidoMapper.Entregue)
            throw new PedidoNaoElegivelParaAvaliacaoException(
                $"Pedido não está entregue (status={pedido.Status}).");

        var agora = timeProvider.GetUtcNow().UtcDateTime;
        var entregue = pedido.EntreguEm ?? pedido.AlteradoEm;
        if ((agora - entregue) < JanelaMinima)
            throw new PedidoNaoElegivelParaAvaliacaoException(
                "O pedido precisa ter sido entregue há pelo menos 24 horas para ser avaliado.");

        // ── 4. Verificar duplicidade ─────────────────────────────────────
        var existing = await avaliacaoRepo.GetByPedidoAsync(input.PedidoId, ct);
        if (existing is not null)
            throw new AvaliacaoDuplicadaException(existing.Id);

        // ── 5. Validar ClienteId ─────────────────────────────────────────
        var clienteId = pedido.ClienteId ?? Guid.Empty;
        if (clienteId == Guid.Empty)
        {
            logger.LogWarning(
                "Avaliacao solicitada para pedidoId={PedidoId} sem ClienteId.", input.PedidoId);
            throw new StorefrontNaoEncontradoException(input.Slug);
        }

        // ── 6. Sanitizar comentário ──────────────────────────────────────
        var comentarioSanitizado = string.IsNullOrWhiteSpace(input.Comentario)
            ? null
            : sanitizer.Sanitizar(input.Comentario);
        if (string.IsNullOrWhiteSpace(comentarioSanitizado)) comentarioSanitizado = null;

        // ── 7. Criar entity + persistir ──────────────────────────────────
        var solicitadoEm = pedido.AvaliacaoSolicitadaEm ?? entregue.AddHours(24);
        var avaliacao = PedidoAvaliacao.Criar(
            pedidoId: input.PedidoId,
            clienteId: clienteId,
            empresaId: pedido.EmpresaId,
            estrelas: input.Nota,
            comentario: comentarioSanitizado,
            recomendariaParaAmigos: input.RecomendariaParaAmigos,
            fotoUrl: input.FotoUrl,
            solicitadoEm: solicitadoEm);

        await avaliacaoRepo.AddAsync(avaliacao, ct);

        logger.LogInformation(
            "PedidoAvaliacao criada. avaliacaoId={AvaliacaoId} pedidoId={PedidoId} nota={Nota}",
            avaliacao.Id, input.PedidoId, input.Nota);

        return new AvaliacaoCriadaDto(
            Id: avaliacao.Id,
            CriadaEm: avaliacao.RespondidoEm ?? agora,
            Nota: avaliacao.Estrelas,
            Comentario: avaliacao.Comentario);
    }
}

public sealed record CriarAvaliacaoPedidoInput(
    string Slug,
    Guid PedidoId,
    int Nota,
    string? Comentario,
    bool RecomendariaParaAmigos,
    string? FotoUrl,
    string CookieValue);

public sealed record AvaliacaoCriadaDto(
    Guid Id,
    DateTime? CriadaEm,
    int Nota,
    string? Comentario);
