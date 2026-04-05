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
        var empresas = await empresaRepository.GetAllAsync();
        foreach (var empresa in empresas)
        {
            if (ct.IsCancellationRequested)
                break;

            await ProcessarEmpresaAsync(empresa, ct);
        }

        await unitOfWork.CommitAsync();
        logger.LogInformation("Geracao automatica de notificacoes concluida para {TotalEmpresas} empresa(s).", empresas.Count());
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
                    item => CriarSeNaoExisteNoDiaAsync(
                        empresa.Id,
                        TipoAlertaEstoque.EstoqueCritico,
                        item.Id,
                        $"Estoque critico: item '{item.CodigoInterno ?? item.Id.ToString()}' com {item.QuantidadeAtual.Value} unidade(s)."),
                    ct);
            }

            if (configuracaoLoja.NotificarValidade)
            {
                await ProcessarPaginadoAsync(
                    page => estoqueRepository.GetProximoVencimentoAsync(empresa.Id, configuracaoLoja.DiasAlertaValidade, page, 100, loja.Id),
                    item =>
                    {
                        var diasRestantes = item.ValidadeEm?.DiasAteVencimento() ?? 0;
                        return CriarSeNaoExisteNoDiaAsync(
                            empresa.Id,
                            TipoAlertaEstoque.ValidadeProxima,
                            item.Id,
                            $"Validade proxima: '{item.CodigoInterno ?? item.Id.ToString()}' vence em {diasRestantes} dia(s).");
                    },
                    ct);
            }

            if (configuracaoLoja.NotificarParado)
            {
                await ProcessarPaginadoAsync(
                    page => estoqueRepository.GetItensParadosAsync(empresa.Id, configuracaoLoja.DiasAlertaParado, page, 100, loja.Id),
                    item => CriarSeNaoExisteNoDiaAsync(
                        empresa.Id,
                        TipoAlertaEstoque.ProdutoParado,
                        item.Id,
                        $"Produto parado ha mais de {configuracaoLoja.DiasAlertaParado} dias: '{item.CodigoInterno ?? item.Id.ToString()}'."),
                    ct);
            }

            if (configuracaoLoja.NotificarReposicao)
            {
                await ProcessarPaginadoAsync(
                    page => estoqueRepository.GetSugestaoReposicaoAsync(empresa.Id, configuracaoLoja.QuantidadeMinimaPadrao, page, 100, loja.Id),
                    item =>
                    {
                        var previsao = item.PrevisaoZeramentoDias?.ToString() ?? "indefinida";
                        return CriarSeNaoExisteNoDiaAsync(
                            empresa.Id,
                            TipoAlertaEstoque.ReposicaoSugerida,
                            item.Id,
                            $"Reposicao sugerida para '{item.CodigoInterno ?? item.Id.ToString()}'. Previsao de zeramento: {previsao}.");
                    },
                    ct);
            }
        }

        var pedidosAtrasados = await pedidoFornecedorRepository.GetPedidosAtrasadosAsync(empresa.Id, hoje);
        foreach (var pedido in pedidosAtrasados)
        {
            await CriarSeNaoExisteNoDiaAsync(
                empresa.Id,
                TipoAlertaEstoque.PedidoAtrasado,
                pedido.Id,
                $"Pedido atrasado: {pedido.Id} com previsao em {(pedido.PrevisaoEntrega?.ToString("yyyy-MM-dd") ?? "data nao informada")}.");
        }

        var pedidosRecebidos = await pedidoFornecedorRepository.GetPedidosRecebidosNoPeriodoAsync(empresa.Id, hoje.AddHours(-1), hoje);
        foreach (var pedido in pedidosRecebidos)
        {
            await CriarSeNaoExisteNoDiaAsync(
                empresa.Id,
                TipoAlertaEstoque.PedidoRecebido,
                pedido.Id,
                $"Pedido recebido: {pedido.Id} confirmado em {pedido.DataRecebimento:yyyy-MM-dd HH:mm}.");
        }
    }

    private async Task CriarSeNaoExisteNoDiaAsync(Guid empresaId, TipoAlertaEstoque tipo, Guid? referenciaId, string mensagem)
    {
        var jaExiste = await notificacaoRepository.ExisteNotificacaoDoDiaAsync(empresaId, tipo, referenciaId, DateTime.UtcNow);
        if (jaExiste)
            return;

        await notificacaoRepository.AddAsync(Notificacao.Criar(empresaId, tipo, mensagem, referenciaId));
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
