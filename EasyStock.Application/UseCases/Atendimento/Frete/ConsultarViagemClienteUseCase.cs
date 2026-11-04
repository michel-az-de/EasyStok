using EasyStock.Application.Ports.Output.Persistence.Atendimento;
using EasyStock.Application.UseCases.Atendimento.Entregas;
using EasyStock.Domain.Enums.Atendimento;

namespace EasyStock.Application.UseCases.Atendimento.Frete;

public sealed record ViagemClienteResult(Guid PedidoId, ViagemResult? Viagem);

public sealed class ConsultarViagemClienteUseCase(
    IClienteRepository clientes, IPedidoRepository pedidos, IViagemRepository viagens, ObterViagemUseCase obter)
{
    public async Task<ViagemClienteResult?> ExecuteAsync(Guid empresaId, Guid clienteId, Guid pedidoId, CancellationToken ct = default)
    {
        UseCaseGuards.EnsureEmpresaId(empresaId);
        UseCaseGuards.EnsureNotEmpty(clienteId, "ClienteId");
        UseCaseGuards.EnsureNotEmpty(pedidoId, "PedidoId");
        var cliente = await clientes.GetByIdAsync(empresaId, clienteId);
        if (cliente is null || cliente.EmpresaId != empresaId) return null;
        var pedido = await pedidos.GetByIdAsync(empresaId, pedidoId);
        if (pedido is null || pedido.EmpresaId != empresaId || pedido.ClienteId != clienteId) return null;
        var viagem = await viagens.ObterAtivaDoPedidoAsync(empresaId, pedidoId, ct);
        if (viagem is null || viagem.Situacao is not (SituacaoViagem.Montando or SituacaoViagem.EmRota))
            return new(pedidoId, null);
        if (viagem.EmpresaId != empresaId || !viagem.Paradas.Any(p => p.PedidoId == pedidoId)) return null;
        return new(pedidoId, await obter.ExecuteAsync(empresaId, viagem.Id, ct));
    }
}
