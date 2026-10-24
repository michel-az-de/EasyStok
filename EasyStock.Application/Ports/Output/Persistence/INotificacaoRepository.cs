namespace EasyStock.Application.Ports.Output.Persistence
{
    public interface INotificacaoRepository
    {
        Task<Notificacao?> GetByIdAsync(Guid id);
        Task<(IEnumerable<Notificacao> Items, int TotalCount)> GetByEmpresaAsync(
            Guid empresaId,
            bool? lida = null,
            TipoAlertaEstoque? tipo = null,
            SeveridadeNotificacao? severidade = null,
            int page = 1,
            int pageSize = 20,
            Guid? usuarioId = null);
        Task<IEnumerable<Notificacao>> GetRecentesNaoLidasAsync(Guid empresaId, int limit = 5, Guid? usuarioId = null);
        Task<NotificacaoResumo> GetResumoAsync(Guid empresaId, Guid? usuarioId = null);
        Task<bool> ExisteNotificacaoNaoLidaAsync(Guid empresaId, TipoAlertaEstoque tipo, Guid referenciaId);

        /// <summary>
        /// Os alertas AUTOMÁTICOS de estoque da empresa (sem destinatário e sem mensagem do outbox: os avisos de caixa,
        /// de convite etc. que reusam o enum ficam de fora) que ainda valem para a deduplicação: não lidos, de qualquer
        /// idade, ou criados a partir de <paramref name="desdeUtc"/>. Uma consulta por rodada, não uma por item (#2016).
        /// </summary>
        Task<IReadOnlyList<AlertaAutomatico>> GetAlertasAutomaticosAsync(
            Guid empresaId, IReadOnlyCollection<TipoAlertaEstoque> tipos, DateTime desdeUtc, CancellationToken ct = default);

        /// <summary>Apaga alertas automáticos da empresa por id (condição resolvida). Devolve quantos saíram.</summary>
        Task<int> RemoverAlertasAsync(Guid empresaId, IReadOnlyCollection<Guid> ids, CancellationToken ct = default);

        /// <summary>
        /// Retenção (#2016): apaga os alertas automáticos de estoque da empresa que já cumpriram o papel, só dos
        /// <paramref name="tipos"/> dados: lidos e criados antes de <paramref name="lidosAntesDeUtc"/>, ou de qualquer
        /// estado criados antes de <paramref name="qualquerAntesDeUtc"/>. Devolve quantos saíram.
        /// </summary>
        Task<int> ExpurgarAlertasAutomaticosAsync(
            Guid empresaId, IReadOnlyCollection<TipoAlertaEstoque> tipos, DateTime lidosAntesDeUtc, DateTime qualquerAntesDeUtc,
            CancellationToken ct = default);
        Task<int> CountNaoLidasAsync(Guid empresaId, Guid? usuarioId = null);
        Task AddAsync(Notificacao notificacao);
        Task UpdateAsync(Notificacao notificacao);
        Task MarcarTodasComoLidasAsync(Guid empresaId, Guid? usuarioId = null);
        Task DeleteAsync(Guid empresaId, Guid id);
    }

    /// <summary>Alerta automático já gravado, no mínimo que a deduplicação precisa.</summary>
    public sealed record AlertaAutomatico(Guid Id, TipoAlertaEstoque Tipo, Guid? ReferenciaId, bool Lida, DateTime CriadaEm);

    public record NotificacaoResumo
    {
        public int TotalNaoLidas { get; init; }
        public int Criticas { get; init; }
        public int Altas { get; init; }
        public int Medias { get; init; }
        public int Informativas { get; init; }
        public Dictionary<string, int> PorTipo { get; init; } = new();
    }
}
