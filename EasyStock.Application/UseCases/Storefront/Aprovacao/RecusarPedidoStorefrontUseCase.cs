using System.Diagnostics;
using EasyStock.Application.Events.Pedidos;
using EasyStock.Application.Events.Storefront.Handlers;
using EasyStock.Application.Ports.Output.Integration;
using EasyStock.Application.Ports.Output.Pagamentos;
using EasyStock.Application.Ports.Output.Persistence.Pagamentos;
using EasyStock.Application.Ports.Output.Persistence.Storefront;
using EasyStock.Application.Services.Atendimento;
using EasyStock.Application.Services.Pedidos;
using EasyStock.Application.UseCases.Pedidos.Cobranca;
using EasyStock.Application.UseCases.Storefront.Aprovacao.Exceptions;
using EasyStock.Domain.Entities.Pagamentos;
using EasyStock.Domain.Enums.Pagamentos;
using EasyStock.Domain.Events.Storefront;
using EasyStock.Domain.Sales;
using PedidoEntity = EasyStock.Domain.Entities.Pedido;

namespace EasyStock.Application.UseCases.Storefront.Aprovacao;

/// <summary>
/// Use case <strong>Recusar Pedido Storefront</strong> (TASK-EZ-APROVAR-001, Fase 6 do plano v8.0; #1289).
///
/// <para>
/// Fluxo (single transaction via <see cref="IUnitOfWork.ExecuteInTransactionAsync"/>):
/// </para>
/// <list type="number">
///   <item>SELECT FOR UPDATE no <c>Pedido</c> (lock pessimista — ADR-0014).</item>
///   <item>Valida tenant — mismatch → <see cref="PedidoNaoEncontradoException"/> (404).</item>
///   <item>Valida status atual == <see cref="StatusPedido.AguardandoAprovacaoBaba"/>.</item>
///   <item>
///     #1289: estorna na hora cada cobrança <c>Paga</c> do Mercado Pago (pedido <c>RequerAprovacao</c> já
///     pago) com a chave <c>recusa-{pedido}-{pagamento}</c> e marca a cobrança <c>Estornada</c>. Estorno
///     recusado lança <see cref="EstornoAutomaticoFalhouException"/> antes de qualquer mudança: o pedido
///     continua aguardando a dona e a nova tentativa repete a mesma chave (sem estorno em dobro).
///   </item>
///   <item>
///     Atualiza Pedido: status = <see cref="StatusPedido.Cancelado"/>, <c>CanceladoEm</c>,
///     <c>RecusadoEm</c>, <c>RecusadoPorUsuarioId</c>, <c>MotivoRecusa</c>, <c>MensagemRecusaCliente</c>.
///   </item>
///   <item>Libera a vaga pelo <see cref="LiberarVagaOnPedidoCanceladoHandler"/> (mesmo uso do job de cobrança).</item>
///   <item><c>pedido.mudou_status</c> no Outbox (MESMA TX) e trilha <c>recusado_storefront</c>.</item>
///   <item>Commit; depois dele, aviso best-effort na conversa da cobrança paga.</item>
/// </list>
///
/// <para>
/// Antes do #1289 a recusa só enfileirava <c>storefront.pedido.cancelado</c>,
/// <c>storefront.pagamento.estorno_solicitado</c> e <c>storefront.pedido.recusado_notificar_cliente</c>, que
/// nunca tiveram handler: o dinheiro ficava retido e a vaga presa.
/// </para>
///
/// <para>
/// <strong>Validação de motivo</strong> fica no controller (parse + 422); use case
/// recebe <see cref="MotivoRecusa"/> tipado — ArgumentException defensiva apenas.
/// </para>
/// </summary>
public sealed class RecusarPedidoStorefrontUseCase(
    IPedidoStorefrontRepository pedidoRepository,
    ICobrancaPedidoRepository cobrancaRepository,
    IEstornoPedidoGateway estorno,
    LiberarVagaOnPedidoCanceladoHandler liberarVaga,
    AvisoCobrancaConversa aviso,
    IPublicadorEventoIntegracao publicadorEventos,
    IUnitOfWork unitOfWork,
    ILogger<RecusarPedidoStorefrontUseCase> logger)
{
    private const int MensagemClienteMaxChars = 280;
    private const string Origem = "storefront";

    /// <summary>Valor de <see cref="RefundEnfileirado.Evento"/> quando o pagamento foi estornado na recusa.</summary>
    public const string RefundEstornado = "estorno_mercadopago";

    /// <summary>Valor de <see cref="RefundEnfileirado.Evento"/> quando não havia pagamento a devolver.</summary>
    public const string RefundSemPagamento = "sem_pagamento";

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

        // Conversa a avisar depois do commit; reatribuída a cada tentativa da transação.
        Guid? conversaAviso = null;
        var resultado = await unitOfWork.ExecuteInTransactionAsync(async innerCt =>
        {
            conversaAviso = null;

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

            // 4. #1289: devolve o dinheiro antes de mudar qualquer coisa.
            var agora = DateTime.UtcNow;
            var estornadas = await EstornarCobrancasPagasAsync(pedido, motivoCanonical, agora, input.NivelSolicitante, innerCt);
            conversaAviso = estornadas.Select(c => c.ConversaId).FirstOrDefault(c => c is not null);

            // 5. Aplicar transição + audit trail.
            var statusAntigo = pedido.Status;
            pedido.Status = StatusPedidoMapper.Cancelado;
            pedido.CanceladoEm = agora;
            pedido.RecusadoEm = agora;
            pedido.RecusadoPorUsuarioId = input.UsuarioId;
            pedido.MotivoRecusa = motivoCanonical;
            pedido.MensagemRecusaCliente = input.MensagemCliente;
            pedido.AlteradoEm = agora;
            await pedidoRepository.UpdateAsync(pedido, innerCt);

            // 6. Vaga liberada na mesma transação (idempotente).
            await liberarVaga.HandleAsync(
                new PedidoCanceladoEvent(pedido.Id, Guid.Empty, $"recusado_baba: {motivoCanonical}"), innerCt);

            // 7. Outbox: o evento de status que a esteira (log, automações, avisos) já consome.
            await publicadorEventos.PublicarAsync(
                empresaId: pedido.EmpresaId,
                tipoEvento: "pedido.mudou_status",
                aggregateType: "pedido",
                aggregateId: pedido.Id,
                payload: new PedidoMudouStatusEvent(pedido.Id, pedido.EmpresaId, pedido.LojaId, statusAntigo,
                    pedido.Status, Origem, input.UsuarioId, input.UsuarioNome, agora),
                correlationId: pedido.Id.ToString(),
                ct: innerCt);

            // 8. Trilha por último: o flush dela grava cobrança, vaga e outbox juntos.
            await PedidoEventoRecusado(pedido, input, motivoCanonical, agora, innerCt);

            logger.LogInformation(
                "Recusar pedido sucesso pedidoId={PedidoId} empresaId={EmpresaId} usuarioId={UsuarioId} action=recusar motivo={Motivo} estornos={Estornos} durationMs={Ms}",
                pedido.Id, pedido.EmpresaId, input.UsuarioId, motivoCanonical, estornadas.Count, sw.ElapsedMilliseconds);

            return new RecusarPedidoStorefrontResult(
                PedidoId: pedido.Id,
                Status: pedido.Status,
                RecusadoEm: agora,
                RecusadoPor: input.UsuarioNome ?? input.UsuarioId.ToString(),
                Motivo: motivoCanonical,
                VagaLiberada: true,
                Refund: new RefundEnfileirado(
                    estornadas.Count > 0,
                    estornadas.Count > 0 ? RefundEstornado : RefundSemPagamento),
                NotificacaoCliente: new NotificacaoCliente(Enfileirada: false, Evento: nameof(AvisoCobrancaConversa)));
        }, ct);

        // Aviso ao cliente só depois do commit; best-effort, não desfaz a recusa se falhar.
        if (conversaAviso is { } conversaId)
        {
            var enviado = await aviso.EnviarAsync(input.EmpresaId, conversaId,
                AvisoCobrancaConversa.TextoPedidoRecusadoEstornado(input.MensagemCliente), DateTime.UtcNow, ct);
            resultado = resultado with { NotificacaoCliente = resultado.NotificacaoCliente with { Enfileirada = enviado } };
        }

        return resultado;
    }

    /// <summary>
    /// Estorna no gateway cada cobrança paga do Mercado Pago e a marca <c>Estornada</c>. A chave de idempotência
    /// é estável por pedido e pagamento: repetir a recusa (retry da transação, nova tentativa da dona depois de
    /// uma falha) não devolve duas vezes.
    /// </summary>
    private async Task<IReadOnlyList<CobrancaPedido>> EstornarCobrancasPagasAsync(
        PedidoEntity pedido, string motivoCanonical, DateTime agora, NivelAcesso nivel, CancellationToken ct)
    {
        var cobrancas = await cobrancaRepository.ListarDoPedidoAsync(pedido.EmpresaId, pedido.Id, ct);
        EfeitosCancelamentoPedido.ExigirPermissao(
            pedido.TotalPago > 0 || cobrancas.Any(c => c.Status == StatusCobrancaPedido.Paga), nivel);
        var pagas = cobrancas
            .Where(c => c.Status == StatusCobrancaPedido.Paga && c.EhOnline && !string.IsNullOrWhiteSpace(c.PagamentoExternoId))
            .ToList();

        foreach (var paga in pagas)
        {
            var pagamentoId = paga.PagamentoExternoId!;
            var valor = paga.ValorPago ?? paga.Valor;
            var r = await estorno.EstornarAsync(pagamentoId, valor, $"recusa-{pedido.Id}-{pagamentoId}", ct);
            if (!r.Sucesso)
            {
                logger.LogWarning("Recusar pedido: estorno recusado pedidoId={PedidoId} pagamento={Pagamento} erro={Erro}",
                    pedido.Id, pagamentoId, r.Erro);
                throw new EstornoAutomaticoFalhouException(pagamentoId, r.Erro);
            }

            paga.MarcarEstornada(
                $"estorno_recusa: pagamento {pagamentoId} de {valor.ToString("F2", Cultura.PtBr)} devolvido na recusa " +
                $"do pedido ({motivoCanonical}); estorno {r.IdSolicitacao}", agora);
        }

        return pagas;
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
            Origem = Origem,
            OcorridoEm = quando,
        };
        await pedidoRepository.AddEventoAsync(evento, ct);
    }
}
