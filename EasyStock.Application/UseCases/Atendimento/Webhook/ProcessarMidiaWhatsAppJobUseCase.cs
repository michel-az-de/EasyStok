using EasyStock.Application.Ports.Output.Persistence.Atendimento;
using EasyStock.Application.Services.Atendimento;

namespace EasyStock.Application.UseCases.Atendimento.Webhook;

/// <summary>
/// Drena um <see cref="ArmazenarMidiaWhatsAppJob"/> da fila (S03): baixa a mídia pelo
/// <see cref="ArmazenadorMidiaWhatsApp"/> (S02) e anexa a chave na <c>Mensagem</c> correspondente.
/// </summary>
public sealed class ProcessarMidiaWhatsAppJobUseCase(
    IConversaRepository conversaRepository,
    ArmazenadorMidiaWhatsApp armazenador,
    IUnitOfWork unitOfWork,
    ILogger<ProcessarMidiaWhatsAppJobUseCase> logger)
{
    public async Task ExecuteAsync(ArmazenarMidiaWhatsAppJob job, CancellationToken ct = default)
    {
        var mensagem = await conversaRepository.ObterMensagemPorExternoIdAsync(job.EmpresaId, job.Wamid, ct);
        if (mensagem is null)
        {
            logger.LogWarning("Fila de mídia WhatsApp: mensagem não encontrada para wamid {Wamid}.", job.Wamid);
            return;
        }

        try
        {
            var (chave, mime) = await armazenador.ArmazenarAsync(job.EmpresaId, job.ConversaId, job.Wamid, job.MediaId, ct);
            mensagem.AnexarMidia(chave, mime);
            await unitOfWork.CommitAsync();
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Fila de mídia WhatsApp: falha ao armazenar mídia do wamid {Wamid}.", job.Wamid);
        }
    }
}
