using System.Diagnostics;
using EasyStock.Application.Events.Storefront;
using EasyStock.Application.Ports.Output.Integration;
using EasyStock.Application.Ports.Output.Persistence.Storefront;
using EasyStock.Application.UseCases.Storefront.Aprovacao.Exceptions;
using EasyStock.Domain.Events.Storefront;
using EasyStock.Domain.Sales;
using PedidoEntity = EasyStock.Domain.Entities.Pedido;

namespace EasyStock.Application.UseCases.Storefront.Aprovacao;

/// <summary>
/// Use case <strong>Recusar Pedido Storefront</strong> (TASK-EZ-APROVAR-001, Fase 6 do plano v8.0).
///
/// <para>
/// Fluxo (single transaction via <see cref="IUnitOfWork.ExecuteInTransactionAsync"/>):
/// </para>
/// <list type="number">
///   <item>SELECT FOR UPDATE no <c>Pedido</c> (lock pessimista — ADR-0014).</item>
///   <item>Valida tenant — mismatch → <see cref="PedidoNaoEncontradoException"/> (404).</item>
///   <item>Valida status atual == <see cref="StatusPedido.AguardandoAprovacaoBaba"/>.</item>
///   <item>
///     Atualiza Pedido: status = <see cref="StatusPedido.Cancelado"/>, <c>CanceladoEm</c>,
///     <c>RecusadoEm</c>, <c>RecusadoPorUsuarioId</c>, <c>MotivoRecusa</c>, <c>MensagemRecusaCliente</c>.
///   </item>
///   <item>
///     Enfileira <strong>3 eventos</strong> no Outbox (MESMA TX):
///     <list type="bullet">
///       <item><see cref="PedidoCanceladoEvent"/> — handler <c>LiberarVagaOnPedidoCanceladoHandler</c> libera vaga.</item>
///       <item><see cref="EstornarPagamentoAutomaticoEvent"/> — dispatcher MP (TASK-EZ-APROVAR-002) chama refund.</item>
///       <item><see cref="NotificarClientePagamentoRecusadoEvent"/> — handler WhatsApp envia mensagem.</item>
///     </list>
///   </item>
///   <item>Commit transacional.</item>
/// </list>
///
/// <para>
/// <strong>Validação de motivo</strong> fica no controller (parse + 422); use case
/// recebe <see cref="MotivoRecusa"/> tipado — ArgumentException defensiva apenas.
/// </para>
/// </summary>
public sealed class RecusarPedidoStorefrontUseCase(
    IPedidoStorefrontRepository pedidoRepository,
    IPublicadorEventoIntegracao publicadorEventos,
    IUnitOfWork unitOfWork,
    ILogger<RecusarPedidoStorefrontUseCase> logger)
{
    private const int MensagemClienteMaxChars = 280;

    public async Task<RecusarPedidoStorefrontResult> ExecuteAsync(
        RecusarPedidoStorefrontInput input,
        CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(input);

        if (input.PedidoId == Guid.Empty)
            throw new ArgumentException("PedidoId obrigatório.", nameof(input));
        if (input.EmpresaId == Guid.Empty)
            throw new ArgumentException("EmpresaId obrigatório.", nameof(input));
        if (input.UsuarioId == Guid.Empty)
            throw new ArgumentException("UsuarioId obrigatório.", nameof(input));

        if (input.MensagemCliente is { Length: > MensagemClienteMaxChars })
            throw new ArgumentException(
                $"MensagemCliente acima do limite ({MensagemClienteMaxChars} chars).",
                nameof(input));

        // Defesa adicional: enum value inválido (cast de int desconhecido).
        if (!Enum.IsDefined(typeof(MotivoRecusa), input.Motivo))
            throw new ArgumentOutOfRangeException(
                nameof(input.Motivo), input.Motivo, "Motivo de recusa fora do enum válido.");

        var sw = Stopwatch.StartNew();
        var motivoCanonical = input.Motivo.ToCanonicalString();

        return await unitOfWork.ExecuteInTransactionAsync(async innerCt =>
        {
            // 1. SELECT FOR UPDATE.
            var pedido = await pedidoRepository.GetForUpdateAsync(input.PedidoId, innerCt);

            // 2. Tenant isolation → 404.
            if (pedido is null || pedido.EmpresaId != input.EmpresaId)
            {
                logger.LogWarning(
                    "Recusar pedido nao encontrado pedidoId={PedidoId} empresaId={EmpresaId} usuarioId={UsuarioId}",
                    input.PedidoId, input.EmpresaId, input.UsuarioId);
                throw new PedidoNaoEncontradoException(input.PedidoId);
            }

            // 3. Status mismatch → 409.
            if (!StatusPedidoMapper.TryParse(pedido.Status, out var statusAtual)
                || statusAtual != StatusPedido.AguardandoAprovacaoBaba)
            {
                var resolvidoEm = pedido.RecusadoEm ?? pedido.AprovadoEm ?? pedido.CanceladoEm;
                logger.LogInformation(
                    "Recusar pedido ja resolvido pedidoId={PedidoId} statusAtual={Status} resolvidoEm={ResolvidoEm}",
                    input.PedidoId, pedido.Status, resolvidoEm);
                throw new PedidoJaResolvidoException(
                    input.PedidoId,
                    StatusPedidoMapper.TryParse(pedido.Status, out var parsed)
                        ? parsed
                        : StatusPedido.Cancelado,
                    resolvidoEm);
            }

            // 4. Aplicar transição + audit trail.
            var agora = DateTime.UtcNow;
            pedido.Status = StatusPedidoMapper.Cancelado;
            pedido.CanceladoEm = agora;
            pedido.RecusadoEm = agora;
            pedido.RecusadoPorUsuarioId = input.UsuarioId;
            pedido.MotivoRecusa = motivoCanonical;
            pedido.MensagemRecusaCliente = input.MensagemCliente;
            pedido.AlteradoEm = agora;
            await pedidoRepository.UpdateAsync(pedido, innerCt);

            // 5. Outbox — 3 eventos na MESMA TX.

            // 5.1. PedidoCanceladoEvent (handler LiberarVaga libera VagaOcupada, idempotente).
            var canceladoEvt = new PedidoCanceladoEvent(
                PedidoId: pedido.Id,
                StorefrontId: Guid.Empty, // não rastreamos StorefrontId no Pedido base; handler usa só PedidoId.
                Motivo: $"recusado_baba: {motivoCanonical}");
            await publicadorEventos.PublicarAsync(
                empresaId: pedido.EmpresaId,
                tipoEvento: "storefront.pedido.cancelado",
                aggregateType: "pedido",
                aggregateId: pedido.Id,
                payload: canceladoEvt,
                correlationId: pedido.Id.ToString(),
                ct: innerCt);

            // 5.2. EstornarPagamentoAutomaticoEvent (refund MP — TASK-EZ-APROVAR-002 dispatcher).
            var refundEvt = new EstornarPagamentoAutomaticoEvent(
                PedidoId: pedido.Id,
                EmpresaId: pedido.EmpresaId,
                ValorTotal: pedido.Total.Valor,
                Motivo: motivoCanonical,
                SolicitadoEm: agora);
            await publicadorEventos.PublicarAsync(
                empresaId: pedido.EmpresaId,
                tipoEvento: "storefront.pagamento.estorno_solicitado",
                aggregateType: "pedido",
                aggregateId: pedido.Id,
                payload: refundEvt,
                correlationId: pedido.Id.ToString(),
                ct: innerCt);

            // 5.3. NotificarClientePagamentoRecusadoEvent.
            var notificacao = new NotificarClientePagamentoRecusadoEvent(
                PedidoId: pedido.Id,
                EmpresaId: pedido.EmpresaId,
                ClienteId: pedido.ClienteId,
                ClienteNome: pedido.ClienteNome,
                ClienteTelefone: pedido.ClienteTelefone,
                Motivo: motivoCanonical,
                MensagemCliente: input.MensagemCliente,
                RecusadoEm: agora);
            await publicadorEventos.PublicarAsync(
                empresaId: pedido.EmpresaId,
                tipoEvento: "storefront.pedido.recusado_notificar_cliente",
                aggregateType: "pedido",
                aggregateId: pedido.Id,
                payload: notificacao,
                correlationId: pedido.Id.ToString(),
                ct: innerCt);

            await PedidoEventoRecusado(pedido, input, motivoCanonical, agora, innerCt);

            logger.LogInformation(
                "Recusar pedido sucesso pedidoId={PedidoId} empresaId={EmpresaId} usuarioId={UsuarioId} action=recusar motivo={Motivo} durationMs={Ms}",
                pedido.Id, pedido.EmpresaId, input.UsuarioId, motivoCanonical, sw.ElapsedMilliseconds);

            return new RecusarPedidoStorefrontResult(
                PedidoId: pedido.Id,
                Status: pedido.Status,
                RecusadoEm: agora,
                RecusadoPor: input.UsuarioNome ?? input.UsuarioId.ToString(),
                Motivo: motivoCanonical,
                VagaLiberada: true,
                Refund: new RefundEnfileirado(true, nameof(EstornarPagamentoAutomaticoEvent)),
                NotificacaoCliente: new NotificacaoCliente(
                    Enfileirada: true,
                    Evento: nameof(NotificarClientePagamentoRecusadoEvent)));
        }, ct);
    }

    private async Task PedidoEventoRecusado(
        PedidoEntity pedido,
        RecusarPedidoStorefrontInput input,
        string motivoCanonical,
        DateTime quando,
        CancellationToken ct)
    {
        var detalhes = string.IsNullOrWhiteSpace(input.MensagemCliente)
            ? motivoCanonical
            : $"{motivoCanonical} | {input.MensagemCliente}";

        var evento = new PedidoEvento
        {
            Id = Guid.NewGuid(),
            PedidoId = pedido.Id,
            Tipo = "recusado_storefront",
            StatusAntigo = StatusPedidoMapper.AguardandoAprovacaoBaba,
            StatusNovo = StatusPedidoMapper.Cancelado,
            Detalhes = detalhes,
            UsuarioId = input.UsuarioId,
            UsuarioNome = input.UsuarioNome,
            Origem = "storefront",
            OcorridoEm = quando,
        };
        await pedidoRepository.AddEventoAsync(evento, ct);
    }
}
