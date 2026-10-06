using EasyStock.Application.Ports.Output.Persistence.Atendimento;
using EasyStock.Application.Services.Atendimento;

namespace EasyStock.Application.UseCases.Atendimento.Webhook;

/// <summary>
/// Drena um <see cref="ArmazenarMidiaWhatsAppJob"/> da fila (S03): baixa a mídia pelo
/// <see cref="ArmazenadorMidiaWhatsApp"/> (S02) e anexa a chave na <c>Mensagem</c> correspondente.
/// #1406: com transcritor disponível, o webhook não enfileira o turno do agente para áudio; ele sai
/// daqui, depois da transcrição (ou na primeira falha da mídia, para o agente pedir que o cliente escreva).
/// </summary>
public sealed class ProcessarMidiaWhatsAppJobUseCase(
    IConversaRepository conversaRepository,
    ArmazenadorMidiaWhatsApp armazenador,
    ITenantContextAccessor tenantContext,
    IUnitOfWork unitOfWork,
    TimeProvider relogio,
    ILogger<ProcessarMidiaWhatsAppJobUseCase> logger,
    TranscricaoAudioWhatsApp? transcricao = null,
    IQueueService? fila = null)
{
    public async Task ExecuteAsync(ArmazenarMidiaWhatsAppJob job, CancellationToken ct = default)
    {
        // Escopo do job não tem JWT: sem isto o filtro global e a RLS (ADR-0010) zeram a consulta.
        tenantContext.SetCurrentTenant(job.EmpresaId);

        var mensagem = await conversaRepository.ObterMensagemPorExternoIdAsync(job.EmpresaId, job.Wamid, ct);
        if (mensagem is null)
        {
            logger.LogWarning("Fila de mídia WhatsApp: mensagem não encontrada para wamid {Wamid}.", job.Wamid);
            return;
        }
        if (mensagem.MidiaChave is not null)
            return; // fila em memória e varredura podem entregar o mesmo anexo (#1397)

        try
        {
            var (chave, mime) = await armazenador.ArmazenarAsync(job.EmpresaId, job.ConversaId, job.Wamid, job.MediaId, ct);
            mensagem.AnexarMidia(chave, mime);
            await unitOfWork.CommitAsync();
            if (transcricao is not null) await transcricao.TranscreverAsync(mensagem, ct); // #1398, nunca lança
            await DispararTurnoDoAudioAsync(job, mensagem);
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Fila de mídia WhatsApp: falha ao armazenar mídia do wamid {Wamid}.", job.Wamid);
            // #1397: o erro fica na mensagem (o console mostra) e a varredura tenta de novo até o limite.
            mensagem.RegistrarFalhaMidia(ex.Message, relogio.GetUtcNow().UtcDateTime);
            await unitOfWork.CommitAsync();
            if (mensagem.TentativasMidia == 1) await DispararTurnoDoAudioAsync(job, mensagem);
        }
    }

    private Task DispararTurnoDoAudioAsync(ArmazenarMidiaWhatsAppJob job, Domain.Entities.Atendimento.Mensagem mensagem) =>
        AguardaTranscricao(mensagem.TipoConteudo, transcricao?.Disponivel == true) && fila is not null
            ? fila.EnqueueAsync(FilaAtendimentoNomes.TurnoAgente, new ProcessarTurnoAgenteJob(job.EmpresaId, job.ConversaId))
            : Task.CompletedTask;

    /// <summary>Regra única (#1406): áudio com transcritor disponível tem o turno disparado pelo job de mídia.</summary>
    public static bool AguardaTranscricao(Domain.Enums.Atendimento.TipoConteudoMensagem tipo, bool transcritorDisponivel) =>
        tipo == Domain.Enums.Atendimento.TipoConteudoMensagem.Audio && transcritorDisponivel;
}
