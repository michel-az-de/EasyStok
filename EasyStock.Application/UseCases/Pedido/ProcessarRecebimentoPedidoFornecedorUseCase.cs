using EasyStock.Application.Ports.Output.Events;
using EasyStock.Application.Ports.Output.Persistence;
using EasyStock.Application.UseCases.Common;
using EasyStock.Application.UseCases.RegistrarEntradaEstoque;
using EasyStock.Domain.Enums;
using EasyStock.Domain.Events;
using EasyStock.Domain.Exceptions;
using Microsoft.Extensions.Logging;

namespace EasyStock.Application.UseCases.Pedido;

public sealed record ProcessarRecebimentoPedidoFornecedorCommand(
    Guid PedidoId,
    Guid EmpresaId,
    DateTime? DataRecebimento,
    IDictionary<Guid, decimal> ItensRecebidos);

public sealed record ProcessarRecebimentoPedidoFornecedorResult(
    string Mensagem,
    int ItensProcessados);

public class ProcessarRecebimentoPedidoFornecedorUseCase(
    IPedidoFornecedorRepository pedidoRepository,
    IPedidoFornecedorItemRepository itemRepository,
    RegistrarEntradaEstoqueUseCase entradaUseCase,
    IUnitOfWork unitOfWork,
    ILogger<ProcessarRecebimentoPedidoFornecedorUseCase> logger,
    IPublicadorEventos? publicadorEventos = null)
{
    public async Task<ProcessarRecebimentoPedidoFornecedorResult> ExecuteAsync(
        ProcessarRecebimentoPedidoFornecedorCommand command,
        CancellationToken ct = default)
    {
        logger.LogInformation(
            "Processando recebimento do pedido {PedidoId} da empresa {EmpresaId}",
            command.PedidoId,
            command.EmpresaId);

        // 1. VALIDAÇÕES
        UseCaseGuards.EnsureNotEmpty(command.PedidoId, nameof(command.PedidoId));
        UseCaseGuards.EnsureEmpresaId(command.EmpresaId);

        var pedido = await pedidoRepository.GetByIdAsync(command.PedidoId)
            ?? throw new UseCaseValidationException("Pedido não encontrado.");

        if (pedido.EmpresaId != command.EmpresaId)
            throw new UseCaseValidationException("Pedido não pertence à empresa.");

        if (pedido.Status == Domain.Enums.StatusPedidoFornecedor.Cancelado)
            throw new RegraDeDominioVioladaException("Pedido cancelado não pode ser recebido.");

        if (pedido.Status == Domain.Enums.StatusPedidoFornecedor.Recebido)
        {
            logger.LogWarning("Pedido {PedidoId} já foi recebido (idempotência)", command.PedidoId);
            return new ProcessarRecebimentoPedidoFornecedorResult("Pedido já recebido", 0);
        }

        var itens = await itemRepository.GetByPedidoIdAsync(command.PedidoId, ct);
        if (!itens.Any())
            throw new UseCaseValidationException("Pedido sem itens.");

        // 2. PROCESSAR ITENS
        var dataRecebimento = command.DataRecebimento ?? DateTime.UtcNow;
        var itensFinal = 0;

        try
        {
            foreach (var item in itens)
            {
                // Skip se quantidade é 0 ou item não existe em command
                if (!command.ItensRecebidos.TryGetValue(item.Id, out var qtdRecebida))
                    qtdRecebida = 0;

                if (qtdRecebida <= 0)
                {
                    logger.LogDebug("Item {ItemId} com qty=0, pulando", item.Id);
                    continue;
                }

                // Validar produto (obrigatório para criar entrada)
                if (!item.ProdutoId.HasValue)
                {
                    logger.LogWarning("Item {ItemId} sem ProdutoId, pula entrada estoque", item.Id);
                    item.QuantidadeRecebida = qtdRecebida;
                    await itemRepository.UpdateAsync(item, ct);
                    continue;
                }

                // Cria entrada de estoque (REUTILIZA RegistrarEntradaEstoqueUseCase)
                var entradaCmd = new RegistrarEntradaEstoqueCommand(
                    EmpresaId: command.EmpresaId,
                    ProdutoId: item.ProdutoId.Value,
                    ProdutoVariacaoId: null,
                    Quantidade: (int)qtdRecebida,
                    CustoUnitario: item.CustoUnitario,
                    PrecoVendaSugerido: null,
                    DataEntrada: dataRecebimento,
                    Natureza: NaturezaMovimentacaoEstoque.Compra,
                    CodigoInterno: null,
                    CodigoLote: null,
                    CodigoMarketplace: null,
                    VariacaoDescricao: null,
                    Cor: null,
                    Tamanho: null,
                    FornecedorNome: pedido.Fornecedor?.Nome ?? "Desconhecido",
                    Validade: null,
                    Observacoes: item.Observacao,
                    DescricaoAnuncio: null,
                    DocumentoReferencia: $"{command.PedidoId}:{item.Id}", // IDEMPOTÊNCIA
                    DimensoesReais: null,
                    InstrucoesGeracaoDescricao: null,
                    LojaId: pedido.LojaId);

                try
                {
                    var resultadoEntrada = await entradaUseCase.ExecuteAsync(entradaCmd);

                    // Atualiza quantidade recebida no item
                    item.QuantidadeRecebida = qtdRecebida;
                    await itemRepository.UpdateAsync(item, ct);

                    // Publica evento item recebido
                    if (publicadorEventos != null)
                    {
                        var eventoItem = new PedidoFornecedorItemRecebido(
                            EventoId: Guid.NewGuid(),
                            OcorridoEm: dataRecebimento,
                            PedidoFornecedorId: command.PedidoId,
                            ItemId: item.Id,
                            ProdutoId: item.ProdutoId.Value,
                            EmpresaId: command.EmpresaId,
                            QuantidadeRecebida: qtdRecebida,
                            DataRecebimento: dataRecebimento);

                        await publicadorEventos.PublicarAsync(eventoItem);
                    }

                    itensFinal++;
                }
                catch (Exception ex)
                {
                    logger.LogError(
                        ex,
                        "Falha ao criar entrada para item {ItemId}: {Erro}",
                        item.Id,
                        ex.Message);
                    throw;
                }
            }

            // 3. ATUALIZAR PEDIDO
            pedido.Status = Domain.Enums.StatusPedidoFornecedor.Recebido;
            pedido.DataRecebimento = dataRecebimento;
            pedido.AlteradoEm = DateTime.UtcNow;
            await pedidoRepository.UpdateAsync(pedido);

            // 4. COMMIT TRANSAÇÃO
            await unitOfWork.CommitAsync();

            // 5. PUBLICAR EVENTO PEDIDO RECEBIDO
            if (publicadorEventos != null)
            {
                var eventoPedido = new PedidoFornecedorRecebido(
                    EventoId: Guid.NewGuid(),
                    OcorridoEm: dataRecebimento,
                    PedidoId: command.PedidoId,
                    EmpresaId: command.EmpresaId,
                    FornecedorId: pedido.FornecedorId,
                    TotalItensRecebidos: itensFinal,
                    DataRecebimento: dataRecebimento);

                await publicadorEventos.PublicarAsync(eventoPedido);
            }

            logger.LogInformation(
                "Pedido {PedidoId} recebido com sucesso ({ItensRecebidos} itens)",
                command.PedidoId,
                itensFinal);

            return new ProcessarRecebimentoPedidoFornecedorResult(
                $"Pedido recebido: {itensFinal} itens processados",
                itensFinal);
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Erro ao processar recebimento do pedido {PedidoId}", command.PedidoId);
            throw;
        }
    }
}
