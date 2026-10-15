using EasyStock.Application.Ports.Output.Atendimento;
using EasyStock.Application.Ports.Output.Persistence.Operacao;
using EasyStock.Domain.Entities.Operacao;

namespace EasyStock.Application.UseCases.Operacao.Impressao;

public sealed record ReimprimirCanhotoInput(Guid EmpresaId, Guid PedidoId);

/// <summary>
/// Põe o canhoto do pedido de novo na fila (S20): papel perdido, impressão falhou. Cria outro item (o
/// histórico fica) e publica <c>impressao.pendente</c> depois do commit. Devolve <c>null</c> para pedido
/// inexistente ou de outra empresa.
/// </summary>
public sealed class ReimprimirCanhotoUseCase(
    IPedidoRepository pedidos,
    IImpressaoPendenteRepository repo,
    IOperacaoEventPublisher operacaoEventos,
    IUnitOfWork unitOfWork,
    TimeProvider relogio)
{
    public async Task<ImpressaoPendenteDto?> ExecuteAsync(ReimprimirCanhotoInput input, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(input);
        UseCaseGuards.EnsureEmpresaId(input.EmpresaId);
        UseCaseGuards.EnsureNotEmpty(input.PedidoId, "PedidoId");

        var pedido = await pedidos.GetByIdAsync(input.EmpresaId, input.PedidoId);
        if (pedido is null || pedido.EmpresaId != input.EmpresaId) return null;

        var impressao = ImpressaoPendente.CriarCanhoto(input.EmpresaId, pedido.LojaId, pedido.Id, relogio.GetUtcNow().UtcDateTime);
        await repo.AddAsync(impressao, ct);
        await unitOfWork.CommitAsync();

        await operacaoEventos.PublicarAsync(EventosOperacao.ImpressaoPendente, input.EmpresaId,
            new ImpressaoPendenteOperacao(impressao.Id, pedido.Id), ct);
        return ImpressaoPendenteDto.De(impressao);
    }
}
