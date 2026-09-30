using EasyStock.Application.Ports.Output.Persistence.Atendimento;
using EasyStock.Domain.Entities.Atendimento;
using EasyStock.Domain.Enums.Atendimento;

namespace EasyStock.Application.UseCases.Atendimento.Entregas;

public sealed record ChamadoEntregadorResult(
    Guid Id, Guid? ViagemId, string Texto, SituacaoChamadoEntregador Situacao, DateTime AbertoEm, DateTime? AtendidoEm, DateTime? CanceladoEm)
{
    internal static ChamadoEntregadorResult De(ChamadoEntregador c) =>
        new(c.Id, c.ViagemId, c.Texto, c.Situacao, c.AbertoEm, c.AtendidoEm, c.CanceladoEm);
}

public sealed class ChamadoEntregadorNaoEncontradoException(Guid id) : Exception($"Chamado {id} não encontrado.");

/// <summary>S44 (<c>CHAMAR_ENTREGADOR</c>): chamado em texto livre, opcionalmente ligado a uma viagem.</summary>
public sealed class AbrirChamadoEntregadorUseCase(IChamadoEntregadorRepository repository, IUnitOfWork unitOfWork, TimeProvider relogio)
{
    public async Task<ChamadoEntregadorResult> ExecuteAsync(Guid empresaId, string texto, Guid? viagemId, CancellationToken ct = default)
    {
        var chamado = Regra.Validar(() => ChamadoEntregador.Abrir(empresaId, texto, viagemId, relogio.GetUtcNow().UtcDateTime));
        await repository.AddAsync(chamado, ct);
        await unitOfWork.CommitAsync();
        return ChamadoEntregadorResult.De(chamado);
    }
}

/// <summary>S44: atender ou cancelar o chamado. Idempotente.</summary>
public sealed class ResolverChamadoEntregadorUseCase(IChamadoEntregadorRepository repository, IUnitOfWork unitOfWork, TimeProvider relogio)
{
    public async Task<ChamadoEntregadorResult> ExecuteAsync(Guid empresaId, Guid id, bool atendido, CancellationToken ct = default)
    {
        var chamado = await repository.ObterAsync(empresaId, id, ct) ?? throw new ChamadoEntregadorNaoEncontradoException(id);
        var agora = relogio.GetUtcNow().UtcDateTime;
        if (atendido) chamado.Atender(agora); else chamado.Cancelar(agora);
        await unitOfWork.CommitAsync();
        return ChamadoEntregadorResult.De(chamado);
    }
}

public sealed class ListarChamadosEntregadorUseCase(IChamadoEntregadorRepository repository)
{
    public const int LimitePadrao = 100;

    public async Task<IReadOnlyList<ChamadoEntregadorResult>> ExecuteAsync(Guid empresaId, bool apenasAbertos, CancellationToken ct = default) =>
        (await repository.ListarAsync(empresaId, apenasAbertos, LimitePadrao, ct)).Select(ChamadoEntregadorResult.De).ToList();
}
