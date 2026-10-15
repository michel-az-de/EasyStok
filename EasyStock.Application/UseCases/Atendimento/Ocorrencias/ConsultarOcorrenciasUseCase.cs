using EasyStock.Application.Ports.Output.Persistence.Atendimento;
using EasyStock.Domain.Enums.Atendimento;
using EasyStock.Application.UseCases.Pedidos.Cobranca;

namespace EasyStock.Application.UseCases.Atendimento.Ocorrencias;

/// <summary>Leitura das ocorrências para o console (S27).</summary>
public sealed class ConsultarOcorrenciasUseCase(IOcorrenciaRepository repo, IPedidoRepository pedidos, IEstornosOnlineService estornos)
{
    public const int LimitePadrao = 100;

    public async Task<IReadOnlyList<OcorrenciaDto>> ListarAsync(Guid empresaId, StatusOcorrencia? status, CancellationToken ct = default)
    {
        UseCaseGuards.EnsureEmpresaId(empresaId);
        return (await repo.ListarAsync(empresaId, status, LimitePadrao, ct)).Select(OcorrenciaDto.De).ToList();
    }

    public async Task<OcorrenciaDto?> ObterAsync(Guid empresaId, Guid id, CancellationToken ct = default)
    {
        UseCaseGuards.EnsureEmpresaId(empresaId);
        var o = await repo.ObterAsync(empresaId, id, ct);
        if (o is null || o.EmpresaId != empresaId) return null;
        var financeiro = await estornos.ConsultarAsync(empresaId, o.PedidoId, ct);
        return OcorrenciaDto.De(o).ComEstorno(financeiro.Estornos.FirstOrDefault(e => e.Id == o.Id));
    }

    public async Task<IReadOnlyList<OcorrenciaDto>?> DoPedidoAsync(Guid empresaId, Guid pedidoId, CancellationToken ct = default)
    {
        UseCaseGuards.EnsureEmpresaId(empresaId);
        UseCaseGuards.EnsureNotEmpty(pedidoId, "PedidoId");
        var pedido = await pedidos.GetByIdAsync(empresaId, pedidoId);
        if (pedido is null || pedido.EmpresaId != empresaId) return null;
        var ocorrencias = await repo.ListarDoPedidoAsync(empresaId, pedidoId, ct);
        if (ocorrencias.Count == 0) return [];
        var financeiro = await estornos.ConsultarAsync(empresaId, pedidoId, ct);
        return ocorrencias.Select(o => OcorrenciaDto.De(o).ComEstorno(financeiro.Estornos.FirstOrDefault(e => e.Id == o.Id))).ToList();
    }
}
