using EasyStock.Application.Events.Pedidos;
using EasyStock.Application.Ports.Output.Atendimento;
using EasyStock.Application.Ports.Output.Integration;
using EasyStock.Application.Ports.Output.Persistence.Pagamentos;
using EasyStock.Application.Services;
using EasyStock.Application.Services.Pedidos;
using EasyStock.Application.UseCases.CriarPedido;
using EasyStock.Application.UseCases.CancelarPedido;
using EasyStock.Application.UseCases.Financeiro.Integracao;
using EasyStock.Application.UseCases.Pedidos;
using EasyStock.Application.UseCases.Pedidos.Cobranca;
using EasyStock.Domain.Sales;

namespace EasyStock.Application.UseCases.AtualizarStatusPedido;

public sealed record AtualizarStatusPedidoCommand(
    [property: Required] Guid EmpresaId,
    [property: Required] Guid Id,
    [property: Required][property: MaxLength(20)] string Status,
    Guid? UsuarioId = null,
    [property: MaxLength(120)] string? UsuarioNome = null,
    [property: MaxLength(20)] string? Origem = "web",
    DateTime? OcorridoEm = null,
    NivelAcesso NivelSolicitante = NivelAcesso.Operador);

/// <summary>
/// Atualiza o status do pedido (aguardando → preparando → pronto → entregue).
///
/// Validação de transição é feita pela <see cref="PedidoStateMachine"/>
/// (centralizada no Domain). Este use case orquestra apenas:
///   1. parse do status string do command para enum
///   2. integração com estoque conforme a transição
///   3. delegação a <see cref="Pedido.MudarStatus"/>
///   4. registro de evento de auditoria + commit
///
/// Integração com estoque (PedidoEstoqueIntegrationService):
///   - Transição → Pronto ou Entregue (e estoque ainda não foi descontado):
///     desconta itens com ProdutoId.
///   - Transição → Cancelado (e estoque havia sido descontado):
///     devolve itens com ProdutoId.
///   - Idempotente: usa MovimentacaoEstoque.ReferenciaDocumento = pedidoId
///     pra evitar duplicação em retries.
/// </summary>
public class AtualizarStatusPedidoUseCase(
    IPedidoRepository pedidoRepo,
    PedidoEstoqueIntegrationService estoqueIntegration,
    IConfiguracaoLojaRepository configLojaRepo,
    GerarContaReceberDePedidoUseCase gerarContaReceberUseCase,
    IPublicadorEventoIntegracao publicadorEventos,
    IOperacaoEventPublisher operacaoEventos,
    IUnitOfWork uow,
    ILogger<AtualizarStatusPedidoUseCase> logger,
    CalculadoraInicioPrevistoPedido inicioPrevisto,
    CancelarPedidoUseCase cancelarPedido,
    ICobrancaPedidoRepository? cobrancas = null,
    TrocarFormaPagamentoPedidoUseCase? trocarForma = null)
{
    public async Task<PedidoResult?> ExecuteAsync(AtualizarStatusPedidoCommand cmd)
    {
        UseCaseGuards.EnsureEmpresaId(cmd.EmpresaId);
        UseCaseGuards.EnsureNotEmpty(cmd.Id, "Id");

        if (!StatusPedidoMapper.TryParse(cmd.Status, out var statusNovo))
            throw new UseCaseValidationException($"Status inválido: {cmd.Status}");

        if (statusNovo == StatusPedido.Cancelado)
            return await cancelarPedido.ExecuteAsync(new CancelarPedidoCommand(cmd.EmpresaId, cmd.Id,
                cmd.UsuarioId, cmd.UsuarioNome, "Cancelado pela troca de status", cmd.Origem, cmd.NivelSolicitante, cmd.OcorridoEm));

        // CRITICAL: usar GetByIdWithDetailsAsync para carregar Itens (Include).
        // Sem isso, pedido.Itens vem vazio e a integração com estoque vira
        // no-op silencioso quando a transição deveria descontar.
        var pedido = await pedidoRepo.GetByIdWithDetailsAsync(cmd.EmpresaId, cmd.Id);
        if (pedido == null || pedido.EmpresaId != cmd.EmpresaId) return null;

        var statusAtual = pedido.StatusEnum;

        if (statusAtual == statusNovo)
            return CriarPedidoUseCase.Map(pedido); // idempotente

        // Validação de transição (lança TransicaoInvalidaException se inválida).
        // Convertemos pra UseCaseValidationException pra preservar o contrato
        // HTTP atual (400 Bad Request) que clients (PWA, mobile, MAUI) esperam.
        try
        {
            PedidoStateMachine.EnsureTransicaoValida(statusAtual, statusNovo);
        }
        catch (TransicaoInvalidaException ex)
        {
            throw new UseCaseValidationException(ex.Message);
        }

        // #1726: liberar sem pagamento um pedido com link pendente é trocar a forma para "na entrega". O link
        // morre, a dívida continua, a trilha guarda quem liberou e o pedido de exceção ainda espera a dona.
        if (statusAtual == StatusPedido.AguardandoPagamento && statusNovo == StatusPedido.Aguardando
            && cobrancas is not null && trocarForma is not null
            && (await cobrancas.ListarDoPedidoAsync(cmd.EmpresaId, pedido.Id)).Any(c => c.EstaPendente && c.EhOnline))
        {
            try
            {
                await trocarForma.ExecuteAsync(new TrocarFormaPagamentoPedidoInput(cmd.EmpresaId, pedido.Id,
                    TrocarFormaPagamentoPedidoUseCase.FormaNaEntrega, cmd.UsuarioId, cmd.UsuarioNome));
            }
            catch (CobrancaPedidoConflitoException ex)
            {
                // Mesmo contrato das transições inválidas: 400, não erro interno.
                throw new UseCaseValidationException(ex.Message);
            }
            return CriarPedidoUseCase.Map(pedido);
        }

        var statusAntigoStr = pedido.Status;
        var eraEstoqueDescontado = PedidoStateMachine.DescontaEstoque(statusAtual);
        var seraEstoqueDescontado = PedidoStateMachine.DescontaEstoque(statusNovo);

        // Integração estoque PRIMEIRO — se falhar (ex: estoque insuficiente),
        // status não é alterado e o caller recebe a exceção. Atomicidade
        // a nível de aplicação: ou ambos (status + estoque) mudam, ou nada.
        // Idempotência via DocumentoReferencia evita double-debit em retries.
        if (!eraEstoqueDescontado && seraEstoqueDescontado)
        {
            await estoqueIntegration.DescontarAsync(pedido);
        }

        // Estoque OK — agora aplica transição no agregado. MudarStatus é
        // idempotente e re-valida (defesa em profundidade); como já validamos
        // acima, o re-check é apenas paranoia barata.
        pedido.MudarStatus(statusNovo);
        // #1230: AguardandoPagamento -> Aguardando pela troca genérica (a dona recebeu por fora) entra na
        // fila com início previsto; nos demais status é no-op sem consulta.
        await inicioPrevisto.AplicarNaFilaAsync(pedido);

        var statusNovoStr = StatusPedidoMapper.Format(statusNovo);
        await pedidoRepo.AddEventoAsync(new PedidoEvento
        {
            Id = Guid.NewGuid(),
            PedidoId = pedido.Id,
            Tipo = "status_changed",
            StatusAntigo = statusAntigoStr,
            StatusNovo = statusNovoStr,
            UsuarioId = cmd.UsuarioId,
            UsuarioNome = cmd.UsuarioNome,
            Origem = cmd.Origem,
            OcorridoEm = OcorridoEmAuditoria(cmd.OcorridoEm)
        });

        // Ponto de integracao unico da esteira (ADR-0042, Onda 4): publica a transicao no
        // outbox, na MESMA transacao do commit de status. Hiram/marketplaces consomem este
        // evento em vez de observar a coluna Status (issue 866).
        await publicadorEventos.PublicarAsync(
            empresaId: pedido.EmpresaId,
            tipoEvento: "pedido.mudou_status",
            aggregateType: "pedido",
            aggregateId: pedido.Id,
            payload: new PedidoMudouStatusEvent(
                PedidoId: pedido.Id,
                EmpresaId: pedido.EmpresaId,
                LojaId: pedido.LojaId,
                StatusAntigo: statusAntigoStr,
                StatusNovo: statusNovoStr,
                Origem: cmd.Origem,
                UsuarioId: cmd.UsuarioId,
                UsuarioNome: cmd.UsuarioNome,
                OcorridoEm: DateTime.UtcNow),
            correlationId: pedido.Id.ToString());

        await pedidoRepo.UpdateAsync(pedido);
        await uow.CommitAsync();

        logger.LogInformation("Pedido {Id} status {Antigo} → {Novo}.", pedido.Id, statusAntigoStr, statusNovoStr);

        // SSE de operação (S18): evento de UI, só depois do commit — commit que lança não publica.
        await operacaoEventos.PublicarAsync(EventosOperacao.PedidoMudouStatus, pedido.EmpresaId,
            new PedidoMudouStatusOperacao(pedido.Id, statusAntigoStr, statusNovoStr));

        // Integracao automatica CAP/CAR (P1): se status novo coincide com configuracao,
        // gera ContaReceber. Best-effort: falha aqui nao reverte status do pedido
        // (idempotencia via OrigemRefId garante retry seguro depois).
        if (pedido.LojaId.HasValue)
        {
            try
            {
                var config = await configLojaRepo.GetByLojaIdAsync(pedido.LojaId.Value);
                if (config is not null &&
                    config.GerarContaReceberAutomaticaDePedido &&
                    string.Equals(statusNovoStr, config.StatusPedidoQueGeraContaReceber, StringComparison.OrdinalIgnoreCase))
                {
                    await gerarContaReceberUseCase.ExecuteAsync(
                        new GerarContaReceberDePedidoCommand(pedido.EmpresaId, pedido,
                            cmd.UsuarioId, cmd.UsuarioNome));
                }
            }
            catch (Exception ex)
            {
                logger.LogError(ex,
                    "Falha ao gerar ContaReceber automatica pra pedido {Id} — status mantido. " +
                    "Idempotencia via OrigemRefId permite retry.", pedido.Id);
            }
        }

        return CriarPedidoUseCase.Map(pedido);
    }

    /// <summary>
    /// Instante do toque informado pelo aparelho (fila offline do KDS, S19), em UTC e nunca no futuro;
    /// sem ele, agora. Só alimenta o evento de auditoria.
    /// </summary>
    internal static DateTime OcorridoEmAuditoria(DateTime? informado)
    {
        var agora = DateTime.UtcNow;
        if (informado is not { } o) return agora;
        var utc = o.Kind switch
        {
            DateTimeKind.Utc => o,
            DateTimeKind.Local => o.ToUniversalTime(),
            _ => DateTime.SpecifyKind(o, DateTimeKind.Utc),
        };
        return utc > agora ? agora : utc;
    }
}
