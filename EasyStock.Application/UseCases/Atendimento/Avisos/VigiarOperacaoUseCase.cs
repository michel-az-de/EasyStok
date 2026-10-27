using System.Collections.Concurrent;
using System.Globalization;
using System.Text;
using EasyStock.Application.Ports.Output.Notifications;
using EasyStock.Application.Ports.Output.Persistence.Atendimento;
using EasyStock.Application.Services.Atendimento;
using EasyStock.Application.Services.Notifications;
using EasyStock.Domain.Entities.Atendimento;
using EasyStock.Domain.ValueObjects;
using Microsoft.Extensions.Options;

namespace EasyStock.Application.UseCases.Atendimento.Avisos;

/// <summary>O que uma rodada da vigia enfileirou numa empresa.</summary>
public sealed record ResultadoVigiaOperacao(int ProntosSemEntregador, int EncomendasProximas, int EncomendasSemConfirmacao, int ResumosEstoque)
{
    public int Total => ProntosSemEntregador + EncomendasProximas + EncomendasSemConfirmacao + ResumosEstoque;
}

/// <summary>
/// Vigia da operação (#2096), uma rodada por empresa com o tenant ligado (o <c>VigiaOperacaoJob</c> chama a cada minuto).
/// Olha o que nenhum fato do sistema avisa sozinho: pedido pronto parado sem entregador, encomenda chegando, encomenda sem
/// confirmação do cliente e, uma vez por dia, o freezer (perto de vencer e lote parado sem baixa). Cada aviso sai uma vez:
/// a chave de correlação do evento é a trava, consultada antes de enfileirar. Tempo zero na tela Avisos desliga a leitura.
/// </summary>
public sealed class VigiarOperacaoUseCase(
    IConfiguracaoAtendimentoRepository configuracoes,
    IVigiaOperacaoQuery consultas,
    IEventoNotificacaoRepository eventos,
    INotificadorService notificador,
    IUnitOfWork unitOfWork,
    IOptions<PrazosOptions> prazos,
    ILogger<VigiarOperacaoUseCase> logger)
{
    /// <summary>Hora de Brasília a partir da qual sai o resumo do freezer do dia.</summary>
    public const int HoraResumoEstoque = 8;

    public const int MaximoPorLeitura = 50;

    /// <summary>Pedido pronto há mais que isto não vira aviso: é resto parado, não entrega esperando agora.</summary>
    public static readonly TimeSpan JanelaDoPronto = TimeSpan.FromHours(24);
    public const int ItensNoResumo = 8;

    private const string SemNome = "Cliente";

    // Otimização de uma instância: depois de olhar o freezer no dia, não consulta de novo. A trava de verdade é a correlação.
    private static readonly ConcurrentDictionary<Guid, DateOnly> FreezerOlhadoEm = new();

    public async Task<ResultadoVigiaOperacao> ExecuteAsync(Guid empresaId, DateTime agoraUtc, CancellationToken ct = default)
    {
        UseCaseGuards.EnsureEmpresaId(empresaId);
        var opcoes = prazos.Value;
        if (!opcoes.Habilitado) return new ResultadoVigiaOperacao(0, 0, 0, 0);

        var avisos = (await configuracoes.GetOrDefaultAsync(empresaId)).Avisos;
        var prontos = await ProntosSemEntregadorAsync(empresaId, agoraUtc, avisos, opcoes, ct);
        var (proximas, semConfirmacao) = await EncomendasAsync(empresaId, agoraUtc, avisos, opcoes, ct);
        var estoque = await FreezerAsync(empresaId, agoraUtc, avisos, opcoes, ct);

        var resultado = new ResultadoVigiaOperacao(prontos, proximas, semConfirmacao, estoque);
        if (resultado.Total > 0)
        {
            await unitOfWork.CommitAsync();
            // Só depois do commit: uma falha ao gravar não perde o resumo do freezer do dia.
            if (estoque > 0) FreezerOlhadoEm[empresaId] = DateOnly.FromDateTime(HorarioBrasil.ConverterParaBrasilia(agoraUtc));
            logger.LogInformation(
                "Vigia da operação empresa={EmpresaId}: prontos={Prontos} encomendas={Proximas} semConfirmacao={SemConfirmacao} estoque={Estoque}",
                empresaId, prontos, proximas, semConfirmacao, estoque);
        }

        return resultado;
    }

    private async Task<int> ProntosSemEntregadorAsync(
        Guid empresaId, DateTime agora, AvisosOperacao avisos, PrazosOptions opcoes, CancellationToken ct)
    {
        if (avisos.ProntoSemEntregadorMinutos <= 0) return 0;

        var limite = TimeSpan.FromMinutes(avisos.ProntoSemEntregadorMinutos);
        var pedidos = await consultas.ListarProntosSemEntregadorAsync(
            empresaId, agora - JanelaDoPronto, agora - limite, MaximoPorLeitura, ct);
        var enfileirados = 0;
        foreach (var pedido in pedidos)
        {
            var correlacao = PrazoEstouradoEvento.CorrelationId(TipoPrazo.ProntoSemEntregador, pedido.PedidoId);
            if (await eventos.ExisteCorrelacaoAsync(empresaId, correlacao, ct)) continue;

            if (await PrazoEstouradoEvento.EnfileirarAsync(notificador, opcoes, TipoPrazo.ProntoSemEntregador,
                    empresaId, pedido.PedidoId, CodigoCurto.De(pedido.PedidoId),
                    PrazoEstouradoEvento.InstanteTexto(pedido.ProntoDesde + limite),
                    PrazoEstouradoEvento.Duracao(agora - pedido.ProntoDesde), usuarioId: null, ct,
                    cliente: NomesDeContato.PrimeiroNome(pedido.ClienteNome)))
                enfileirados++;
        }

        return enfileirados;
    }

    private async Task<(int Proximas, int SemConfirmacao)> EncomendasAsync(
        Guid empresaId, DateTime agora, AvisosOperacao avisos, PrazosOptions opcoes, CancellationToken ct)
    {
        var horas = Math.Max(avisos.EncomendaProximaHoras, avisos.EncomendaSemConfirmacaoHoras);
        if (horas <= 0) return (0, 0);

        var encomendas = await consultas.ListarEncomendasAsync(empresaId, agora, agora.AddHours(horas), MaximoPorLeitura, ct);
        int proximas = 0, semConfirmacao = 0;
        foreach (var e in encomendas.Where(EhEncomenda))
        {
            var nome = NomesDeContato.PrimeiroNome(e.ClienteNome, SemNome);
            var quando = Quando(e.AgendadoParaEm);
            var sufixo = e.AgendadoParaEm.ToString("yyMMddHHmm", CultureInfo.InvariantCulture);

            if (avisos.EncomendaProximaHoras > 0 && e.AgendadoParaEm <= agora.AddHours(avisos.EncomendaProximaHoras)
                && await AlertaOperacaoEvento.EnfileirarAsync(notificador, eventos, opcoes,
                    CodigosAviso.EncomendaProxima, empresaId, e.PedidoId,
                    titulo: "Encomenda chegando",
                    resumoCurto: $"{nome}: encomenda {quando}",
                    texto: $"A encomenda de {nome} ({CodigoCurto.De(e.PedidoId)}) está marcada para {quando}. " +
                           (e.ConfirmadoPeloClienteEm is null ? "O cliente ainda não confirmou." : "O cliente já confirmou."),
                    ct, sufixo))
                proximas++;

            if (avisos.EncomendaSemConfirmacaoHoras > 0 && e.ConfirmadoPeloClienteEm is null
                && e.AgendadoParaEm <= agora.AddHours(avisos.EncomendaSemConfirmacaoHoras)
                && await AlertaOperacaoEvento.EnfileirarAsync(notificador, eventos, opcoes,
                    CodigosAviso.EncomendaSemConfirmacao, empresaId, e.PedidoId,
                    titulo: "Encomenda sem confirmação",
                    resumoCurto: $"{nome}: confirmar a encomenda {quando}",
                    texto: $"A encomenda de {nome} ({CodigoCurto.De(e.PedidoId)}) para {quando} ainda não foi confirmada " +
                           "pelo cliente. Fale com o cliente e marque 'Cliente confirmou' no pedido.",
                    ct, sufixo))
                semConfirmacao++;
        }

        return (proximas, semConfirmacao);
    }

    private async Task<int> FreezerAsync(
        Guid empresaId, DateTime agora, AvisosOperacao avisos, PrazosOptions opcoes, CancellationToken ct)
    {
        if (!avisos.Ligado(CodigosAviso.EstoqueParaConferir)) return 0;
        var local = HorarioBrasil.ConverterParaBrasilia(agora);
        if (local.Hour < HoraResumoEstoque) return 0;
        var dia = DateOnly.FromDateTime(local);
        if (FreezerOlhadoEm.TryGetValue(empresaId, out var olhado) && olhado == dia) return 0;

        var sufixo = dia.ToString("yyyyMMdd", CultureInfo.InvariantCulture);
        var correlacao = AlertaOperacaoEvento.CorrelationId(CodigosAviso.EstoqueParaConferir, empresaId, sufixo);
        if (await eventos.ExisteCorrelacaoAsync(empresaId, correlacao, ct))
        {
            FreezerOlhadoEm[empresaId] = dia;
            return 0;
        }

        DateTime? venceAte = avisos.EstoqueVencendoDias > 0 ? agora.AddDays(avisos.EstoqueVencendoDias) : null;
        DateTime? produzidoAte = avisos.EstoqueParadoDias > 0 ? agora.AddDays(-avisos.EstoqueParadoDias) : null;
        var itens = await consultas.ListarFreezerParaConferirAsync(empresaId, venceAte, produzidoAte, MaximoPorLeitura, ct);
        if (itens.Count == 0)
        {
            FreezerOlhadoEm[empresaId] = dia;
            return 0;
        }

        var vencendo = itens.Count(i => venceAte is { } v && i.ExpiraEm is { } e && e <= v);
        var parados = itens.Count - vencendo;
        var resumo = (vencendo, parados) switch
        {
            ( > 0, > 0) => $"{Itens(vencendo)} perto de vencer e {Itens(parados)} para conferir no freezer",
            ( > 0, _) => $"{Itens(vencendo)} perto de vencer",
            _ => $"{Itens(parados)} para conferir no freezer",
        };

        return await AlertaOperacaoEvento.EnfileirarAsync(notificador, eventos, opcoes,
            CodigosAviso.EstoqueParaConferir, empresaId, empresaId,
            titulo: "Estoque para conferir", resumoCurto: resumo, texto: TextoFreezer(itens, venceAte, agora),
            ct, sufixo)
            ? 1
            : 0;
    }

    /// <summary>
    /// Encomenda é pedido para outro dia (data de Brasília da entrega depois da data em que foi feito). O site grava a
    /// data em todo pedido; o pedido para hoje já está na cozinha e tem os próprios avisos.
    /// </summary>
    public static bool EhEncomenda(EncomendaEmAberto e) =>
        HorarioBrasil.DataOperacional(e.AgendadoParaEm) > HorarioBrasil.DataOperacional(e.CriadoEm);

    public static string TextoFreezer(IReadOnlyList<ItemFreezerParaConferir> itens, DateTime? venceAte, DateTime agora)
    {
        var texto = new StringBuilder("Confira no freezer e dê baixa no que já saiu. ");
        foreach (var i in itens.Take(ItensNoResumo))
        {
            var unidades = i.Unidades == 1 ? "1 unidade" : $"{i.Unidades} unidades";
            var motivo = venceAte is { } v && i.ExpiraEm is { } e && e <= v
                ? (e <= agora ? $"venceu em {Dia(e)}" : $"vence em {Dia(e)}")
                : $"produzido em {Dia(i.ProduzidoEm)} e sem baixa";
            texto.Append(CultureInfo.InvariantCulture, $"{i.Nome} (lote {i.CodigoLote}): {motivo}, {unidades}. ");
        }

        if (itens.Count > ItensNoResumo)
            texto.Append(CultureInfo.InvariantCulture, $"E mais {itens.Count - ItensNoResumo} na tela de produção.");
        return texto.ToString().Trim();
    }

    private static string Itens(int n) => n == 1 ? "1 item" : $"{n} itens";

    private static string Dia(DateTime utc) =>
        HorarioBrasil.ConverterParaBrasilia(DateTime.SpecifyKind(utc, DateTimeKind.Utc)).ToString("dd/MM", CultureInfo.InvariantCulture);

    private static string Quando(DateTime utc) =>
        HorarioBrasil.ConverterParaBrasilia(DateTime.SpecifyKind(utc, DateTimeKind.Utc))
            .ToString("dd/MM 'às' HH:mm", CultureInfo.InvariantCulture);
}
