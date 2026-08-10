using EasyStock.Application.Ports.Output.Events;
using EasyStock.Application.UseCases.RegistrarEntradaEstoque;
using EasyStock.Domain.Events;

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
    RegistrarEntradaEstoqueUseCase entradaUseCase,
    IMovimentacaoEstoqueRepository movimentacaoRepository,
    IUnitOfWork unitOfWork,
    ILogger<ProcessarRecebimentoPedidoFornecedorUseCase> logger,
    IPublicadorEventos? publicadorEventos = null)
{
    // #306: o publicador e opcional na assinatura (DI o registra), mas se um evento
    // precisa ser publicado e ele esta null, falhamos explicito em vez de descartar
    // o evento em silencio. Retorna nao-null para o null-analysis do compilador.
    private IPublicadorEventos PublicadorObrigatorio() =>
        publicadorEventos ?? throw new InvalidOperationException(
            "IPublicadorEventos nao injetado: eventos de ProcessarRecebimentoPedidoFornecedor seriam perdidos silenciosamente (#306).");

    public Task<ProcessarRecebimentoPedidoFornecedorResult> ExecuteAsync(
        ProcessarRecebimentoPedidoFornecedorCommand command,
        CancellationToken ct = default)
    {
        logger.LogInformation(
            "Processando recebimento do pedido {PedidoId} da empresa {EmpresaId}",
            command.PedidoId,
            command.EmpresaId);

        // #1019: transacao explicita garante atomicidade. RegistrarEntradaEstoqueUseCase
        // chama CommitAsync (= SaveChanges) internamente; dentro de uma transacao aberta
        // isso e apenas flush — o commit real e feito aqui no fim. Se qualquer item falhar,
        // rollback reverte entradas de estoque ja salvas + atualizacoes de QuantidadeRecebida.
        return unitOfWork.ExecuteInTransactionSemRetryAsync(
            token => ExecutarDentroDaTransacaoAsync(command, token), ct);
    }

    private async Task<ProcessarRecebimentoPedidoFornecedorResult> ExecutarDentroDaTransacaoAsync(
        ProcessarRecebimentoPedidoFornecedorCommand command,
        CancellationToken ct)
    {
        // 1. VALIDAÇÕES + CARREGAR AGREGADO
        UseCaseGuards.EnsureNotEmpty(command.PedidoId, nameof(command.PedidoId));
        UseCaseGuards.EnsureEmpresaId(command.EmpresaId);

        var pedido = await pedidoRepository.GetByIdWithItensAsync(command.PedidoId, ct)
            ?? throw new UseCaseValidationException("Pedido não encontrado.");

        if (pedido.EmpresaId != command.EmpresaId)
            throw new UseCaseValidationException("Pedido não pertence à empresa.");

        if (pedido.Status == Domain.Enums.StatusPedidoFornecedor.Cancelado)
            throw new RegraDeDominioVioladaException("Pedido cancelado não pode ser recebido.");

        if (pedido.Status == Domain.Enums.StatusPedidoFornecedor.Recebido)
        {
            logger.LogWarning("Pedido {PedidoId} já foi totalmente recebido (idempotência)", command.PedidoId);
            return new ProcessarRecebimentoPedidoFornecedorResult("Pedido já recebido", 0);
        }

        var itens = pedido.Itens.ToList();
        if (!itens.Any())
            throw new UseCaseValidationException("Pedido sem itens.");

        // 2. PROCESSAR ITENS
        var dataRecebimento = command.DataRecebimento ?? DateTime.UtcNow;
        var itensFinal = 0;

        foreach (var item in itens)
        {
            // command.ItensRecebidos[itemId] e o NOVO TOTAL absoluto recebido
            // (nao o delta). Permite multiplos recebimentos: cliente envia
            // sucessivamente 3, 7, 10 ate fechar quantidade pedida.
            if (!command.ItensRecebidos.TryGetValue(item.Id, out var novoTotalRecebido))
                novoTotalRecebido = item.QuantidadeRecebida;

            if (novoTotalRecebido < item.QuantidadeRecebida)
            {
                logger.LogWarning(
                    "Item {ItemId} novoTotal ({Novo}) menor que ja recebido ({Anterior}); estorno nao suportado, pulando.",
                    item.Id, novoTotalRecebido, item.QuantidadeRecebida);
                continue;
            }

            var delta = novoTotalRecebido - item.QuantidadeRecebida;
            if (delta <= 0)
            {
                logger.LogDebug("Item {ItemId} sem delta a aplicar, pulando.", item.Id);
                continue;
            }

            // Validar produto (obrigatório para criar entrada)
            if (!item.ProdutoId.HasValue)
            {
                logger.LogWarning("Item {ItemId} sem ProdutoId, pula entrada estoque", item.Id);
                item.QuantidadeRecebida = novoTotalRecebido;
                continue;
            }

            // Estoque atual exige int. Quantidade fracionaria (1.5kg) nao
            // entra ate refactor que troque RegistrarEntradaEstoqueCommand
            // pra decimal. Falhar explicito evita o cast (int) silencioso
            // que truncava 1.5 -> 1 e gerava saldo errado em compra.
            if (delta % 1m != 0m)
                throw new UseCaseValidationException(
                    $"Quantidade fracionaria ({delta}) ainda nao suportada no recebimento. " +
                    "Cadastre delta inteiro ou aguarde suporte a decimal no estoque.");

            // IDEMPOTENCIA (#790): refDoc inclui novoTotalRecebido, entao cada delta e
            // uma chave unica. Se uma execucao anterior falhou DEPOIS de commitar esta
            // entrada mas ANTES de persistir QuantidadeRecebida, o retry chegaria aqui
            // com a MESMA refDoc — checamos e NAO recriamos a entrada (senao dobra o
            // estoque). So garantimos o total recebido persistido e seguimos. O indice
            // unico parcial ux_mov_estoque_referencia e o backstop contra retries concorrentes.
            var refDoc = $"{command.PedidoId}:{item.Id}:r{novoTotalRecebido}";
            if (await movimentacaoRepository.ExisteReferenciaAsync(
                    command.EmpresaId, item.ProdutoId.Value, refDoc, NaturezaMovimentacaoEstoque.Compra, ct))
            {
                logger.LogInformation(
                    "Item {ItemId} (ref {Ref}) ja aplicado — idempotencia, pulando entrada e persistindo total.",
                    item.Id, refDoc);
                item.QuantidadeRecebida = novoTotalRecebido;
                continue;
            }

            // Cria entrada de estoque (REUTILIZA RegistrarEntradaEstoqueUseCase)
            var entradaCmd = new RegistrarEntradaEstoqueCommand(
                EmpresaId: command.EmpresaId,
                ProdutoId: item.ProdutoId.Value,
                ProdutoVariacaoId: null,
                Quantidade: (int)delta,
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
                // Mesma refDoc do check de idempotencia acima (#790).
                DocumentoReferencia: refDoc,
                DimensoesReais: null,
                InstrucoesGeracaoDescricao: null,
                LojaId: pedido.LojaId);

            var resultadoEntrada = await entradaUseCase.ExecuteAsync(entradaCmd);

            // Atualiza quantidade recebida no item — total absoluto agora.
            item.QuantidadeRecebida = novoTotalRecebido;

            // Publica evento item recebido — QuantidadeRecebida no evento
            // e o DELTA aplicado nesta chamada (compativel com listeners
            // existentes que esperam "qty desta entrada", nao total).
            var eventoItem = new PedidoFornecedorItemRecebido(
                EventoId: Guid.NewGuid(),
                OcorridoEm: dataRecebimento,
                PedidoFornecedorId: command.PedidoId,
                ItemId: item.Id,
                ProdutoId: item.ProdutoId.Value,
                EmpresaId: command.EmpresaId,
                QuantidadeRecebida: delta,
                DataRecebimento: dataRecebimento);

            await PublicadorObrigatorio().PublicarAsync(eventoItem);

            itensFinal++;
        }

        // 3. ATUALIZAR PEDIDO — total absoluto vs pedido determina parcial/total.
        var totalPedido = itens.Sum(i => i.Quantidade);
        var totalRecebidoApos = itens.Sum(i => i.QuantidadeRecebida);
        pedido.Status = totalRecebidoApos >= totalPedido
            ? Domain.Enums.StatusPedidoFornecedor.Recebido
            : Domain.Enums.StatusPedidoFornecedor.RecebidoParcial;
        pedido.DataRecebimento = dataRecebimento;
        pedido.AlteradoEm = DateTime.UtcNow;
        await pedidoRepository.UpdateAsync(pedido);
        await unitOfWork.CommitAsync();

        logger.LogInformation(
            "Pedido {PedidoId} status apos recebimento: {Status} (recebido {Recebido} de {Pedido})",
            command.PedidoId, pedido.Status, totalRecebidoApos, totalPedido);

        // 4. PUBLICAR EVENTO PEDIDO RECEBIDO (apos persistencia bem-sucedida).
        var eventoPedido = new PedidoFornecedorRecebido(
            EventoId: Guid.NewGuid(),
            OcorridoEm: dataRecebimento,
            PedidoId: command.PedidoId,
            EmpresaId: command.EmpresaId,
            FornecedorId: pedido.FornecedorId,
            TotalItensRecebidos: itensFinal,
            DataRecebimento: dataRecebimento);

        await PublicadorObrigatorio().PublicarAsync(eventoPedido);

        logger.LogInformation(
            "Pedido {PedidoId} recebido com sucesso ({ItensRecebidos} itens)",
            command.PedidoId,
            itensFinal);

        return new ProcessarRecebimentoPedidoFornecedorResult(
            $"Pedido recebido: {itensFinal} itens processados",
            itensFinal);
    }
}
