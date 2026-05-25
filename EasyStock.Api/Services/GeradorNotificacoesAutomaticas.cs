using EasyStock.Application.Ports.Output.Persistence;
using EasyStock.Domain.Entities;
using EasyStock.Domain.Enums;

namespace EasyStock.Api.Services;

public sealed class GeradorNotificacoesAutomaticas(
    IEmpresaRepository empresaRepository,
    ILojaRepository lojaRepository,
    IConfiguracaoLojaRepository configuracaoLojaRepository,
    IItemEstoqueRepository estoqueRepository,
    INotificacaoRepository notificacaoRepository,
    IPedidoFornecedorRepository pedidoFornecedorRepository,
    IUnitOfWork unitOfWork,
    ILogger<GeradorNotificacoesAutomaticas> logger)
{
    public async Task ExecutarAsync(CancellationToken ct = default)
    {
        var total = 0;
        // Stream evita carregar todas as empresas em memoria — em prod multi-tenant
        // a tabela cresce indefinidamente e GetAllAsync seria O(n) memoria.
        await foreach (var empresa in empresaRepository.StreamAllAsync(ct))
        {
            if (ct.IsCancellationRequested)
                break;

            await ProcessarEmpresaAsync(empresa, ct);
            await unitOfWork.CommitAsync();
            total++;
        }

        logger.LogInformation("Geração automática de notificações concluída para {TotalEmpresas} empresa(s).", total);
    }

    private async Task ProcessarEmpresaAsync(Empresa empresa, CancellationToken ct)
    {
        var hoje = DateTime.UtcNow;
        var lojas = (await lojaRepository.GetByEmpresaAsync(empresa.Id)).Where(x => x.Ativa).ToList();

        foreach (var loja in lojas)
        {
            if (ct.IsCancellationRequested)
                return;

            var configuracaoLoja = await configuracaoLojaRepository.GetOrDefaultAsync(loja.Id);

            if (configuracaoLoja.NotificarEstoqueCritico)
            {
                await ProcessarPaginadoAsync(
                    page => estoqueRepository.GetEstoqueBaixoAsync(empresa.Id, Math.Max(2, configuracaoLoja.QuantidadeMinimaPadrao), page, 100, loja.Id),
                    item =>
                    {
                        var qty = item.QuantidadeAtual.Value;
                        var severidade = qty <= 2 ? SeveridadeNotificacao.Critica : SeveridadeNotificacao.Alta;
                        var codigo = item.CodigoInterno ?? item.Id.ToString()[..8];
                        return CriarSeNaoExisteNoDiaAsync(
                            empresa.Id,
                            TipoAlertaEstoque.EstoqueCritico,
                            "Estoque Crítico",
                            $"{codigo} com apenas {qty} unidade(s) — minimo configurado: {configuracaoLoja.QuantidadeMinimaPadrao}. Considere repor este item.",
                            severidade,
                            item.Id);
                    },
                    ct);
            }

            if (configuracaoLoja.NotificarValidade)
            {
                // Itens proximos do vencimento (ainda nao vencidos)
                await ProcessarPaginadoAsync(
                    page => estoqueRepository.GetProximoVencimentoAsync(empresa.Id, configuracaoLoja.DiasAlertaValidade, page, 100, loja.Id),
                    item =>
                    {
                        var diasRestantes = item.ValidadeEm?.DiasAteVencimento() ?? 0;
                        if (diasRestantes < 0)
                        {
                            // Ja vencido — gera ProdutoVencido
                            var codigoV = item.CodigoInterno ?? item.Id.ToString()[..8];
                            return CriarSeNaoExisteNoDiaAsync(
                                empresa.Id,
                                TipoAlertaEstoque.ProdutoVencido,
                                "Produto Vencido",
                                $"{codigoV} venceu há {Math.Abs(diasRestantes)} dia(s). Retire do estoque ou descarte conforme procedimento.",
                                SeveridadeNotificacao.Critica,
                                item.Id);
                        }

                        var severidade = diasRestantes <= 3 ? SeveridadeNotificacao.Alta : SeveridadeNotificacao.Media;
                        var codigo = item.CodigoInterno ?? item.Id.ToString()[..8];
                        var dataValidade = item.ValidadeEm!.DataValidade.ToString("dd/MM/yyyy");
                        var prazoTexto = diasRestantes switch
                        {
                            0 => "vence hoje",
                            1 => "vence amanhã",
                            _ => $"vence em {diasRestantes} dia(s)"
                        };
                        return CriarSeNaoExisteNoDiaAsync(
                            empresa.Id,
                            TipoAlertaEstoque.ValidadeProxima,
                            "Validade Próxima",
                            $"{codigo} {prazoTexto} ({dataValidade}). {qty_context(item)} Priorize a venda ou rotatividade.",
                            severidade,
                            item.Id);
                    },
                    ct);
            }

            if (configuracaoLoja.NotificarParado)
            {
                await ProcessarPaginadoAsync(
                    page => estoqueRepository.GetItensParadosAsync(empresa.Id, configuracaoLoja.DiasAlertaParado, page, 100, loja.Id),
                    item =>
                    {
                        var dias = item.DiasSemMovimentacao > 0 ? item.DiasSemMovimentacao : configuracaoLoja.DiasAlertaParado;
                        var codigo = item.CodigoInterno ?? item.Id.ToString()[..8];
                        return CriarSeNaoExisteNoDiaAsync(
                            empresa.Id,
                            TipoAlertaEstoque.ProdutoParado,
                            "Produto Parado",
                            $"{codigo} sem movimentação há {dias} dias. {qty_context(item)} Avalie promoção ou reposicionamento.",
                            SeveridadeNotificacao.Media,
                            item.Id);
                    },
                    ct);
            }

            if (configuracaoLoja.NotificarReposicao)
            {
                await ProcessarPaginadoAsync(
                    page => estoqueRepository.GetSugestaoReposicaoAsync(empresa.Id, configuracaoLoja.QuantidadeMinimaPadrao, page, 100, loja.Id),
                    item =>
                    {
                        var previsao = item.PrevisaoZeramentoDias?.ToString() ?? "indefinida";
                        var codigo = item.CodigoInterno ?? item.Id.ToString()[..8];
                        return CriarSeNaoExisteNoDiaAsync(
                            empresa.Id,
                            TipoAlertaEstoque.ReposicaoSugerida,
                            "Reposição Sugerida",
                            $"{codigo} precisa de reposição. {qty_context(item)} Previsão de zeramento: {previsao} dia(s).",
                            SeveridadeNotificacao.Media,
                            item.Id);
                    },
                    ct);
            }
        }

        var pedidosAtrasados = await pedidoFornecedorRepository.GetPedidosAtrasadosAsync(empresa.Id, hoje);
        foreach (var pedido in pedidosAtrasados)
        {
            var previsao = pedido.PrevisaoEntrega?.ToString("dd/MM/yyyy") ?? "não informada";
            await CriarSeNaoExisteNoDiaAsync(
                empresa.Id,
                TipoAlertaEstoque.PedidoAtrasado,
                "Pedido Atrasado",
                $"Pedido com previsão em {previsao} ainda não foi recebido. Entre em contato com o fornecedor.",
                SeveridadeNotificacao.Alta,
                pedido.Id);
        }

        var pedidosRecebidos = await pedidoFornecedorRepository.GetPedidosRecebidosNoPeriodoAsync(empresa.Id, hoje.AddHours(-1), hoje);
        foreach (var pedido in pedidosRecebidos)
        {
            await CriarSeNaoExisteNoDiaAsync(
                empresa.Id,
                TipoAlertaEstoque.PedidoRecebido,
                "Pedido Recebido",
                $"Pedido confirmado em {pedido.DataRecebimento:dd/MM/yyyy HH:mm}. Confira os itens recebidos.",
                SeveridadeNotificacao.Informativa,
                pedido.Id);
        }
    }

    private static string qty_context(ItemEstoque item)
    {
        var qty = item.QuantidadeAtual.Value;
        return qty > 0 ? $"Estoque atual: {qty} un." : "Estoque zerado.";
    }

    private async Task CriarSeNaoExisteNoDiaAsync(
        Guid empresaId,
        TipoAlertaEstoque tipo,
        string titulo,
        string mensagem,
        SeveridadeNotificacao severidade,
        Guid? referenciaId)
    {
        var jaExiste = await notificacaoRepository.ExisteNotificacaoDoDiaAsync(empresaId, tipo, referenciaId, DateTime.UtcNow);
        if (jaExiste)
            return;

        await notificacaoRepository.AddAsync(
            Notificacao.Criar(empresaId, tipo, titulo, mensagem, severidade, referenciaId));
    }

    private static async Task ProcessarPaginadoAsync(
        Func<int, Task<(IEnumerable<ItemEstoque> Items, int TotalCount)>> carregarPagina,
        Func<ItemEstoque, Task> processarItem,
        CancellationToken ct)
    {
        const int pageSize = 100;
        var page = 1;

        while (!ct.IsCancellationRequested)
        {
            var (items, totalCount) = await carregarPagina(page);
            var paginaAtual = items.ToList();

            foreach (var item in paginaAtual)
            {
                if (ct.IsCancellationRequested)
                    return;

                await processarItem(item);
            }

            if (paginaAtual.Count == 0 || page * pageSize >= totalCount)
                break;

            page++;
        }
    }
}
