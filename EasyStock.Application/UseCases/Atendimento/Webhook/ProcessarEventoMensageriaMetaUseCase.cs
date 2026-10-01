using System.Security.Cryptography;
using System.Text;
using EasyStock.Application.Ports.Output.Atendimento;
using EasyStock.Application.Ports.Output.Persistence.Atendimento;
using EasyStock.Application.UseCases.FeatureFlags;
using EasyStock.Domain.Entities.Atendimento;
using EasyStock.Domain.Enums.Atendimento;

namespace EasyStock.Application.UseCases.Atendimento.Webhook;

/// <summary>
/// Processa o webhook da Meta para Instagram e Messenger (S35), já com a assinatura conferida pelo
/// controller. Roteia o tenant pelo <c>recipient.id</c> (conta do Instagram ou página), exige a flag
/// do módulo e a do canal, grava a mensagem com idempotência pelo <c>mid</c> e põe a conversa na fila
/// humana sem responsável. O agente não responde nestes canais: ele envia direto pelo WhatsApp.
/// </summary>
public sealed class ProcessarEventoMensageriaMetaUseCase(
    IEmpresaRepository empresaRepository,
    ITenantFeatureFlagRepository featureFlagRepository,
    IConversaRepository conversaRepository,
    IOperacaoEventPublisher eventPublisher,
    ITenantContextAccessor tenantContext,
    IUnitOfWork unitOfWork,
    ILogger<ProcessarEventoMensageriaMetaUseCase> logger)
{
    /// <returns><c>false</c> quando alguma mensagem falhou por motivo que um reenvio da Meta pode resolver.</returns>
    public async Task<bool> ExecuteAsync(string rawBody, CancellationToken ct = default)
    {
        EventoMensageriaMeta evento;
        try
        {
            evento = WebhookMensageriaMetaParser.Parse(rawBody);
        }
        catch (System.Text.Json.JsonException ex)
        {
            logger.LogWarning(ex, "Webhook Meta (Instagram/Messenger): corpo não é JSON, ignorado.");
            return true;
        }

        var completo = true;
        foreach (var mensagem in evento.Mensagens.Where(m => !m.Eco))
            completo &= await ProcessarAsync(mensagem, ct);
        return completo;
    }

    private async Task<bool> ProcessarAsync(MensagemRecebidaMeta msg, CancellationToken ct)
    {
        var empresa = msg.Canal == CanalConversa.Instagram
            ? await empresaRepository.GetByInstagramAccountIdAsync(msg.RecipientId, ct)
            : await empresaRepository.GetByFacebookPageIdAsync(msg.RecipientId, ct);
        if (empresa is null)
        {
            logger.LogWarning("Webhook Meta: {Canal} sem empresa vinculada ao destinatário, ignorado.", msg.Canal);
            return true;
        }

        tenantContext.SetCurrentTenant(empresa.Id);
        var flags = await featureFlagRepository.ListarAtivasAsync(empresa.Id, ct);
        var flagCanal = msg.Canal == CanalConversa.Instagram ? FeatureCatalogo.CanalInstagram : FeatureCatalogo.CanalMessenger;
        if (!flags.Contains(FeatureCatalogo.ModuloAtendimento, StringComparer.OrdinalIgnoreCase)
            || !flags.Contains(flagCanal, StringComparer.OrdinalIgnoreCase))
        {
            logger.LogInformation("Webhook Meta: {Canal} desligado para a empresa {EmpresaId}, ignorado.", msg.Canal, empresa.Id);
            return true;
        }

        var externoId = ExternoId(msg.Mid);
        if (await conversaRepository.ObterMensagemPorExternoIdAsync(empresa.Id, externoId, ct) is not null)
            return true; // reentrega da Meta

        // A janela conta da mensagem do cliente, não do processamento.
        var enviadaEm = msg.Timestamp.UtcDateTime;
        Conversa conversa;
        Mensagem mensagem;
        try
        {
            conversa = await conversaRepository.ObterAbertaPorContatoAsync(empresa.Id, msg.Canal, msg.SenderId, ct)
                ?? await AbrirAsync(empresa.Id, msg, enviadaEm, ct);

            var (tipo, texto) = Conteudo(msg);
            var payload = msg.PostbackPayload is { Length: > Mensagem.BotaoIdTamanhoMaximo } longo
                ? longo[..Mensagem.BotaoIdTamanhoMaximo]
                : msg.PostbackPayload;
            mensagem = Mensagem.Entrada(empresa.Id, conversa.Id, enviadaEm, tipo, texto, externoId, payload);
            conversa.RegistrarEntrada(enviadaEm);
            await conversaRepository.AddMensagemAsync(mensagem, ct);
            await unitOfWork.CommitAsync();
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            // Ex.: duas entregas da 1ª mensagem do contato ao mesmo tempo violando o índice da conversa
            // aberta. Descarta o que ficou rastreado e pede reenvio; o mid já gravado é pulado.
            unitOfWork.DescartarAlteracoesPendentes();
            logger.LogWarning(ex, "Webhook Meta: falha ao gravar mensagem de {Canal}.", msg.Canal);

            // Regra de domínio não passa num reenvio: pedir retry faria a Meta reenviar para sempre
            // (mesmo critério do webhook do WhatsApp).
            return ex is RegraDeDominioVioladaException;
        }

        try
        {
            await eventPublisher.PublicarAsync("conversa.mensagem_recebida", empresa.Id,
                new { conversaId = conversa.Id, mensagemId = mensagem.Id }, ct);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            logger.LogWarning(ex, "Webhook Meta: mensagem {MensagemId} gravada, aviso ao console falhou.", mensagem.Id);
        }

        return true;
    }

    private async Task<Conversa> AbrirAsync(Guid empresaId, MensagemRecebidaMeta msg, DateTime enviadaEm, CancellationToken ct)
    {
        var conversa = Conversa.Abrir(empresaId, msg.SenderId, enviadaEm, canal: msg.Canal);
        conversa.Assumir(enviadaEm); // fila humana, sem responsável: o agente não responde aqui
        await conversaRepository.AddAsync(conversa, ct);
        return conversa;
    }

    /// <summary>
    /// O <c>mid</c> do Instagram pode passar de <see cref="Mensagem.ExternoIdTamanhoMaximo"/>. Sem
    /// alargar a coluna, o longo vira um id determinístico (<c>h:</c> + SHA-256), que mantém a
    /// deduplicação da reentrega.
    /// </summary>
    private static string ExternoId(string mid) =>
        mid.Length <= Mensagem.ExternoIdTamanhoMaximo
            ? mid
            : "h:" + Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(mid))).ToLowerInvariant();

    /// <summary>A mídia recebida ainda não é guardada (a URL da CDN expira): fica o tipo e um aviso.</summary>
    private static (TipoConteudoMensagem Tipo, string? Texto) Conteudo(MensagemRecebidaMeta msg)
    {
        if (msg.PostbackPayload is not null) return (TipoConteudoMensagem.Botao, msg.Texto);
        return msg.TipoAnexo switch
        {
            // Mensagem apagada ou sem suporte (is_deleted, is_unsupported) chega sem texto e sem anexo.
            null when string.IsNullOrWhiteSpace(msg.Texto) => (TipoConteudoMensagem.Outro, "[mensagem sem conteúdo suportado; abra no app para ver]"),
            null => (TipoConteudoMensagem.Texto, msg.Texto),
            "image" => (TipoConteudoMensagem.Imagem, msg.Texto ?? "[imagem recebida; abra no app para ver]"),
            "audio" => (TipoConteudoMensagem.Audio, msg.Texto ?? "[áudio recebido; abra no app para ouvir]"),
            "file" => (TipoConteudoMensagem.Documento, msg.Texto ?? "[arquivo recebido; abra no app para ver]"),
            _ => (TipoConteudoMensagem.Outro, msg.Texto ?? $"[{msg.TipoAnexo} recebido; abra no app para ver]"),
        };
    }
}
