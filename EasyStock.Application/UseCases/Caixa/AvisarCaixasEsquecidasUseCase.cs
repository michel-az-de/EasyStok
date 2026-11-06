using System.Text.Json;
using EasyStock.Application.Ports.Output.Notifications;
using EasyStock.Application.Services.Notifications;
using EasyStock.Domain.Enums.Notifications;
using Microsoft.Extensions.Options;
using EasyStock.Domain.ValueObjects;

namespace EasyStock.Application.UseCases.Caixa;

/// <summary>
/// Miolo do <c>CaixaEsquecidoJob</c> (ADR-0034 / #641): caixa aberto de dia anterior vira o aviso in-app
/// <see cref="TipoEventoNotificacao.CaixaAbertoEsquecido"/> (sino) e, desde a N11, também o
/// <see cref="TipoEventoNotificacao.PrazoEstourado"/> (e-mail e WhatsApp para a equipe). Só avisa, não fecha.
///
/// <para>
/// #2258 (F1): o sino não pode contar outra história que a tela do caixa. O aviso só sai se o caixa segue aberto pelo
/// mesmo critério da tela (nenhum fechamento da loja depois da abertura), o dinheiro vai em reais e cada rodada resolve
/// os avisos de caixas que já fecharam (o fechamento também os resolve na hora, em <c>FecharCaixaUseCase</c>).
/// </para>
///
/// <para>
/// Cross-tenant: o job liga o bypass de RLS antes de abrir a conexão e segura o advisory lock. Gate: sem rotina
/// <c>CaixaAbertoEsquecido</c> ativa o pipeline descartaria o evento e o carimbo mataria o aviso futuro.
/// </para>
///
/// <para>
/// Dedupe: <c>NotificadoEsquecidoEm</c> é carimbado depois de publicar (um aviso por sessão). Se o processo cair
/// entre publicar e carimbar, o sino pode repetir no dia seguinte (at-least-once, aceitável), mas o
/// <c>PrazoEstourado</c> não: a pré-checagem do <c>CorrelationId</c> determinístico o impede.
/// </para>
/// </summary>
public sealed class AvisarCaixasEsquecidasUseCase(
    ICaixaRepository caixaRepository,
    IRotinaRepository rotinaRepository,
    IEventoNotificacaoRepository eventoRepository,
    INotificacaoRepository notificacaoRepository,
    INotificadorService notificador,
    IUnitOfWork unitOfWork,
    IOptions<PrazosOptions> prazos,
    TimeProvider relogio,
    ILogger<AvisarCaixasEsquecidasUseCase> logger)
{
    /// <returns>Quantos caixas foram avisados.</returns>
    public async Task<int> ExecuteAsync(CancellationToken ct = default)
    {
        if (!await rotinaRepository.ExisteAtivaAsync(TipoEventoNotificacao.CaixaAbertoEsquecido, ct))
        {
            logger.LogWarning("CaixaEsquecidoJob: rotina CaixaAbertoEsquecido inativa/ausente — pulando (seed pendente?).");
            return 0;
        }

        await ResolverAvisosDeCaixasFechadosAsync(ct);

        var agora = relogio.GetUtcNow().UtcDateTime;
        var hoje = HorarioBrasil.DataOperacional(agora);
        var esquecidas = await caixaRepository.GetAberturasEsquecidasAsync(HorarioBrasil.InicioRealDoDiaUtc(hoje), ct);

        var avisados = 0;
        foreach (var abertura in esquecidas)
        {
            if (abertura.NotificadoEsquecidoEm != null) continue; // dedup: já avisado
            if (ct.IsCancellationRequested) break;

            // #2258 (F1): a consulta pode atrasar frente ao fechamento (ou o marcador faltar); o último fechamento da
            // loja, o mesmo que a tela usa, decide: depois da abertura, o caixa já fechou e não há o que avisar.
            var ultimoFechamento = await caixaRepository.GetUltimoFechamentoDaLojaAsync(abertura.EmpresaId, abertura.LojaId);
            if (ultimoFechamento is not null && ultimoFechamento.FechadoEm > abertura.DataMovimento)
            {
                logger.LogInformation("CaixaEsquecidoJob: abertura {Id} já tem fechamento posterior — não avisa.", abertura.Id);
                continue;
            }

            var usuarioId = abertura.RegistradoPorUserId
                ?? await caixaRepository.ResolverResponsavelPadraoAsync(abertura.EmpresaId, ct);
            if (usuarioId is null)
            {
                logger.LogWarning("CaixaEsquecidoJob: sem destinatário p/ abertura {Id} (empresa {Empresa}) — pulando.",
                    abertura.Id, abertura.EmpresaId);
                continue;
            }

            var diaAbertura = HorarioBrasil.DataOperacional(abertura.DataMovimento);
            var payload = JsonSerializer.Serialize(new
            {
                usuarioId = usuarioId.Value,            // resolve o destinatário do sino in-app
                aberturaId = abertura.Id,
                data_abertura = diaAbertura.ToString("dd/MM/yyyy"),
                valor_abertura = Reais(abertura.Valor),
                loja_nome = string.Empty
            });

            try
            {
                await notificador.PublicarEventoAsync(
                    TipoEventoNotificacao.CaixaAbertoEsquecido, abertura.EmpresaId,
                    usuarioDestinoId: usuarioId, payloadJson: payload, ct: ct, refEntidadeId: abertura.Id);
                await EnfileirarPrazoAsync(abertura.EmpresaId, abertura.Id, diaAbertura, abertura.Valor, hoje, ct);
            }
            catch (Exception ex)
            {
                logger.LogWarning(ex,
                    "CaixaEsquecidoJob: falha ao publicar p/ abertura {Id} — não carimba (retry amanhã).", abertura.Id);
                continue; // não carimba → retenta na próxima rodada
            }

            await caixaRepository.MarcarNotificadoEsquecidoAsync(abertura.Id, agora, ct);
            avisados++;
        }

        logger.LogInformation(
            "CaixaEsquecidoJob: {Avisados} caixa(s) esquecido(s) notificado(s) de {Total} aberto(s) de dias anteriores.",
            avisados, esquecidas.Count);
        return avisados;
    }

    // N11: ao lado do sino. A audiência é a da rotina (gestores); sem usuarioId no payload. A chave determinística
    // faz o segundo passe, depois de uma queda entre publicar e carimbar, não repetir o aviso externo.
    private async Task EnfileirarPrazoAsync(
        Guid empresaId, Guid aberturaId, DateOnly diaAbertura, decimal valorAbertura, DateOnly hoje, CancellationToken ct)
    {
        var opcoes = prazos.Value;
        if (!opcoes.Habilitado) return;

        var correlationId = PrazoEstouradoEvento.CorrelationId(TipoPrazo.CaixaEsquecido, aberturaId);
        if (await eventoRepository.ExisteCorrelacaoAsync(empresaId, correlationId, ct)) return;

        var dias = Math.Max(1, hoje.DayNumber - diaAbertura.DayNumber);
        await PrazoEstouradoEvento.EnfileirarAsync(notificador, opcoes, TipoPrazo.CaixaEsquecido,
            empresaId, aberturaId, CodigoCurto.De(aberturaId),
            diaAbertura.ToString("dd/MM/yyyy"), PrazoEstouradoEvento.Duracao(TimeSpan.FromDays(dias)), usuarioId: null, ct,
            detalhe: $"Aberto desde {diaAbertura:dd/MM/yyyy}, saldo de abertura {Reais(valorAbertura)}. " +
                     "Feche-o para não acumular vendas no dia errado.");
        await unitOfWork.CommitAsync();
    }

    // #2258 (F1): falhar aqui não pode impedir os avisos novos; a próxima rodada varre de novo.
    private async Task ResolverAvisosDeCaixasFechadosAsync(CancellationToken ct)
    {
        try
        {
            var resolvidos = await notificacaoRepository.ResolverAvisosDeCaixaFechadoAsync(null, ct);
            if (resolvidos > 0)
                logger.LogInformation("CaixaEsquecidoJob: {Resolvidos} aviso(s) de caixa já fechado marcado(s) como lido(s).", resolvidos);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            logger.LogWarning(ex, "CaixaEsquecidoJob: não consegui resolver os avisos de caixas já fechados.");
        }
    }

    // "R$ 300,03" com espaço comum (o espaço sem quebra do pt-BR some em alguns renderizadores do sino).
    private static string Reais(decimal valor) => valor.ToString("C2", Cultura.PtBr).Replace(' ', ' ');
}
