using EasyStock.Application.Ports.Output.Persistence.Pagamentos;
using EasyStock.Application.Services.Pedidos;
using EasyStock.Application.UseCases.Caixa;
using EasyStock.Application.UseCases.CriarPedido;
using EasyStock.Application.UseCases.Pedidos;
using EasyStock.Application.UseCases.Pedidos.Cobranca;
using EasyStock.Domain.Sales;

namespace EasyStock.Application.UseCases.RemoverPagamentoPedido;

public sealed record RemoverPagamentoPedidoCommand(
    Guid EmpresaId,
    Guid PedidoId,
    Guid PagamentoId,
    Guid? UsuarioId = null,
    string? UsuarioNome = null,
    string? Origem = "web",
    NivelAcesso NivelSolicitante = NivelAcesso.Operador);

public class RemoverPagamentoPedidoUseCase(
    IPedidoRepository repo,
    ICobrancaPedidoRepository cobrancas,
    IUnitOfWork uow,
    ILogger<RemoverPagamentoPedidoUseCase> logger,
    QuitacaoPedido quitacao,
    ICaixaRepository caixa)
{
    public async Task<PedidoResult?> ExecuteAsync(RemoverPagamentoPedidoCommand cmd)
    {
        UseCaseGuards.EnsureEmpresaId(cmd.EmpresaId);
        UseCaseGuards.EnsureNotEmpty(cmd.PedidoId, "PedidoId");
        UseCaseGuards.EnsureNotEmpty(cmd.PagamentoId, "PagamentoId");

        return await uow.ExecuteInTransactionSemRetryAsync(async token =>
        {
            await repo.TravarAsync(cmd.EmpresaId, cmd.PedidoId, token);
            var pedido = await repo.GetByIdWithDetailsAsync(cmd.EmpresaId, cmd.PedidoId);
            if (pedido == null || pedido.EmpresaId != cmd.EmpresaId) return null;

            var pag = pedido.Pagamentos.FirstOrDefault(p => p.Id == cmd.PagamentoId);
            if (pag == null) return CriarPedidoUseCase.Map(pedido);

            // Recebimento do Mercado Pago apagado deixava a cobrança paga sem pagamento: nem estorno nem ocorrência fechavam.
            if (ConsultarEstornosManuaisUseCase.TemCobrancaOnline(pag, await cobrancas.ListarDoPedidoAsync(cmd.EmpresaId, pedido.Id, token)))
                throw new UseCaseValidationException("Recebimento do Mercado Pago não se remove: use o estorno.");

            // Antes do preparo é o "marquei por engano" do atendimento. Depois, tirar dinheiro do pedido
            // segue a regra do cancelamento com pagamento: só a gestão.
            if (!PedidoStateMachine.PodeDesfazerPagamento(pedido.StatusEnum)
                && cmd.NivelSolicitante is not (NivelAcesso.Gerente or NivelAcesso.Admin or NivelAcesso.SuperAdmin))
                throw new UnauthorizedAccessException("Só a dona ou um gerente pode remover pagamento depois que o preparo começou.");

            if (await repo.TemEstornoOnlineAsync(cmd.EmpresaId, pag.Id, token)
                || (await repo.ListarEstornosManuaisAsync(cmd.EmpresaId, pedido.Id, token)).Any(e => e.PagamentoId == pag.Id))
                throw new UseCaseValidationException("O recebimento possui devolução confirmada e deve permanecer no histórico.");

            // #1547: o recebimento já entrou no retrato de um dia fechado. Mesma guarda da devolução.
            await CaixaFechadoGuard.ExigirDiaAbertoAsync(caixa, cmd.EmpresaId, pag.PagoEm, pedido.LojaId,
                CaixaFechadoGuard.RecebimentoEmDiaFechado);

            // Remocao rastreada (mesmo motivo do RemoverItemPedidoUseCase, #768): FK
            // PedidoPagamento->Pedido required+Cascade, o DELETE participa do SaveChanges
            // do evento — atomico, sem ExecuteDeleteAsync imediato fora do UoW.
            pedido.Pagamentos.Remove(pag);

            await repo.AddEventoAsync(new PedidoEvento
            {
                Id = Guid.NewGuid(),
                PedidoId = pedido.Id,
                Tipo = "pagamento_removido",
                UsuarioId = cmd.UsuarioId,
                UsuarioNome = cmd.UsuarioNome,
                Origem = cmd.Origem,
                OcorridoEm = DateTime.UtcNow,
                Detalhes = $"-{pag.Valor.ToString("C", Cultura.PtBr)} ({pag.Metodo})"
        });

        await quitacao.ReabrirManualAsync(pedido, DateTime.UtcNow, token);
        await uow.CommitAsync();
        logger.LogInformation("Pedido {Id}: pagamento {Pag} removido.", pedido.Id, pag.Id);
        return CriarPedidoUseCase.Map(pedido);
        });
    }
}
