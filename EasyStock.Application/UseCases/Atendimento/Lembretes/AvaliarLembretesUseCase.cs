using System.Text.Json;
using EasyStock.Application.Ports.Output.Atendimento;
using EasyStock.Application.Ports.Output.Notifications;
using EasyStock.Application.Ports.Output.Persistence.Atendimento;
using EasyStock.Application.Services.Notifications;
using EasyStock.Domain.Entities.Atendimento;
using EasyStock.Domain.Entities.Storefront;
using EasyStock.Domain.Enums.Atendimento;
using EasyStock.Domain.Enums.Notifications;
using Microsoft.Extensions.Options;

namespace EasyStock.Application.UseCases.Atendimento.Lembretes;

public sealed record ResultadoAvaliacaoLembretes(int Criados, int Resolvidos, int Avisados);

/// <summary>
/// Avaliador dos lembretes da dona (S43), uma rodada por minuto (cross-tenant: o host liga o bypass
/// de RLS e segura o advisory lock, então só um processo avalia por vez).
///
/// <list type="number">
///   <item>Cria os automáticos a partir das consultas, uma vez por fato <c>(Tipo, Referencia)</c>
///   (o índice único no banco é a segunda trava).</item>
///   <item>Resolve sozinho o automático aberto cujo fato saiu da consulta: o pagamento baixou, a
///   dona respondeu, a conversa foi encerrada ou devolvida ao agente.</item>
///   <item>Avisa os vencidos e ainda não avisados (o automático vence na hora; o manual, no horário
///   escolhido): evento <see cref="TipoEventoNotificacao.LembreteVencido"/> no outbox (Web Push,
///   ADR-0030) no mesmo commit do carimbo, e SSE <see cref="EventoSse"/> depois do commit.</item>
/// </list>
/// Nada sai para o cliente.
/// </summary>
public sealed class AvaliarLembretesUseCase(
    ILembreteRepository repository,
    ICandidatosLembreteQuery candidatos,
    INotificadorService notificador,
    IOperacaoEventPublisher publisher,
    IUnitOfWork unitOfWork,
    TimeProvider relogio,
    IOptions<PrazosOptions> prazos)
{
    public static readonly TimeSpan PagamentoSemBaixaApos = TimeSpan.FromMinutes(15);
    /// <summary>
    /// Padrão do limite de cliente sem resposta quando a loja não gravou configuração; a fonte é
    /// <see cref="PrazosOptions.ClienteSemRespostaMin"/> (N11). Com configuração, vale o SLA da loja (#1427).
    /// </summary>
    public static readonly TimeSpan SemRespostaApos = TimeSpan.FromMinutes(10);
    /// <summary>
    /// Por quanto tempo o início previsto do preparo ainda é notícia (#1550). Depois disso o marco sai de
    /// vigência: pedido antigo com início no passado não gera aviso quando o avaliador liga.
    /// </summary>
    public static readonly TimeSpan InicioPrevistoValePor = TimeSpan.FromHours(24);
    public const string EventoSse = "lembrete.vencido";
    public const int AvisosPorRodada = 100;

    public async Task<ResultadoAvaliacaoLembretes> ExecuteAsync(CancellationToken ct = default)
    {
        var agora = relogio.GetUtcNow().UtcDateTime;
        var vigentes = new HashSet<(Guid EmpresaId, TipoLembrete Tipo, string Referencia)>();
        var criados = 0;

        // #1564: cada bloco junta os fatos que já venceram e pergunta ao banco uma vez só quais já têm lembrete.
        var agendados = new List<(PedidoAgendadoLembrete Pedido, string Referencia, string Texto)>();
        foreach (var pedido in await candidatos.ListarPedidosAgendadosAsync(ct))
        {
            var expediente = pedido.Expediente ?? ExpedienteLoja.CriarPadrao(pedido.EmpresaId);
            if (expediente.EmpresaId != pedido.EmpresaId) continue;
            var diaEntrega = HorarioBrasil.DataOperacional(pedido.EntregaEm);
            var vespera = diaEntrega.AddDays(-1);
            var turno = expediente.Horarios.FirstOrDefault(h => h.DiaDaSemana == (int)vespera.DayOfWeek);
            var quem = string.IsNullOrWhiteSpace(pedido.ClienteNome) ? "Pedido agendado" : $"Pedido de {pedido.ClienteNome.Trim()}";
            // #1550: passada a véspera, "entrega amanhã" é falso. O marco sai de vigência e o aviso aberto
            // fecha sozinho; não renasce porque só nasce na própria véspera e a referência já existe.
            var hoje = HorarioBrasil.DataOperacional(agora);
            if (turno is not null && hoje <= vespera)
                MarcarAgendado(pedido, $"vespera:{diaEntrega:yyyyMMdd}",
                    $"{quem}: entrega amanhã, {HorarioBrasil.ConverterParaBrasilia(pedido.EntregaEm):dd/MM 'às' HH:mm}.",
                    agora >= HorarioBrasil.InstanteUtc(vespera, turno.Abre) && hoje == vespera,
                    vigentes, agendados);
            // #1550: o início previsto é aviso para quem ainda não começou. Com o preparo iniciado, ou com o
            // início velho demais para ser notícia, o marco sai de vigência e o aviso aberto fecha sozinho.
            if (pedido.InicioPrevistoEm is { } inicio && !pedido.PreparoIniciado && agora - inicio <= InicioPrevistoValePor)
                MarcarAgendado(pedido, $"inicio:{inicio:yyyyMMddHHmmss}",
                    $"{quem}: início previsto do preparo em {HorarioBrasil.ConverterParaBrasilia(inicio):dd/MM 'às' HH:mm}.",
                    agora >= inicio, vigentes, agendados);
        }
        var agendadosExistentes = await ExistentesAsync(
            TipoLembrete.PedidoAgendado, agendados.Select(a => (a.Pedido.EmpresaId, a.Referencia)), ct);
        foreach (var (pedido, referencia, texto) in agendados)
        {
            if (!agendadosExistentes.Add((pedido.EmpresaId, referencia))) continue;
            await repository.AddAsync(Lembrete.Automatico(pedido.EmpresaId, TipoLembrete.PedidoAgendado, referencia,
                texto.Length > Lembrete.TextoTamanhoMaximo ? texto[..Lembrete.TextoTamanhoMaximo] : texto,
                agora, conversaId: pedido.ConversaId, pedidoId: pedido.PedidoId), ct);
            criados++;
        }

        var semBaixa = await candidatos.ListarPedidosSemBaixaAsync(agora - PagamentoSemBaixaApos, ct);
        var semBaixaExistentes = await ExistentesAsync(
            TipoLembrete.PagamentoSemBaixa, semBaixa.Select(p => (p.EmpresaId, p.PedidoId.ToString())), ct);
        foreach (var pedido in semBaixa)
        {
            var referencia = pedido.PedidoId.ToString();
            vigentes.Add((pedido.EmpresaId, TipoLembrete.PagamentoSemBaixa, referencia));
            if (!semBaixaExistentes.Add((pedido.EmpresaId, referencia))) continue;

            var quem = string.IsNullOrWhiteSpace(pedido.ClienteNome) ? "Um pedido" : $"O pedido de {pedido.ClienteNome.Trim()}";
            await repository.AddAsync(Lembrete.Automatico(pedido.EmpresaId, TipoLembrete.PagamentoSemBaixa, referencia,
                $"{quem} está há {PagamentoSemBaixaApos.TotalMinutes:0} min aguardando pagamento sem baixa.", agora,
                conversaId: pedido.ConversaId, pedidoId: pedido.PedidoId), ct);
            criados++;
        }

        // #1427: o prazo é da loja. A consulta traz desde o menor SLA possível e o corte fino é aqui, por loja.
        var padrao = prazos.Value.ClienteSemResposta;
        var slaPorEmpresa = new Dictionary<Guid, TimeSpan>();
        var aguardandoPrazo = new HashSet<Guid>();
        var menorPrazo = TimeSpan.FromMinutes(Math.Min(ConfiguracaoAtendimento.SlaRespostaMinimoMinutos, padrao.TotalMinutes));
        var semResposta = new List<(ConversaSemResposta Conversa, string Referencia, TimeSpan Sla)>();
        foreach (var conversa in await candidatos.ListarConversasSemRespostaAsync(agora - menorPrazo, ct))
        {
            var semRespostaApos = SlaDa(conversa, padrao);
            var expediente = conversa.Expediente ?? ExpedienteLoja.CriarPadrao(conversa.EmpresaId);
            if (expediente.EmpresaId != conversa.EmpresaId) continue;
            var referencia = conversa.MensagemEntradaId.ToString();
            vigentes.Add((conversa.EmpresaId, TipoLembrete.ClienteSemResposta, referencia));
            if (conversa.EntradaEm is { } entrada &&
                expediente.MinutosAbertosEntre(entrada, agora, semRespostaApos.TotalMinutes) <= semRespostaApos.TotalMinutes)
            {
                aguardandoPrazo.Add(conversa.ConversaId);
                continue;
            }
            slaPorEmpresa[conversa.EmpresaId] = semRespostaApos;
            semResposta.Add((conversa, referencia, semRespostaApos));
        }
        var semRespostaExistentes = await ExistentesAsync(
            TipoLembrete.ClienteSemResposta, semResposta.Select(s => (s.Conversa.EmpresaId, s.Referencia)), ct);
        foreach (var (conversa, referencia, semRespostaApos) in semResposta)
        {
            if (!semRespostaExistentes.Add((conversa.EmpresaId, referencia))) continue;

            var quem = string.IsNullOrWhiteSpace(conversa.ContatoNome) ? "Um cliente" : conversa.ContatoNome.Trim();
            await repository.AddAsync(Lembrete.Automatico(conversa.EmpresaId, TipoLembrete.ClienteSemResposta, referencia,
                $"{quem} está há {semRespostaApos.TotalMinutes:0} min sem resposta.", agora,
                paraUsuarioId: conversa.AssumidaPorUsuarioId, conversaId: conversa.ConversaId), ct);
            criados++;
        }

        var resolvidos = 0;
        foreach (var aberto in await repository.ListarAutomaticosAbertosAsync(ct))
        {
            // Só resolve os tipos que ele mesmo cria. O de integração (F16, #1246) é do vigia.
            if (aberto.Tipo is not (TipoLembrete.PagamentoSemBaixa or TipoLembrete.ClienteSemResposta or TipoLembrete.PedidoAgendado)) continue;
            if (aberto.Referencia is { } r && vigentes.Contains((aberto.EmpresaId, aberto.Tipo, r))) continue;
            aberto.Concluir(agora);
            resolvidos++;
        }

        if (criados + resolvidos > 0)
            await unitOfWork.CommitAsync();

        var avisados = await AvisarVencidosAsync(agora, slaPorEmpresa, aguardandoPrazo, ct);
        return new ResultadoAvaliacaoLembretes(criados, resolvidos, avisados);
    }

    private static TimeSpan SlaDa(ConversaSemResposta conversa, TimeSpan padrao) =>
        conversa.SlaRespostaMinutos is { } minutos and > 0 ? TimeSpan.FromMinutes(minutos) : padrao;

    private static void MarcarAgendado(PedidoAgendadoLembrete pedido, string marco, string texto, bool venceu,
        HashSet<(Guid EmpresaId, TipoLembrete Tipo, string Referencia)> vigentes,
        List<(PedidoAgendadoLembrete Pedido, string Referencia, string Texto)> vencidos)
    {
        // O instante faz parte do marco: reagendar encerra o aviso anterior e permite lembrar a nova data.
        var referencia = $"{pedido.PedidoId}:{marco}";
        vigentes.Add((pedido.EmpresaId, TipoLembrete.PedidoAgendado, referencia));
        if (venceu) vencidos.Add((pedido, referencia, texto));
    }

    /// <summary>
    /// Os fatos que já têm lembrete, em uma consulta. Quem chama acrescenta o que cria, e o mesmo fato
    /// repetido na rodada não nasce duas vezes.
    /// </summary>
    private async Task<HashSet<(Guid EmpresaId, string Referencia)>> ExistentesAsync(
        TipoLembrete tipo, IEnumerable<(Guid EmpresaId, string Referencia)> fatos, CancellationToken ct)
    {
        var distintos = fatos.Distinct().ToList();
        return distintos.Count == 0 ? [] : [.. await repository.ListarAutomaticosExistentesAsync(tipo, distintos, ct)];
    }

    // `slaPorEmpresa`: o SLA aplicado nesta rodada, para o PrazoEstourado dizer o mesmo prazo que criou o lembrete.
    private async Task<int> AvisarVencidosAsync(DateTime agora, IReadOnlyDictionary<Guid, TimeSpan> slaPorEmpresa,
        IReadOnlySet<Guid> aguardandoPrazo, CancellationToken ct)
    {
        var vencidos = (await repository.ListarVencidosSemAvisoAsync(agora, AvisosPorRodada, ct))
            .Where(l => l.Tipo != TipoLembrete.ClienteSemResposta || l.ConversaId is not { } c || !aguardandoPrazo.Contains(c))
            .ToList();
        if (vencidos.Count == 0) return 0;

        foreach (var lembrete in vencidos)
        {
            // Com destinatário, o Push vai só para ele; sem, para todas as inscrições da empresa.
            var payload = lembrete.ParaUsuarioId is { } usuarioId
                ? JsonSerializer.Serialize(new { usuarioId, lembreteId = lembrete.Id, tipo = lembrete.Tipo.ToString(), texto = lembrete.Texto })
                : JsonSerializer.Serialize(new { lembreteId = lembrete.Id, tipo = lembrete.Tipo.ToString(), texto = lembrete.Texto });
            await notificador.EnfileirarEventoAsync(TipoEventoNotificacao.LembreteVencido, lembrete.EmpresaId, payload, lembrete.Id, ct);
            // N11: o cliente sem resposta também chega por e-mail e WhatsApp. Pagamento sem baixa e lembrete manual
            // ficam só no Push. Sem atendente, o destino é a audiência da rotina (gestores).
            if (lembrete is { Tipo: TipoLembrete.ClienteSemResposta, CriadoPorUsuarioId: null })
            {
                var sla = slaPorEmpresa.GetValueOrDefault(lembrete.EmpresaId, prazos.Value.ClienteSemResposta);
                await PrazoEstouradoEvento.EnfileirarAsync(notificador, prazos.Value, TipoPrazo.ClienteSemResposta,
                    lembrete.EmpresaId, lembrete.Id, PrazoEstouradoEvento.Referencia(lembrete.ConversaId ?? lembrete.Id),
                    PrazoEstouradoEvento.Duracao(sla), PrazoEstouradoEvento.Duracao(sla), lembrete.ParaUsuarioId, ct);
            }
            lembrete.MarcarAvisado(agora);
        }

        await unitOfWork.CommitAsync();

        foreach (var lembrete in vencidos)
            await publisher.PublicarAsync(EventoSse, lembrete.EmpresaId, new
            {
                id = lembrete.Id,
                tipo = lembrete.Tipo,
                texto = lembrete.Texto,
                paraUsuarioId = lembrete.ParaUsuarioId,
                conversaId = lembrete.ConversaId,
                pedidoId = lembrete.PedidoId,
            }, ct);

        return vencidos.Count;
    }
}
