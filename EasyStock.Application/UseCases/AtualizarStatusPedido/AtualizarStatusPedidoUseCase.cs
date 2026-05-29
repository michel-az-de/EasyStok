using EasyStock.Application.Services;
using EasyStock.Application.UseCases.CriarPedido;
using EasyStock.Application.UseCases.Financeiro.Integracao;
using EasyStock.Application.UseCases.Pedidos;
using EasyStock.Domain.Sales;

namespace EasyStock.Application.UseCases.AtualizarStatusPedido;

public sealed record AtualizarStatusPedidoCommand(
    [property: Required] Guid EmpresaId,
    [property: Required] Guid Id,
    [property: Required][property: MaxLength(20)] string Status,
    Guid? UsuarioId = null,
    [property: MaxLength(120)] string? UsuarioNome = null,
    [property: MaxLength(20)] string? Origem = "web");

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
    IUnitOfWork uow,
    ILogger<AtualizarStatusPedidoUseCase> logger)
{
    public async Task<PedidoResult?> ExecuteAsync(AtualizarStatusPedidoCommand cmd)
    {
        UseCaseGuards.EnsureEmpresaId(cmd.EmpresaId);
        UseCaseGuards.EnsureNotEmpty(cmd.Id, "Id");

        if (!StatusPedidoMapper.TryParse(cmd.Status, out var statusNovo))
            throw new UseCaseValidationException($"Status inválido: {cmd.Status}");

        // CRITICAL: usar GetByIdWithDetailsAsync para carregar Itens (Include).
        // Sem isso, pedido.Itens vem vazio e a integração com estoque vira
        // no-op silencioso quando a transição deveria descontar.
        var pedido = await pedidoRepo.GetByIdWithDetailsAsync(cmd.EmpresaId, cmd.Id);
        if (pedido == null) return null;

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
        else if (eraEstoqueDescontado && statusNovo == StatusPedido.Cancelado)
        {
            await estoqueIntegration.DevolverAsync(pedido);
        }

        // Estoque OK — agora aplica transição no agregado. MudarStatus é
        // idempotente e re-valida (defesa em profundidade); como já validamos
        // acima, o re-check é apenas paranoia barata.
        pedido.MudarStatus(statusNovo);

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
            OcorridoEm = DateTime.UtcNow
        });

        await pedidoRepo.UpdateAsync(pedido);
        await uow.CommitAsync();

        logger.LogInformation("Pedido {Id} status {Antigo} → {Novo}.", pedido.Id, statusAntigoStr, statusNovoStr);

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
}
