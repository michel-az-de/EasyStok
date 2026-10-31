using EasyStock.Application.Ports.Output.Atendimento;
using EasyStock.Application.Services.Atendimento;
using EasyStock.Application.UseCases.Cliente.Foto;

namespace EasyStock.Application.UseCases.Atendimento.Webhook;

/// <summary>
/// #2155: drena um <see cref="ColetarFotoPerfilWhatsAppJob"/>. A foto do perfil do WhatsApp vira a foto do cliente
/// quando ele ainda não tem nenhuma, pelo mesmo caminho do upload do console (otimização, storage privado e
/// histórico). Foto escolhida pela loja nunca é trocada. Sem foto, canal sem suporte ou falha da ponte: nada muda.
/// </summary>
public sealed class ColetarFotoPerfilWhatsAppUseCase(
    IClienteRepository clientes,
    IWhatsAppCloudClient whatsApp,
    ClienteFotoUseCase fotos,
    ITenantContextAccessor tenantContext,
    ILogger<ColetarFotoPerfilWhatsAppUseCase> logger)
{
    public async Task ExecuteAsync(ColetarFotoPerfilWhatsAppJob job, CancellationToken ct = default)
    {
        // Escopo do job não tem JWT: sem isto o filtro global e a RLS (ADR-0010) zeram a consulta.
        tenantContext.SetCurrentTenant(job.EmpresaId);

        var cliente = await clientes.GetByIdAsync(job.EmpresaId, job.ClienteId);
        if (cliente is null || cliente.FotoChave is not null) return;

        FotoPerfilWhatsApp? foto;
        try
        {
            foto = await whatsApp.ObterFotoPerfilAsync(job.Contato, ct);
        }
        catch (Exception ex) when (!ct.IsCancellationRequested)
        {
            logger.LogWarning(ex, "Foto do perfil do WhatsApp: a ponte não devolveu a foto do cliente {ClienteId}.", job.ClienteId);
            return;
        }

        if (foto is null || foto.Conteudo.Length is 0 or > FotoPerfilWhatsApp.TetoBytes) return;

        try
        {
            await fotos.EnviarAsync(job.EmpresaId, job.ClienteId, usuarioId: null, "perfil-whatsapp.jpg",
                foto.MimeType, foto.Conteudo, ct, PerfilClienteWhatsApp.Origem);
            logger.LogInformation("Foto do perfil do WhatsApp gravada no cliente {ClienteId}.", job.ClienteId);
        }
        catch (UseCaseValidationException ex)
        {
            logger.LogWarning("Foto do perfil do WhatsApp recusada para o cliente {ClienteId}: {Motivo}", job.ClienteId, ex.Message);
        }
    }
}
