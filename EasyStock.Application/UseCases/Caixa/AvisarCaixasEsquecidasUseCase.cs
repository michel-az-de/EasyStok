using System.Text.Json;
using EasyStock.Application.Ports.Output.Notifications;
using EasyStock.Application.Services.Notifications;
using EasyStock.Domain.Enums.Notifications;
using Microsoft.Extensions.Options;

namespace EasyStock.Application.UseCases.Caixa;

/// <summary>
/// Miolo do <c>CaixaEsquecidoJob</c> (ADR-0034 / #641): caixa aberto de dia anterior vira o aviso in-app
/// <see cref="TipoEventoNotificacao.CaixaAbertoEsquecido"/> (sino) e, desde a N11, também o
/// <see cref="TipoEventoNotificacao.PrazoEstourado"/> (e-mail e WhatsApp para a equipe). Só avisa, não fecha.
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

        var agora = relogio.GetUtcNow().UtcDateTime;
        var hoje = HorarioBrasil.DataOperacional(agora);
        var esquecidas = await caixaRepository.GetAberturasEsquecidasAsync(HorarioBrasil.InicioRealDoDiaUtc(hoje), ct);

        var avisados = 0;
        foreach (var abertura in esquecidas)
        {
            if (abertura.NotificadoEsquecidoEm != null) continue; // dedup: já avisado
            if (ct.IsCancellationRequested) break;

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
                valor_abertura = abertura.Valor,
                loja_nome = string.Empty
            });

            try
            {
                await notificador.PublicarEventoAsync(
                    TipoEventoNotificacao.CaixaAbertoEsquecido, abertura.EmpresaId,
                    usuarioDestinoId: usuarioId, payloadJson: payload, ct: ct);
                await EnfileirarPrazoAsync(abertura.EmpresaId, abertura.Id, diaAbertura, hoje, ct);
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
    private async Task EnfileirarPrazoAsync(Guid empresaId, Guid aberturaId, DateOnly diaAbertura, DateOnly hoje, CancellationToken ct)
    {
        var opcoes = prazos.Value;
        if (!opcoes.Habilitado) return;

        var correlationId = PrazoEstouradoEvento.CorrelationId(TipoPrazo.CaixaEsquecido, aberturaId);
        if (await eventoRepository.ExisteCorrelacaoAsync(empresaId, correlationId, ct)) return;

        var dias = Math.Max(1, hoje.DayNumber - diaAbertura.DayNumber);
        await PrazoEstouradoEvento.EnfileirarAsync(notificador, opcoes, TipoPrazo.CaixaEsquecido,
            empresaId, aberturaId, PrazoEstouradoEvento.Referencia(aberturaId),
            diaAbertura.ToString("dd/MM/yyyy"), PrazoEstouradoEvento.Duracao(TimeSpan.FromDays(dias)), usuarioId: null, ct);
        await unitOfWork.CommitAsync();
    }
}
