using EasyStock.Application.Ports.Output.Persistence.Atendimento;
using EasyStock.Domain.Entities.Atendimento;
using EasyStock.Domain.Enums.Atendimento;

namespace EasyStock.Application.UseCases.Atendimento.Lembretes;

public sealed record LembreteResult(
    Guid Id, TipoLembrete Tipo, string Texto, DateTime VenceEm, SituacaoLembrete Situacao, Guid? ParaUsuarioId,
    Guid? ConversaId, Guid? PedidoId, DateTime CriadoEm, DateTime? VistoEm, DateTime? ConcluidoEm, Guid? ConcluidoPorUsuarioId)
{
    internal static LembreteResult De(Lembrete l) => new(
        l.Id, l.Tipo, l.Texto, l.VenceEm, l.Situacao, l.ParaUsuarioId, l.ConversaId, l.PedidoId, l.CriadoEm,
        l.VistoEm, l.ConcluidoEm, l.ConcluidoPorUsuarioId);
}

public sealed class LembreteNaoEncontradoException(Guid id) : Exception($"Lembrete {id} não encontrado.");

public sealed record CriarLembreteCommand(
    Guid EmpresaId, Guid UsuarioId, string Texto, DateTime? VenceEm, Guid? ParaUsuarioId, Guid? ConversaId, Guid? PedidoId);

/// <summary>S43: lembrete manual da dona. Sem horário, vence na hora e o avaliador avisa na próxima rodada.</summary>
public sealed class CriarLembreteUseCase(ILembreteRepository repository, IUnitOfWork unitOfWork, TimeProvider relogio)
{
    public async Task<LembreteResult> ExecuteAsync(CriarLembreteCommand command, CancellationToken ct = default)
    {
        Lembrete lembrete;
        try
        {
            lembrete = Lembrete.Manual(command.EmpresaId, command.Texto, command.VenceEm, command.UsuarioId,
                relogio.GetUtcNow().UtcDateTime, command.ParaUsuarioId, command.ConversaId, command.PedidoId);
        }
        catch (RegraDeDominioVioladaException ex)
        {
            throw new UseCaseValidationException(ex.Message);
        }

        await repository.AddAsync(lembrete, ct);
        await unitOfWork.CommitAsync();
        return LembreteResult.De(lembrete);
    }
}

/// <summary>
/// S43, sininho: os lembretes do usuário e os da equipe toda, abertos por padrão. <c>todos</c> traz
/// também os que são de outro atendente.
/// </summary>
public sealed class ListarLembretesUseCase(ILembreteRepository repository)
{
    public const int LimitePadrao = 100;

    public async Task<IReadOnlyList<LembreteResult>> ExecuteAsync(
        Guid empresaId, Guid usuarioId, bool todos, bool incluirConcluidos, CancellationToken ct = default) =>
        (await repository.ListarAsync(empresaId, todos ? null : usuarioId, incluirConcluidos, LimitePadrao, ct))
            .Select(LembreteResult.De).ToList();
}

/// <summary>S43: concluir tira o lembrete do sininho. Idempotente.</summary>
public sealed class ConcluirLembreteUseCase(ILembreteRepository repository, IUnitOfWork unitOfWork, TimeProvider relogio)
{
    public async Task<LembreteResult> ExecuteAsync(Guid empresaId, Guid usuarioId, Guid id, CancellationToken ct = default)
    {
        var lembrete = await repository.ObterAsync(empresaId, id, ct) ?? throw new LembreteNaoEncontradoException(id);
        lembrete.Concluir(relogio.GetUtcNow().UtcDateTime, usuarioId);
        await unitOfWork.CommitAsync();
        return LembreteResult.De(lembrete);
    }
}

/// <summary>S43: abrir o sininho marca como vistos os abertos que o usuário enxerga. Devolve quantos mudaram.</summary>
public sealed class MarcarLembretesVistosUseCase(ILembreteRepository repository, IUnitOfWork unitOfWork, TimeProvider relogio)
{
    public async Task<int> ExecuteAsync(Guid empresaId, Guid usuarioId, CancellationToken ct = default)
    {
        var agora = relogio.GetUtcNow().UtcDateTime;
        var naoVistos = (await repository.ListarAsync(empresaId, usuarioId, incluirConcluidos: false, int.MaxValue, ct))
            .Where(l => l.VistoEm is null && l.VenceEm <= agora)
            .ToList();
        if (naoVistos.Count == 0) return 0;

        foreach (var lembrete in naoVistos)
            lembrete.MarcarVisto(agora);
        await unitOfWork.CommitAsync();
        return naoVistos.Count;
    }
}
