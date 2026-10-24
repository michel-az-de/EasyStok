using EasyStock.Application.Common;
using EasyStock.Application.UseCases.Analytics.Reposicao;
using EasyStock.Domain.Reposicao;

namespace EasyStock.Api.Services;

/// <summary>Quanto uma rodada de uma empresa produziu (#2016).</summary>
public sealed record ResultadoAlertasEstoque(int Criados, int LotesMarcadosVencidos);

/// <summary>
/// Alertas de estoque de UMA empresa para o sino da equipe (<c>Notificacao</c>, sem destinatário = toda a equipe):
/// validade próxima e vencida, abaixo do mínimo (crítico/esgotado), reposição sugerida, produto parado e pedido de
/// compra atrasado ou recebido. Também marca como <c>Vencido</c> o lote que passou da validade parado (a coluna só
/// mudava quando alguém tocava no lote).
///
/// <para>
/// O chamador (<see cref="BackgroundServices.AnalisadorEstoqueBackgroundService"/>) abre um escopo de DI por empresa com o
/// tenant ligado (<see cref="BackgroundServices.RodadaPorEmpresa"/>): fora de request o tenant é <c>Guid.Empty</c> e o
/// filtro global e a RLS zeram toda leitura com <c>EmpresaId</c> (#1588). Aqui não há iteração entre empresas.
/// </para>
///
/// <para>
/// Sem repetir: o alerta é um por (tipo, item) por dia de Brasília. As chaves do dia vêm numa consulta só e o que a
/// rodada cria entra no conjunto; a rodada seguinte, no mesmo dia, não cria de novo. Um commit por empresa.
/// </para>
/// </summary>
public sealed class GeradorNotificacoesAutomaticas(
    ILojaRepository lojaRepository,
    IConfiguracaoLojaRepository configuracaoLojaRepository,
    IItemEstoqueRepository estoqueRepository,
    ObterReposicaoUseCase reposicaoUseCase,
    INotificacaoRepository notificacaoRepository,
    IPedidoFornecedorRepository pedidoFornecedorRepository,
    IUnitOfWork unitOfWork,
    TimeProvider relogio,
    ILogger<GeradorNotificacoesAutomaticas> logger)
{
    private const int LimiteDeLotesMarcadosPorRodada = 500;

    private HashSet<(TipoAlertaEstoque Tipo, Guid? ReferenciaId)> alertasDoDia = [];
    private int criados;

    public async Task<ResultadoAlertasEstoque> ExecutarAsync(Guid empresaId, CancellationToken ct = default)
    {
        if (empresaId == Guid.Empty)
            throw new ArgumentException("A empresa é obrigatória: o gerador roda no escopo (tenant) de uma empresa.", nameof(empresaId));

        var agora = relogio.GetUtcNow().UtcDateTime;
        var hojeBrasilia = HorarioBrasil.DataOperacional(agora);
        var (inicioDoDia, fimDoDia) = HorarioBrasil.JanelaDiaUtc(hojeBrasilia);
        alertasDoDia = await notificacaoRepository.GetAlertasDoPeriodoAsync(empresaId, inicioDoDia, fimDoDia, ct);
        criados = 0;

        var marcados = await MarcarLotesVencidosAsync(empresaId, agora, hojeBrasilia, ct);
        await ProcessarEmpresaAsync(empresaId, agora, hojeBrasilia, ct);
        await unitOfWork.CommitAsync();

        logger.LogInformation(
            "Alertas de estoque da empresa {EmpresaId}: {Criados} novo(s), {Vencidos} lote(s) marcado(s) como vencido.",
            empresaId, criados, marcados);
        return new ResultadoAlertasEstoque(criados, marcados);
    }

    /// <summary>
    /// <c>ItemEstoque.Status</c> é coluna materializada: lote que vence parado seguia Ok/Slow/Critical e as telas e
    /// filtros por status o tratavam como vigente (estoque-08). O Domínio decide o status (<c>RecalcularStatus</c>).
    /// </summary>
    private async Task<int> MarcarLotesVencidosAsync(Guid empresaId, DateTime agora, DateOnly hojeBrasilia, CancellationToken ct)
    {
        var lotes = await estoqueRepository.GetVencidosSemStatusAsync(
            empresaId, HorarioBrasil.CivilComoInstanteUtc(hojeBrasilia), LimiteDeLotesMarcadosPorRodada, ct);

        var marcados = 0;
        foreach (var lote in lotes)
        {
            lote.RecalcularStatus(agora);
            if (lote.Status != StatusItemEstoque.Vencido)
                continue;

            lote.AlteradoEm = agora;
            marcados++;
        }

        return marcados;
    }

    private async Task ProcessarEmpresaAsync(Guid empresaId, DateTime hoje, DateOnly hojeBrasilia, CancellationToken ct)
    {
        var lojas = (await lojaRepository.GetByEmpresaAsync(empresaId)).Where(x => x.Ativa).ToList();

        foreach (var loja in lojas)
        {
            if (ct.IsCancellationRequested)
                return;

            var configuracaoLoja = await configuracaoLojaRepository.GetOrDefaultAsync(loja.Id);

            // Fonte única de reposição (ADR-0039 / issue 748): EstoqueCritico e ReposicaoSugerida
            // derivam do MESMO cálculo por-produto (ObterReposicaoUseCase), no lugar de dois
            // predicados por-lote divergentes (GetEstoqueBaixoAsync / GetSugestaoReposicaoAsync).
            var reposicao = configuracaoLoja.NotificarEstoqueCritico || configuracaoLoja.NotificarReposicao
                ? await reposicaoUseCase.ExecuteAsync(new ObterReposicaoCommand(empresaId, loja.Id), ct)
                : (IReadOnlyList<ItemReposicao>)Array.Empty<ItemReposicao>();

            if (configuracaoLoja.NotificarEstoqueCritico)
            {
                foreach (var item in reposicao.Where(i => i.Estado is EstadoReposicao.Esgotado or EstadoReposicao.Critico))
                {
                    var severidade = item.Estado == EstadoReposicao.Esgotado
                        ? SeveridadeNotificacao.Critica
                        : SeveridadeNotificacao.Alta;
                    await CriarSeNaoExisteNoDiaAsync(
                        empresaId,
                        TipoAlertaEstoque.EstoqueCritico,
                        "Estoque Crítico",
                        $"{item.Nome}: {item.Motivo}. Considere repor este item.",
                        severidade,
                        item.ProdutoId);
                }
            }

            if (configuracaoLoja.NotificarValidade)
            {
                // Itens proximos do vencimento (ainda nao vencidos)
                await ProcessarPaginadoAsync(
                    page => estoqueRepository.GetProximoVencimentoAsync(empresaId, configuracaoLoja.DiasAlertaValidade, page, 100, loja.Id, apenasComSaldo: true),
                    item =>
                    {
                        var diasRestantes = item.ValidadeEm?.DiasAteVencimento(hojeBrasilia) ?? 0;
                        if (diasRestantes < 0)
                        {
                            // Ja vencido — gera ProdutoVencido
                            var codigoV = item.CodigoInterno ?? item.Id.ToString()[..8];
                            return CriarSeNaoExisteNoDiaAsync(
                                empresaId,
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
                            empresaId,
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
                    page => estoqueRepository.GetItensParadosAsync(empresaId, configuracaoLoja.DiasAlertaParado, page, 100, loja.Id, apenasComSaldo: true),
                    item =>
                    {
                        var dias = item.DiasSemMovimentacao > 0 ? item.DiasSemMovimentacao : configuracaoLoja.DiasAlertaParado;
                        var codigo = item.CodigoInterno ?? item.Id.ToString()[..8];
                        return CriarSeNaoExisteNoDiaAsync(
                            empresaId,
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
                foreach (var item in reposicao)
                {
                    var previsao = item.DiasAteRuptura?.ToString() ?? "indefinida";
                    var sugestao = item.QuantidadeSugerida.ToString("0.##", System.Globalization.CultureInfo.InvariantCulture);
                    await CriarSeNaoExisteNoDiaAsync(
                        empresaId,
                        TipoAlertaEstoque.ReposicaoSugerida,
                        "Reposição Sugerida",
                        $"{item.Nome} precisa de reposição ({item.Motivo}). Sugestão: {sugestao} un. Previsão de ruptura: {previsao} dia(s).",
                        SeveridadeNotificacao.Media,
                        item.ProdutoId);
                }
            }
        }

        var pedidosAtrasados = await pedidoFornecedorRepository.GetPedidosAtrasadosAsync(empresaId, hoje);
        foreach (var pedido in pedidosAtrasados)
        {
            var previsao = pedido.PrevisaoEntrega?.ToString("dd/MM/yyyy") ?? "não informada";
            await CriarSeNaoExisteNoDiaAsync(
                empresaId,
                TipoAlertaEstoque.PedidoAtrasado,
                "Pedido Atrasado",
                $"Pedido com previsão em {previsao} ainda não foi recebido. Entre em contato com o fornecedor.",
                SeveridadeNotificacao.Alta,
                pedido.Id);
        }

        var pedidosRecebidos = await pedidoFornecedorRepository.GetPedidosRecebidosNoPeriodoAsync(empresaId, hoje.AddHours(-1), hoje);
        foreach (var pedido in pedidosRecebidos)
        {
            await CriarSeNaoExisteNoDiaAsync(
                empresaId,
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
        // Add devolve false quando a chave do dia já está no conjunto (criada antes ou nesta rodada).
        if (!alertasDoDia.Add((tipo, referenciaId)))
            return;

        await notificacaoRepository.AddAsync(
            Notificacao.Criar(empresaId, tipo, titulo, mensagem, severidade, referenciaId));
        criados++;
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
