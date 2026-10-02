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
    ITenantContextAccessor tenantContext,
    IUnitOfWork unitOfWork,
    TimeProvider relogio,
    ILogger<ProcessarMidiaWhatsAppJobUseCase> logger)
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
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Fila de mídia WhatsApp: falha ao armazenar mídia do wamid {Wamid}.", job.Wamid);
            // #1397: o erro fica na mensagem (o console mostra) e a varredura tenta de novo até o limite.
            mensagem.RegistrarFalhaMidia(ex.Message, relogio.GetUtcNow().UtcDateTime);
            await unitOfWork.CommitAsync();
        }
    }
}
