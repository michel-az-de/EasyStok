using System.Text.Json;
using EasyStock.Application.Ports.Output.Atendimento;
using EasyStock.Application.Ports.Output.Notifications;
using EasyStock.Application.Ports.Output.Persistence.Atendimento;
using EasyStock.Domain.Entities.Atendimento;
using EasyStock.Domain.Enums.Atendimento;
using EasyStock.Domain.Enums.Notifications;

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
    TimeProvider relogio)
{
    public static readonly TimeSpan PagamentoSemBaixaApos = TimeSpan.FromMinutes(15);
    public static readonly TimeSpan SemRespostaApos = TimeSpan.FromMinutes(10);
    public const string EventoSse = "lembrete.vencido";
    public const int AvisosPorRodada = 100;

    public async Task<ResultadoAvaliacaoLembretes> ExecuteAsync(CancellationToken ct = default)
    {
        var agora = relogio.GetUtcNow().UtcDateTime;
        var vigentes = new HashSet<(Guid EmpresaId, TipoLembrete Tipo, string Referencia)>();
        var criados = 0;

        foreach (var pedido in await candidatos.ListarPedidosSemBaixaAsync(agora - PagamentoSemBaixaApos, ct))
        {
            var referencia = pedido.PedidoId.ToString();
            vigentes.Add((pedido.EmpresaId, TipoLembrete.PagamentoSemBaixa, referencia));
            if (await repository.ExisteAutomaticoAsync(pedido.EmpresaId, TipoLembrete.PagamentoSemBaixa, referencia, ct)) continue;

            var quem = string.IsNullOrWhiteSpace(pedido.ClienteNome) ? "Um pedido" : $"O pedido de {pedido.ClienteNome.Trim()}";
            await repository.AddAsync(Lembrete.Automatico(pedido.EmpresaId, TipoLembrete.PagamentoSemBaixa, referencia,
                $"{quem} está há {PagamentoSemBaixaApos.TotalMinutes:0} min aguardando pagamento sem baixa.", agora,
                conversaId: pedido.ConversaId, pedidoId: pedido.PedidoId), ct);
            criados++;
        }

        foreach (var conversa in await candidatos.ListarConversasSemRespostaAsync(agora - SemRespostaApos, ct))
        {
            var referencia = conversa.MensagemEntradaId.ToString();
            vigentes.Add((conversa.EmpresaId, TipoLembrete.ClienteSemResposta, referencia));
            if (await repository.ExisteAutomaticoAsync(conversa.EmpresaId, TipoLembrete.ClienteSemResposta, referencia, ct)) continue;

            var quem = string.IsNullOrWhiteSpace(conversa.ContatoNome) ? "Um cliente" : conversa.ContatoNome.Trim();
            await repository.AddAsync(Lembrete.Automatico(conversa.EmpresaId, TipoLembrete.ClienteSemResposta, referencia,
                $"{quem} está há {SemRespostaApos.TotalMinutes:0} min sem resposta.", agora,
                paraUsuarioId: conversa.AssumidaPorUsuarioId, conversaId: conversa.ConversaId), ct);
            criados++;
        }

        var resolvidos = 0;
        foreach (var aberto in await repository.ListarAutomaticosAbertosAsync(ct))
        {
            // Só resolve os tipos que ele mesmo cria. O de integração (F16, #1246) é do vigia.
            if (aberto.Tipo is not (TipoLembrete.PagamentoSemBaixa or TipoLembrete.ClienteSemResposta)) continue;
            if (aberto.Referencia is { } r && vigentes.Contains((aberto.EmpresaId, aberto.Tipo, r))) continue;
            aberto.Concluir(agora);
            resolvidos++;
        }

        if (criados + resolvidos > 0)
            await unitOfWork.CommitAsync();

        var avisados = await AvisarVencidosAsync(agora, ct);
        return new ResultadoAvaliacaoLembretes(criados, resolvidos, avisados);
    }

    private async Task<int> AvisarVencidosAsync(DateTime agora, CancellationToken ct)
    {
        var vencidos = await repository.ListarVencidosSemAvisoAsync(agora, AvisosPorRodada, ct);
        if (vencidos.Count == 0) return 0;

        foreach (var lembrete in vencidos)
        {
            // Com destinatário, o Push vai só para ele; sem, para todas as inscrições da empresa.
            var payload = lembrete.ParaUsuarioId is { } usuarioId
                ? JsonSerializer.Serialize(new { usuarioId, lembreteId = lembrete.Id, tipo = lembrete.Tipo.ToString(), texto = lembrete.Texto })
                : JsonSerializer.Serialize(new { lembreteId = lembrete.Id, tipo = lembrete.Tipo.ToString(), texto = lembrete.Texto });
            await notificador.EnfileirarEventoAsync(TipoEventoNotificacao.LembreteVencido, lembrete.EmpresaId, payload, lembrete.Id, ct);
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
