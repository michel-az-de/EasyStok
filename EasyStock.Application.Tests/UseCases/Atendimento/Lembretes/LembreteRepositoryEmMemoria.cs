using EasyStock.Application.Ports.Output.Persistence.Atendimento;
using EasyStock.Domain.Entities.Atendimento;
using EasyStock.Domain.Enums.Atendimento;

namespace EasyStock.Application.Tests.UseCases.Atendimento.Lembretes;

/// <summary>Repositório de lembretes (S43) em memória, compartilhado pelo avaliador e pelo vigia da F16.</summary>
internal sealed class LembreteRepositoryEmMemoria : ILembreteRepository
{
    private readonly List<Lembrete> _itens = [];
    public IReadOnlyList<Lembrete> Todos => _itens;

    public Task AddAsync(Lembrete lembrete, CancellationToken ct = default)
    {
        _itens.Add(lembrete);
        return Task.CompletedTask;
    }

    public Task<Lembrete?> ObterAsync(Guid empresaId, Guid id, CancellationToken ct = default) =>
        Task.FromResult(_itens.FirstOrDefault(l => l.EmpresaId == empresaId && l.Id == id));

    public Task<IReadOnlyList<Lembrete>> ListarAsync(
        Guid empresaId, Guid? usuarioId, bool incluirConcluidos, int limite, CancellationToken ct = default) =>
        Task.FromResult<IReadOnlyList<Lembrete>>(_itens
            .Where(l => l.EmpresaId == empresaId && (incluirConcluidos || l.EstaAberto)
                && (usuarioId is null || l.ParaUsuarioId is null || l.ParaUsuarioId == usuarioId))
            .Take(limite).ToList());

    public Task<bool> ExisteAutomaticoAsync(Guid empresaId, TipoLembrete tipo, string referencia, CancellationToken ct = default) =>
        Task.FromResult(_itens.Any(l => l.EmpresaId == empresaId && l.Tipo == tipo && l.Referencia == referencia));

    public Task<IReadOnlyList<Lembrete>> ListarAutomaticosAbertosAsync(CancellationToken ct = default) =>
        Task.FromResult<IReadOnlyList<Lembrete>>(_itens.Where(l => l.Tipo != TipoLembrete.Manual && l.EstaAberto).ToList());

    public Task<IReadOnlyList<Lembrete>> ListarVencidosSemAvisoAsync(DateTime agoraUtc, int limite, CancellationToken ct = default) =>
        Task.FromResult<IReadOnlyList<Lembrete>>(_itens.Where(l => l.DeveAvisar(agoraUtc)).Take(limite).ToList());
}
