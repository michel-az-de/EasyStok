using System.Diagnostics;
using EasyStock.Application.Events.Storefront;
using EasyStock.Application.Ports.Output.Integration;
using EasyStock.Application.Ports.Output.Persistence;
using EasyStock.Application.Ports.Output.Persistence.Storefront;
using EasyStock.Application.UseCases.Storefront.Aprovacao.Exceptions;
using EasyStock.Domain.Entities;
using EasyStock.Domain.Sales;
using Microsoft.Extensions.Logging;
using PedidoEntity = EasyStock.Domain.Entities.Pedido;

namespace EasyStock.Application.UseCases.Storefront.Aprovacao;

/// <summary>
/// Use case <strong>Aprovar Pedido Storefront</strong> (TASK-EZ-APROVAR-001, Fase 6 do plano v8.0).
///
/// <para>
/// Fluxo (single transaction via <see cref="IUnitOfWork.ExecuteInTransactionAsync"/>):
/// </para>
/// <list type="number">
///   <item>SELECT FOR UPDATE no <c>Pedido</c> via <see cref="IPedidoStorefrontRepository.GetForUpdateAsync"/>.</item>
///   <item>Valida tenant (<c>EmpresaId</c>) — mismatch → <c>PedidoNaoEncontradoException</c> (404, não vaza existência).</item>
///   <item>Valida status atual == <see cref="StatusPedido.AguardandoAprovacaoBaba"/> — mismatch → <see cref="PedidoJaResolvidoException"/> (409).</item>
///   <item>Atualiza Pedido: status = <see cref="StatusPedido.AprovadoBaba"/>, <c>AprovadoEm</c>, <c>AprovadoPorUsuarioId</c>.</item>
///   <item>Enfileira <see cref="NotificarClientePedidoAprovadoEvent"/> no Outbox (mesma TX).</item>
///   <item>Commit transacional pelo <c>IUnitOfWork</c>.</item>
/// </list>
///
/// <para>
/// <strong>Concorrência:</strong> dois agentes Babá clicando "Aprovar" simultaneamente —
/// um pega o lock, faz update, commita. O outro acorda, lê status novo
/// (<c>aprovado_baba</c>) e lança <see cref="PedidoJaResolvidoException"/>. Garante 1 sucesso.
/// </para>
///
/// <para>
/// <strong>Auth:</strong> exigência <c>[Authorize]</c> está no controller — use case
/// confia no <see cref="AprovarPedidoStorefrontInput.UsuarioId"/> vindo de cima.
/// </para>
/// </summary>
public sealed class AprovarPedidoStorefrontUseCase(
    IPedidoStorefrontRepository pedidoRepository,
    IPublicadorEventoIntegracao publicadorEventos,
    IUnitOfWork unitOfWork,
    ILogger<AprovarPedidoStorefrontUseCase> logger)
{
    public async Task<AprovarPedidoStorefrontResult> ExecuteAsync(
        AprovarPedidoStorefrontInput input,
        CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(input);

        if (input.PedidoId == Guid.Empty)
            throw new ArgumentException("PedidoId obrigatório.", nameof(input));
        if (input.EmpresaId == Guid.Empty)
            throw new ArgumentException("EmpresaId obrigatório.", nameof(input));
        if (input.UsuarioId == Guid.Empty)
            throw new ArgumentException("UsuarioId obrigatório.", nameof(input));

        var sw = Stopwatch.StartNew();

        return await unitOfWork.ExecuteInTransactionAsync(async innerCt =>
        {
            // 1. SELECT FOR UPDATE no Pedido (lock pessimista — ADR-0014).
            var pedido = await pedidoRepository.GetForUpdateAsync(input.PedidoId, innerCt);

            // 2. Tenant isolation: pedido inexistente OU de outro tenant → 404
            //    (mesma resposta evita oracle de existência cross-tenant).
            if (pedido is null || pedido.EmpresaId != input.EmpresaId)
            {
                logger.LogWarning(
                    "Aprovar pedido nao encontrado pedidoId={PedidoId} empresaId={EmpresaId} usuarioId={UsuarioId}",
                    input.PedidoId, input.EmpresaId, input.UsuarioId);
                throw new PedidoNaoEncontradoException(input.PedidoId);
            }

            // 3. Status mismatch → 409.
            if (!StatusPedidoMapper.TryParse(pedido.Status, out var statusAtual)
                || statusAtual != StatusPedido.AguardandoAprovacaoBaba)
            {
                var resolvidoEm = pedido.AprovadoEm ?? pedido.RecusadoEm ?? pedido.CanceladoEm;
                logger.LogInformation(
                    "Aprovar pedido ja resolvido pedidoId={PedidoId} statusAtual={Status} resolvidoEm={ResolvidoEm}",
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
            pedido.Status = StatusPedidoMapper.AprovadoBaba;
            pedido.AprovadoEm = agora;
            pedido.AprovadoPorUsuarioId = input.UsuarioId;
            pedido.AlteradoEm = agora;
            if (!string.IsNullOrWhiteSpace(input.Observacoes))
            {
                // Append observações internas — preserva conteúdo anterior.
                pedido.Observacoes = string.IsNullOrWhiteSpace(pedido.Observacoes)
                    ? input.Observacoes
                    : $"{pedido.Observacoes}\n[aprovado] {input.Observacoes}";
            }
            await pedidoRepository.UpdateAsync(pedido, innerCt);

            // 5. Outbox.Enqueue MESMA TX (atomic).
            var notificacao = new NotificarClientePedidoAprovadoEvent(
                PedidoId: pedido.Id,
                EmpresaId: pedido.EmpresaId,
                ClienteId: pedido.ClienteId,
                ClienteNome: pedido.ClienteNome,
                ClienteTelefone: pedido.ClienteTelefone,
                AprovadoEm: agora);

            await publicadorEventos.PublicarAsync(
                empresaId: pedido.EmpresaId,
                tipoEvento: "storefront.pedido.aprovado",
                aggregateType: "pedido",
                aggregateId: pedido.Id,
                payload: notificacao,
                correlationId: pedido.Id.ToString(),
                ct: innerCt);

            await PedidoEventoAprovado(pedido, input, agora, innerCt);

            logger.LogInformation(
                "Aprovar pedido sucesso pedidoId={PedidoId} empresaId={EmpresaId} usuarioId={UsuarioId} action=aprovar durationMs={Ms}",
                pedido.Id, pedido.EmpresaId, input.UsuarioId, sw.ElapsedMilliseconds);

            return new AprovarPedidoStorefrontResult(
                PedidoId: pedido.Id,
                Status: pedido.Status,
                AprovadoEm: agora,
                AprovadoPor: input.UsuarioNome ?? input.UsuarioId.ToString(),
                NotificacaoCliente: new NotificacaoCliente(
                    Enfileirada: true,
                    Evento: nameof(NotificarClientePedidoAprovadoEvent)));
        }, ct);
    }

    private async Task PedidoEventoAprovado(
        PedidoEntity pedido,
        AprovarPedidoStorefrontInput input,
        DateTime quando,
        CancellationToken ct)
    {
        var evento = new PedidoEvento
        {
            Id = Guid.NewGuid(),
            PedidoId = pedido.Id,
            Tipo = "aprovado_storefront",
            StatusAntigo = StatusPedidoMapper.AguardandoAprovacaoBaba,
            StatusNovo = StatusPedidoMapper.AprovadoBaba,
            Detalhes = input.Observacoes,
            UsuarioId = input.UsuarioId,
            UsuarioNome = input.UsuarioNome,
            Origem = "storefront",
            OcorridoEm = quando,
        };
        await pedidoRepository.AddEventoAsync(evento, ct);
    }
}
