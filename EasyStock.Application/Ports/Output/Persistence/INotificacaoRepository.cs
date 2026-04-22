using EasyStock.Domain.Entities;
using EasyStock.Domain.Enums;

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
            int pageSize = 20);
        Task<IEnumerable<Notificacao>> GetRecentesNaoLidasAsync(Guid empresaId, int limit = 5);
        Task<NotificacaoResumo> GetResumoAsync(Guid empresaId);
        Task<bool> ExisteNotificacaoNaoLidaAsync(Guid empresaId, TipoAlertaEstoque tipo, Guid referenciaId);
        Task<bool> ExisteNotificacaoDoDiaAsync(Guid empresaId, TipoAlertaEstoque tipo, Guid? referenciaId, DateTime dataReferencia);
        Task<int> CountNaoLidasAsync(Guid empresaId);
        Task AddAsync(Notificacao notificacao);
        Task UpdateAsync(Notificacao notificacao);
        Task MarcarTodasComoLidasAsync(Guid empresaId);
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
