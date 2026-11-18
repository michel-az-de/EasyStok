using EasyStock.Application.Ports.Output.Atendimento;
using EasyStock.Application.Services.Atendimento;
using EasyStock.Application.UseCases.Cliente.Foto;

namespace EasyStock.Application.UseCases.Atendimento.Webhook;

/// <summary>
/// #2155: drena um <see cref="ColetarFotoPerfilWhatsAppJob"/>. A foto do perfil do WhatsApp vira a foto do cliente
/// pelo mesmo caminho do upload do console (otimização, storage privado e histórico). Foto escolhida pela loja nunca é
/// trocada. Sem foto, canal sem suporte ou falha da ponte: nada muda.
/// #2838: a foto que veio do WhatsApp é renovada depois de <see cref="Validade"/>, e a ponte é consultada no máximo uma
/// vez por cliente na <see cref="JanelaFotoPerfilWhatsApp"/>.
/// </summary>
public sealed class ColetarFotoPerfilWhatsAppUseCase(
    IClienteRepository clientes,
    IWhatsAppCloudClient whatsApp,
    ClienteFotoUseCase fotos,
    ITenantContextAccessor tenantContext,
    JanelaFotoPerfilWhatsApp janela,
    TimeProvider relogio,
    ILogger<ColetarFotoPerfilWhatsAppUseCase> logger)
{
    public async Task ExecuteAsync(ColetarFotoPerfilWhatsAppJob job, CancellationToken ct = default)
    {
        // Escopo do job não tem JWT: sem isto o filtro global e a RLS (ADR-0010) zeram a consulta.
        tenantContext.SetCurrentTenant(job.EmpresaId);

        // A janela vem antes do banco: o webhook enfileira a cada mensagem do cliente.
        if (!janela.Reservar(job.EmpresaId, job.ClienteId)) return;
        var cliente = await clientes.GetByIdAsync(job.EmpresaId, job.ClienteId);
        if (cliente is null || !await PodeTrocarAsync(cliente)) return;

        FotoPerfilWhatsApp? foto;
        try
        {
            foto = await whatsApp.ObterFotoPerfilAsync(job.Contato, ct);
        }
        catch (Exception ex) when (!ct.IsCancellationRequested)
        {
            janela.Liberar(job.EmpresaId, job.ClienteId); // ponte fora do ar: a próxima mensagem tenta de novo
            logger.LogWarning(ex, "Foto do perfil do WhatsApp: a ponte não devolveu a foto do cliente {ClienteId}.", job.ClienteId);
            return;
        }

        if (foto is null || foto.Conteudo.Length is 0 or > FotoPerfilWhatsApp.TetoBytes)
        {
            logger.LogInformation("Foto do perfil do WhatsApp: o cliente {ClienteId} não tem foto visível.", job.ClienteId);
            return;
        }

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

    public static readonly TimeSpan Validade = TimeSpan.FromDays(7);

    /// <summary>
    /// Sem foto que a loja tenha removido, ou com a foto que o próprio WhatsApp deu há mais de <see cref="Validade"/>.
    /// A última gravação da foto no histórico diz quem a pôs (ou a tirou).
    /// </summary>
    private async Task<bool> PodeTrocarAsync(EasyStock.Domain.Entities.Cliente cliente)
    {
        var gravacao = (await clientes.GetAlteracoesAsync(cliente.Id) ?? [])
            .Where(a => a.Campo == "Foto").MaxBy(a => a.AlteradoEm);
        if (cliente.FotoChave is null)
            return gravacao is null || gravacao.ValorNovo is not null || gravacao.Origem == PerfilClienteWhatsApp.Origem;
        return gravacao is { Origem: PerfilClienteWhatsApp.Origem } && gravacao.ValorNovo == cliente.FotoChave
            && relogio.GetUtcNow().UtcDateTime - gravacao.AlteradoEm >= Validade;
    }
}
