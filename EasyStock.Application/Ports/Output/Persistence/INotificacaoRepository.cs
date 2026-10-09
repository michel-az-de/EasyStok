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
        Task<bool> ExisteNotificacaoDoDiaAsync(Guid empresaId, TipoAlertaEstoque tipo, Guid? referenciaId, DateTime dataReferencia);
        Task<int> CountNaoLidasAsync(Guid empresaId, Guid? usuarioId = null);
        Task AddAsync(Notificacao notificacao);
        Task UpdateAsync(Notificacao notificacao);
        Task MarcarTodasComoLidasAsync(Guid empresaId, Guid? usuarioId = null);
        Task DeleteAsync(Guid empresaId, Guid id);
    }

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
