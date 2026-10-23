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
    IOperacaoEventPublisher operacaoEventos,
    SaldoFinanceiroPedido? saldos = null)
{
    /// <summary>#1717-5: tipo do evento da trilha com o valor recebido a devolver ao cliente.</summary>
    public const string EventoDevolucaoAFazer = "devolucao_a_fazer";

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
            var saldo = saldos is null ? null : await saldos.ConsultarAsync(pedido, ct);
            EfeitosCancelamentoPedido.ExigirPermissao(
                pedido.TotalPago > 0 || cobrancas.Any(c => c.Status == StatusCobrancaPedido.Paga)
                    || saldo?.Alocado > 0, cmd.NivelSolicitante);
            if (pedido.StatusEnum == StatusPedido.Cancelado) return CriarPedidoUseCase.Map(pedido);

            statusAntigo = pedido.Status;
            // #1571: no pedido que veio do aparelho o status do ERP espelha o do aparelho, e um
            // aparelho atrasado pode tê-lo levado de volta a "aguardando" com a baixa já feita. Aí
            // devolve-se pelo que está baixado, não pelo status (a devolução só mexe em saída aberta).
            if (PedidoStateMachine.DescontaEstoque(pedido.StatusEnum) || !string.IsNullOrWhiteSpace(pedido.MobileOrderId))
                await estoqueIntegration.DevolverAsync(pedido);
            pedido.Cancelar();
            var agora = DateTime.UtcNow;
            // Cobrança paga conserva a evidência do recebimento. Cancelar a operação não
            // equivale a devolver dinheiro pelo banco/provedor.
            foreach (var cobranca in cobrancas.Where(c => c.Status == StatusCobrancaPedido.Pendente))
                cobranca.Cancelar(cmd.Motivo ?? "Pedido cancelado", agora);
            var ocorridoEm = AtualizarStatusPedido.AtualizarStatusPedidoUseCase.OcorridoEmAuditoria(cmd.OcorridoEm);
            await pedidoRepo.AddEventoAsync(new PedidoEvento
            {
                Id = Guid.NewGuid(), PedidoId = pedido.Id, Tipo = "cancelado",
                StatusAntigo = statusAntigo, StatusNovo = StatusPedidoMapper.Cancelado,
                Detalhes = cmd.Motivo, UsuarioId = cmd.UsuarioId, UsuarioNome = cmd.UsuarioNome,
                Origem = cmd.Origem,
                OcorridoEm = ocorridoEm
            });
            await RegistrarDevolucaoAFazerAsync(pedido, cmd, saldo, ocorridoEm);
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

    /// <summary>
    /// #1717-5: o dinheiro recebido e ainda não devolvido fica registrado na trilha como devolução a fazer. Não
    /// estorna nada: a devolução é registrada depois em Devoluções (manual ou estorno online).
    /// </summary>
    private async Task RegistrarDevolucaoAFazerAsync(Domain.Entities.Pedido pedido, CancelarPedidoCommand cmd,
        SaldoFinanceiroComanda? saldo, DateTime ocorridoEm)
    {
        var aDevolver = saldo?.DinheiroDisponivel ?? pedido.TotalPago;
        if (aDevolver <= 0m) return;
        await pedidoRepo.AddEventoAsync(EventoDevolucaoAFazerDe(pedido, aDevolver, cmd.UsuarioId, cmd.UsuarioNome, cmd.Origem, ocorridoEm));
    }

    /// <summary>Evento da trilha com o valor a devolver; também usado pela recusa do pedido do site (PEDIDO-02).</summary>
    public static PedidoEvento EventoDevolucaoAFazerDe(Domain.Entities.Pedido pedido, decimal aDevolver, Guid? usuarioId,
        string? usuarioNome, string? origem, DateTime ocorridoEm) => new()
    {
        Id = Guid.NewGuid(), PedidoId = pedido.Id, Tipo = EventoDevolucaoAFazer,
        Detalhes = $"Pedido cancelado com {aDevolver.ToString("C", Cultura.PtBr)} recebido. Devolva ao cliente e registre em Devoluções.",
        UsuarioId = usuarioId, UsuarioNome = usuarioNome, Origem = origem, OcorridoEm = ocorridoEm
    };
}
