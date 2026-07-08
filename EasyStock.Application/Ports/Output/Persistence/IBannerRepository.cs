using EasyStock.Domain.Entities.Banners;

namespace EasyStock.Application.Ports.Output.Persistence
{
    /// <summary>
    /// Persistência de banners de broadcast global (#869). Tabela sem <c>EmpresaId</c>
    /// (isenta de RLS). Não faz commit — o UnitOfWork da camada Application persiste.
    /// </summary>
    public interface IBannerRepository
    {
        Task<Banner?> ObterAsync(Guid id, CancellationToken ct = default);

        /// <summary>Listagem paginada do console Admin (todos os banners, filtro opcional por ativo).</summary>
        Task<(IReadOnlyList<Banner> Itens, int Total)> ListarAdminAsync(
            bool? ativo, int page, int pageSize, CancellationToken ct = default);

        /// <summary>
        /// Banners ativos, dentro da janela e ainda não confirmados/vistos por este usuário
        /// (query §1.3): obrigatório só some com Confirmado; não-obrigatório some com qualquer interação.
        /// </summary>
        Task<IReadOnlyList<Banner>> ListarAtivosNaoConfirmadosAsync(
            Guid usuarioId, DateTime agoraUtc, CancellationToken ct = default);

        /// <summary>
        /// Banners marcados para notificar, ativos e ainda não notificados. O worker os
        /// enfileira (fan-out por empresa) e marca via <see cref="MarcarNotificadoSeIneditoAsync"/>.
        /// </summary>
        Task<IReadOnlyList<Banner>> ListarPendentesDeNotificacaoAsync(CancellationToken ct = default);

        Task InserirAsync(Banner banner, CancellationToken ct = default);
        Task AtualizarAsync(Banner banner, CancellationToken ct = default);
        Task RemoverAsync(Banner banner, CancellationToken ct = default);

        /// <summary>
        /// Guard atômico de idempotência da notificação (ADR-0030): marca <c>NotificadoEm</c>
        /// apenas se ainda estava nulo, num único UPDATE condicional. Retorna true se marcou
        /// (rowcount=1) — só então o chamador enfileira o evento. Mata duplo-clique/corrida.
        /// </summary>
        Task<bool> MarcarNotificadoSeIneditoAsync(Guid bannerId, DateTime agoraUtc, CancellationToken ct = default);
    }
}
