using EasyStock.Application.Events.Pedidos;
using EasyStock.Application.Ports.Output.Atendimento;
using EasyStock.Application.Ports.Output.Integration;
using EasyStock.Application.Ports.Output.Persistence.Pagamentos;
using EasyStock.Application.Services;
using EasyStock.Application.Services.Pedidos;
using EasyStock.Application.UseCases.CriarPedido;
using EasyStock.Application.UseCases.Pedidos;
using EasyStock.Domain.Enums.Pagamentos;
using EasyStock.Domain.Sales;

namespace EasyStock.Application.UseCases.CancelarPedido;

public sealed record CancelarPedidoCommand(
    [property: Required] Guid EmpresaId,
    [property: Required] Guid Id,
    Guid? UsuarioId = null,
    [property: MaxLength(120)] string? UsuarioNome = null,
    [property: MaxLength(500)] string? Motivo = null,
    [property: MaxLength(20)] string? Origem = "web",
    NivelAcesso NivelSolicitante = NivelAcesso.Operador,
    DateTime? OcorridoEm = null);

public class CancelarPedidoUseCase(
    IPedidoRepository pedidoRepo,
    PedidoEstoqueIntegrationService estoqueIntegration,
    EfeitosCancelamentoPedido efeitosCancelamento,
    IUnitOfWork uow,
    ILogger<CancelarPedidoUseCase> logger,
    ICobrancaPedidoRepository cobrancaRepo,
    IPublicadorEventoIntegracao publicadorEventos,
    IOperacaoEventPublisher operacaoEventos)
{
    public async Task<PedidoResult?> ExecuteAsync(CancelarPedidoCommand cmd, bool publicarOperacao = true)
    {
        UseCaseGuards.EnsureEmpresaId(cmd.EmpresaId);
        UseCaseGuards.EnsureNotEmpty(cmd.Id, "Id");
        if (cmd.Motivo?.Length > 500)
            throw new UseCaseValidationException("O motivo deve ter até 500 caracteres.");

        string? statusAntigo = null;
        var resultado = await uow.ExecuteInTransactionSemRetryAsync(async ct =>
        {
            statusAntigo = null;
            // O recebimento usa o mesmo lock. A autorização precisa enxergar o pagamento
            // que acabou de ser confirmado, antes de decidir se o operador pode cancelar.
            await pedidoRepo.TravarAsync(cmd.EmpresaId, cmd.Id, ct);
            var pedido = await pedidoRepo.GetByIdWithDetailsAsync(cmd.EmpresaId, cmd.Id);
            if (pedido is null || pedido.EmpresaId != cmd.EmpresaId) return null;
            var cobrancas = await cobrancaRepo.ListarDoPedidoAsync(cmd.EmpresaId, cmd.Id, ct);
            if (cobrancas.Any(c => c.EmpresaId != cmd.EmpresaId || c.PedidoId != cmd.Id))
                throw new UseCaseValidationException("Cobrança não pertence ao pedido.");
            EfeitosCancelamentoPedido.ExigirPermissao(
                pedido.TotalPago > 0 || cobrancas.Any(c => c.Status == StatusCobrancaPedido.Paga), cmd.NivelSolicitante);
            if (pedido.StatusEnum == StatusPedido.Cancelado) return CriarPedidoUseCase.Map(pedido);

            statusAntigo = pedido.Status;
            if (PedidoStateMachine.DescontaEstoque(pedido.StatusEnum))
                await estoqueIntegration.DevolverAsync(pedido);
            pedido.Cancelar();
            var agora = DateTime.UtcNow;
            // Cobrança paga conserva a evidência do recebimento. Cancelar a operação não
            // equivale a devolver dinheiro pelo banco/provedor.
            foreach (var cobranca in cobrancas.Where(c => c.Status == StatusCobrancaPedido.Pendente))
                cobranca.Cancelar(cmd.Motivo ?? "Pedido cancelado", agora);
            await pedidoRepo.AddEventoAsync(new PedidoEvento
            {
                Id = Guid.NewGuid(), PedidoId = pedido.Id, Tipo = "cancelado",
                StatusAntigo = statusAntigo, StatusNovo = StatusPedidoMapper.Cancelado,
                Detalhes = cmd.Motivo, UsuarioId = cmd.UsuarioId, UsuarioNome = cmd.UsuarioNome,
                Origem = cmd.Origem,
                OcorridoEm = AtualizarStatusPedido.AtualizarStatusPedidoUseCase.OcorridoEmAuditoria(cmd.OcorridoEm)
            });
            await efeitosCancelamento.AplicarAsync(pedido, cmd.Motivo, cmd.UsuarioId, ct);
            await publicadorEventos.PublicarAsync(pedido.EmpresaId, "pedido.mudou_status", "pedido", pedido.Id,
                new PedidoMudouStatusEvent(pedido.Id, pedido.EmpresaId, pedido.LojaId, statusAntigo,
                    pedido.Status, cmd.Origem, cmd.UsuarioId, cmd.UsuarioNome, agora),
                correlationId: pedido.Id.ToString(), ct: ct);
            await pedidoRepo.UpdateAsync(pedido);
            await uow.CommitAsync();
            return CriarPedidoUseCase.Map(pedido);
        });

        if (statusAntigo is not null && resultado is not null)
        {
            if (publicarOperacao)
                await operacaoEventos.PublicarAsync(EventosOperacao.PedidoMudouStatus, cmd.EmpresaId,
                    new PedidoMudouStatusOperacao(cmd.Id, statusAntigo, StatusPedidoMapper.Cancelado));
            logger.LogInformation("Pedido {Id} cancelado por {UsuarioId}.", cmd.Id, cmd.UsuarioId);
        }
        return resultado;
    }
}
