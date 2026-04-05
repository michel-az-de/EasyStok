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
            int page = 1,
            int pageSize = 20);
        Task<bool> ExisteNotificacaoNaoLidaAsync(Guid empresaId, TipoAlertaEstoque tipo, Guid referenciaId);
        Task<bool> ExisteNotificacaoDoDiaAsync(Guid empresaId, TipoAlertaEstoque tipo, Guid? referenciaId, DateTime dataReferencia);
        Task<int> CountNaoLidasAsync(Guid empresaId);
        Task AddAsync(Notificacao notificacao);
        Task UpdateAsync(Notificacao notificacao);
        Task MarcarTodasComoLidasAsync(Guid empresaId);
        Task DeleteAsync(Guid id);
    }
}
