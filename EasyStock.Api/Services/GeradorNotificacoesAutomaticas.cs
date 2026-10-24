using EasyStock.Api.Configuration;
using EasyStock.Application.Common;
using EasyStock.Application.UseCases.Analytics.Reposicao;
using EasyStock.Domain.Reposicao;
using Microsoft.Extensions.Options;

namespace EasyStock.Api.Services;

/// <summary>Quanto uma rodada de uma empresa produziu (#2016).</summary>
public sealed record ResultadoAlertasEstoque(int Criados, int LotesMarcadosVencidos, int Resolvidos, int Expurgados);

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
/// <b>Alerta por condição, não por dia.</b> A rodada primeiro levanta as condições que valem agora (o conjunto de
/// (tipo, item)); depois, numa só consulta, lê os alertas automáticos que ainda contam: os não lidos (qualquer idade) e os
/// criados nos últimos <see cref="AlertasEstoqueOptions.DiasSemRepetir"/> dias. Só nasce alerta para condição sem alerta
/// vigente. Alerta cuja condição deixou de valer (lote vendido, produto reposto) sai do sino; se a condição voltar, nasce
/// um novo. <c>PedidoRecebido</c> é evento, não condição: só segue a janela.
/// </para>
///
/// <para>
/// <b>Retenção.</b> Na mesma rodada, e só entre os alertas automáticos de estoque da empresa (os avisos com destinatário ou
/// com mensagem do outbox, como o de caixa, ficam de fora), saem os lidos que passaram da janela de repetição e os de
/// qualquer estado com mais de <see cref="AlertasEstoqueOptions.RetencaoDias"/> dias. Um commit por empresa.
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
    IOptions<AlertasEstoqueOptions> opcoes,
    ILogger<GeradorNotificacoesAutomaticas> logger)
{
    private const int LimiteDeLotesMarcadosPorRodada = 500;

    /// <summary>Tipos que este gerador cria. A retenção e a resolução só tocam nestes.</summary>
    public static readonly IReadOnlyList<TipoAlertaEstoque> TiposAutomaticos =
    [
        TipoAlertaEstoque.EstoqueCritico,
        TipoAlertaEstoque.ProdutoParado,
        TipoAlertaEstoque.ValidadeProxima,
        TipoAlertaEstoque.ReposicaoSugerida,
        TipoAlertaEstoque.PedidoAtrasado,
        TipoAlertaEstoque.PedidoRecebido,
        TipoAlertaEstoque.ProdutoVencido,
    ];

    private readonly Dictionary<(TipoAlertaEstoque Tipo, Guid? ReferenciaId), Notificacao> candidatos = [];

    public async Task<ResultadoAlertasEstoque> ExecutarAsync(Guid empresaId, CancellationToken ct = default)
    {
        if (empresaId == Guid.Empty)
            throw new ArgumentException("A empresa é obrigatória: o gerador roda no escopo (tenant) de uma empresa.", nameof(empresaId));

        var agora = relogio.GetUtcNow().UtcDateTime;
        var hojeBrasilia = HorarioBrasil.DataOperacional(agora);
        var diasSemRepetir = Math.Max(1, opcoes.Value.DiasSemRepetir);
        var retencaoDias = Math.Max(diasSemRepetir, opcoes.Value.RetencaoDias);
        var inicioDaJanela = agora.AddDays(-diasSemRepetir);

        var expurgados = await notificacaoRepository.ExpurgarAlertasAutomaticosAsync(
            empresaId, TiposAutomaticos, lidosAntesDeUtc: inicioDaJanela, qualquerAntesDeUtc: agora.AddDays(-retencaoDias), ct);

        var marcados = await MarcarLotesVencidosAsync(empresaId, agora, hojeBrasilia, ct);

        candidatos.Clear();
        await LevantarCondicoesAsync(empresaId, agora, hojeBrasilia, ct);
        // Cancelamento no meio do levantamento deixaria o conjunto incompleto e a resolução apagaria alerta vigente.
        ct.ThrowIfCancellationRequested();

        var existentes = await notificacaoRepository.GetAlertasAutomaticosAsync(empresaId, TiposAutomaticos, inicioDaJanela, ct);

        // Condição que deixou de valer: o alerta sai (e, se voltar, nasce outro). PedidoRecebido é evento, não condição.
        var resolvidos = existentes
            .Where(e => e.Tipo != TipoAlertaEstoque.PedidoRecebido && !candidatos.ContainsKey((e.Tipo, e.ReferenciaId)))
            .ToList();
        var removidos = await notificacaoRepository.RemoverAlertasAsync(empresaId, resolvidos.Select(e => e.Id).ToList(), ct);

        var vigentes = existentes.Except(resolvidos).Select(e => (e.Tipo, e.ReferenciaId)).ToHashSet();
        var criados = 0;
        foreach (var (chave, alerta) in candidatos)
        {
            if (vigentes.Contains(chave))
                continue;

            await notificacaoRepository.AddAsync(alerta);
            criados++;
        }

        await unitOfWork.CommitAsync();

        logger.LogInformation(
            "Alertas de estoque da empresa {EmpresaId}: {Criados} novo(s), {Resolvidos} resolvido(s), {Expurgados} expurgado(s), {Vencidos} lote(s) marcado(s) como vencido.",
            empresaId, criados, removidos, expurgados, marcados);
        return new ResultadoAlertasEstoque(criados, marcados, removidos, expurgados);
    }

    /// <summary>
    /// <c>ItemEstoque.Status</c> é coluna materializada: lote que vence parado seguia Ok/Slow/Critical e as telas e
    /// filtros por status o tratavam como vigente (estoque-08). O Domínio decide o status (<c>RecalcularIndicadores</c>).
    /// </summary>
    private async Task<int> MarcarLotesVencidosAsync(Guid empresaId, DateTime agora, DateOnly hojeBrasilia, CancellationToken ct)
    {
        var lotes = await estoqueRepository.GetVencidosSemStatusAsync(
            empresaId, HorarioBrasil.CivilComoInstanteUtc(hojeBrasilia), LimiteDeLotesMarcadosPorRodada, ct);

        var marcados = 0;
        foreach (var lote in lotes)
        {
            lote.RecalcularIndicadores(agora);
            if (lote.Status != StatusItemEstoque.Vencido)
                continue;

            lote.AlteradoEm = agora;
            marcados++;
        }

        return marcados;
    }

    private async Task LevantarCondicoesAsync(Guid empresaId, DateTime hoje, DateOnly hojeBrasilia, CancellationToken ct)
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
                    Candidatar(
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
                // Itens proximos do vencimento e vencidos, só com saldo
                await ProcessarPaginadoAsync(
                    page => estoqueRepository.GetProximoVencimentoAsync(empresaId, configuracaoLoja.DiasAlertaValidade, page, 100, loja.Id, apenasComSaldo: true),
                    item =>
                    {
                        var diasRestantes = item.ValidadeEm?.DiasAteVencimento(hojeBrasilia) ?? 0;
                        if (diasRestantes < 0)
                        {
                            // Ja vencido — gera ProdutoVencido
                            var codigoV = item.CodigoInterno ?? item.Id.ToString()[..8];
                            Candidatar(
                                empresaId,
                                TipoAlertaEstoque.ProdutoVencido,
                                "Produto Vencido",
                                $"{codigoV} venceu há {Math.Abs(diasRestantes)} dia(s). Retire do estoque ou descarte conforme procedimento.",
                                SeveridadeNotificacao.Critica,
                                item.Id);
                            return;
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
                        Candidatar(
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
                        Candidatar(
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
                    Candidatar(
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
            Candidatar(
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
            Candidatar(
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

    /// <summary>Registra a condição que vale agora. Fica a primeira do (tipo, item) quando a mesma vem de duas lojas.</summary>
    private void Candidatar(
        Guid empresaId,
        TipoAlertaEstoque tipo,
        string titulo,
        string mensagem,
        SeveridadeNotificacao severidade,
        Guid? referenciaId) =>
        candidatos.TryAdd((tipo, referenciaId), Notificacao.Criar(empresaId, tipo, titulo, mensagem, severidade, referenciaId));

    private static async Task ProcessarPaginadoAsync(
        Func<int, Task<(IEnumerable<ItemEstoque> Items, int TotalCount)>> carregarPagina,
        Action<ItemEstoque> processarItem,
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

                processarItem(item);
            }

            if (paginaAtual.Count == 0 || page * pageSize >= totalCount)
                break;

            page++;
        }
    }
}
