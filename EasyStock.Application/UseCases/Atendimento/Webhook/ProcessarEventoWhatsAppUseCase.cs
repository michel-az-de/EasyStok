using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using EasyStock.Application.Ports.Output.Atendimento;
using EasyStock.Application.Ports.Output.Persistence.Atendimento;
using EasyStock.Application.UseCases.FeatureFlags;
using EasyStock.Domain.Entities.Atendimento;
using EasyStock.Domain.Enums.Atendimento;

namespace EasyStock.Application.UseCases.Atendimento.Webhook;

/// <summary>
/// Processa um payload já validado (assinatura conferida pelo controller) do webhook da Meta:
/// resolve o tenant por <c>phone_number_id</c>, persiste mensagens e status com idempotência por
/// <c>wamid</c>, e enfileira o que roda fora da requisição (agente, mídia). Cada mensagem é
/// isolada: uma falha descarta só o que ela deixou rastreado e não impede as demais.
/// </summary>
public sealed class ProcessarEventoWhatsAppUseCase(
    IEmpresaRepository empresaRepository,
    ITenantFeatureFlagRepository featureFlagRepository,
    IConfiguracaoAtendimentoRepository configuracaoAtendimentoRepository,
    IConversaRepository conversaRepository,
    IWebhookRecebidoRepository webhookRecebidoRepository,
    IWhatsAppCloudClient cloudClient,
    IQueueService queueService,
    IOperacaoEventPublisher eventPublisher,
    ITenantContextAccessor tenantContext,
    IUnitOfWork unitOfWork,
    ILogger<ProcessarEventoWhatsAppUseCase> logger)
{
    private const string Provedor = "meta_whatsapp";

    /// <returns>
    /// <c>false</c> quando alguma mensagem falhou por motivo que um reenvio pode resolver (ex.: dois
    /// POSTs concorrentes da 1ª mensagem de um contato violando o índice da conversa aberta). O
    /// controller responde não-200 e a Meta reenvia; o que já foi gravado é pulado pelo wamid.
    /// </returns>
    public async Task<bool> ExecuteAsync(string rawBody, CancellationToken ct = default)
    {
        EventoWhatsApp evento;
        try
        {
            evento = WebhookWhatsAppParser.Parse(rawBody);
        }
        catch (JsonException ex)
        {
            logger.LogWarning(ex, "Webhook WhatsApp: payload não é JSON válido, ignorado.");
            return true;
        }

        var completo = true;
        foreach (var entrada in evento.Entradas)
            completo &= await ProcessarEntradaAsync(entrada, ct);
        return completo;
    }

    private async Task<bool> ProcessarEntradaAsync(EntradaWhatsApp entrada, CancellationToken ct)
    {
        var empresa = string.IsNullOrWhiteSpace(entrada.PhoneNumberId)
            ? null
            : await empresaRepository.GetByWhatsAppPhoneNumberIdAsync(entrada.PhoneNumberId, ct);

        if (empresa is null)
        {
            logger.LogWarning("Webhook WhatsApp: phone_number_id desconhecido ({PhoneNumberId}).", entrada.PhoneNumberId);
            await RegistrarFalhaDeEntradaAsync(entrada, "empresa_desconhecida", ct);
            return true;
        }

        tenantContext.SetCurrentTenant(empresa.Id);

        var flagsAtivas = await featureFlagRepository.ListarAtivasAsync(empresa.Id, ct);
        if (!flagsAtivas.Contains(FeatureCatalogo.ModuloAtendimento, StringComparer.OrdinalIgnoreCase))
        {
            await RegistrarFalhaDeEntradaAsync(entrada, "modulo_desligado", ct);
            return true;
        }

        var completo = true;
        foreach (var mensagem in entrada.Mensagens)
            completo &= await ProcessarMensagemAsync(empresa.Id, entrada, mensagem, ct);

        foreach (var status in entrada.Statuses)
            await ProcessarStatusAsync(empresa.Id, status, ct);

        return completo;
    }

    private async Task<bool> ProcessarMensagemAsync(Guid empresaId, EntradaWhatsApp entrada, MensagemRecebidaWhatsApp msg, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(msg.Wamid))
        {
            logger.LogWarning("Webhook WhatsApp: mensagem sem wamid, ignorada.");
            return true;
        }

        var registro = await webhookRecebidoRepository.TryRegistrarAsync(Provedor, msg.Wamid, ComputeSha256(msg.Wamid), ct);
        if (registro is null)
        {
            var existente = await webhookRecebidoRepository.ObterAsync(Provedor, msg.Wamid, ct);
            if (existente is { Sucesso: true })
            {
                logger.LogInformation("Webhook WhatsApp: wamid {Wamid} já processado, ignorando duplicata.", msg.Wamid);
                return true;
            }
            registro = existente;
        }

        // Reentrega de um wamid já gravado (1ª tentativa em voo, ou marcada como falha depois do
        // commit): inserir de novo violaria o índice único (EmpresaId, ExternoId) em todo retry.
        if (await conversaRepository.ObterMensagemPorExternoIdAsync(empresaId, msg.Wamid, ct) is not null)
        {
            if (registro is not null)
                await webhookRecebidoRepository.MarcarProcessadoAsync(registro.Id, sucesso: true, ct: ct);
            return true;
        }

        // O instante é o do envio do cliente, não o do processamento: a Meta reentrega com atraso e
        // a janela de 24 h conta da mensagem dele.
        var enviadaEm = msg.Timestamp.UtcDateTime;
        Conversa conversa;
        Mensagem mensagemEntidade;
        try
        {
            var existenteConversa = await conversaRepository.ObterAbertaPorContatoAsync(empresaId, CanalConversa.WhatsApp, msg.De, ct);
            conversa = existenteConversa
                ?? Conversa.Abrir(empresaId, msg.De, enviadaEm, entrada.Contatos.FirstOrDefault(c => c.WaId == msg.De)?.Nome);

            conversa.RegistrarEntrada(enviadaEm);
            if (existenteConversa is null)
                await conversaRepository.AddAsync(conversa, ct);

            var botaoId = msg.BotaoRespostaId ?? msg.BotaoPayload;
            var texto = msg.Tipo is "interactive" or "button" ? null : msg.TextoCorpo;

            mensagemEntidade = Mensagem.Entrada(empresaId, conversa.Id, enviadaEm, MapearTipoConteudo(msg.Tipo), texto, msg.Wamid, botaoId);
            await conversaRepository.AddMensagemAsync(mensagemEntidade, ct);

            await AtualizarUltimaMensagemRecebidaAsync(empresaId, enviadaEm);

            await unitOfWork.CommitAsync();
            await webhookRecebidoRepository.MarcarProcessadoAsync(registro!.Id, sucesso: true, ct: ct);
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Webhook WhatsApp: falha processando mensagem wamid={Wamid}.", msg.Wamid);

            // O que ficou rastreado reenviaria os mesmos inserts no próximo commit do lote.
            unitOfWork.DescartarAlteracoesPendentes();
            if (registro is not null)
                await webhookRecebidoRepository.MarcarProcessadoAsync(registro.Id, sucesso: false, ex.Message, ct);

            // Regra de domínio (ex.: wa_id inválido) não passa num reenvio: pedir retry travaria o
            // payload por dias. O resto (concorrência, banco) passa.
            return ex is RegraDeDominioVioladaException;
        }

        await DispararEfeitosAposGravarAsync(empresaId, conversa, mensagemEntidade, msg, ct);
        return true;
    }

    /// <summary>
    /// Depois do commit, nada aqui pode pedir reenvio: a mensagem já está gravada e o reenvio seria
    /// pulado pelo wamid. Falha é logada.
    /// </summary>
    private async Task DispararEfeitosAposGravarAsync(
        Guid empresaId, Conversa conversa, Mensagem mensagem, MensagemRecebidaWhatsApp msg, CancellationToken ct)
    {
        try { await cloudClient.MarcarComoLidaAsync(msg.Wamid, ct); }
        catch (Exception ex) { logger.LogWarning(ex, "Falha ao marcar {Wamid} como lida (best-effort).", msg.Wamid); }

        try
        {
            if (msg.MidiaId is not null)
            {
                await queueService.EnqueueAsync(FilaAtendimentoNomes.MidiaWhatsApp,
                    new ArmazenarMidiaWhatsAppJob(empresaId, conversa.Id, msg.Wamid, msg.MidiaId));
            }

            if (mensagem.BotaoId is { } botaoId && botaoId.StartsWith("acao:", StringComparison.Ordinal))
            {
                // TODO(S06): RoteadorAcoesBotao resolve "acao:<nome>:<payload>" sem chamar o LLM.
                logger.LogInformation("Webhook WhatsApp: ação de botão {BotaoId} recebida (S06 ainda não implementa o roteador).", botaoId);
            }
            else
            {
                await queueService.EnqueueAsync(FilaAtendimentoNomes.TurnoAgente,
                    new ProcessarTurnoAgenteJob(empresaId, conversa.Id));
            }

            await eventPublisher.PublicarAsync("conversa.mensagem_recebida", empresaId,
                new { conversaId = conversa.Id, mensagemId = mensagem.Id }, ct);
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Webhook WhatsApp: mensagem {Wamid} gravada, mas falhou ao enfileirar o que vem depois.", msg.Wamid);
        }
    }

    private async Task ProcessarStatusAsync(Guid empresaId, StatusRecebidoWhatsApp status, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(status.Wamid)) return;

        var mensagem = await conversaRepository.ObterMensagemPorExternoIdAsync(empresaId, status.Wamid, ct);
        if (mensagem is null)
        {
            logger.LogInformation("Webhook WhatsApp: status para wamid desconhecido ({Wamid}), ignorado.", status.Wamid);
            return;
        }

        var novoStatus = MapearStatus(status.Status);
        if (novoStatus is null) return;

        mensagem.AtualizarStatusEntrega(novoStatus.Value, novoStatus == StatusMensagem.Falhou ? status.ErroMensagem : null);
        await unitOfWork.CommitAsync();
    }

    private async Task RegistrarFalhaDeEntradaAsync(EntradaWhatsApp entrada, string erro, CancellationToken ct)
    {
        var hash = ComputeSha256(entrada.RawJson);
        var registro = await webhookRecebidoRepository.TryRegistrarAsync(Provedor, hash, hash, ct);
        if (registro is not null)
            await webhookRecebidoRepository.MarcarProcessadoAsync(registro.Id, sucesso: false, erro, ct);
    }

    private async Task AtualizarUltimaMensagemRecebidaAsync(Guid empresaId, DateTime agora)
    {
        var configuracao = await configuracaoAtendimentoRepository.GetByEmpresaIdAsync(empresaId);
        var nova = configuracao is null;
        configuracao ??= ConfiguracaoAtendimento.CriarPadrao(empresaId);
        configuracao.RegistrarMensagemRecebida(agora);

        if (nova)
            await configuracaoAtendimentoRepository.AddAsync(configuracao);
        else
            await configuracaoAtendimentoRepository.UpdateAsync(configuracao);
    }

    private static TipoConteudoMensagem MapearTipoConteudo(string tipo) => tipo switch
    {
        "text" => TipoConteudoMensagem.Texto,
        "image" => TipoConteudoMensagem.Imagem,
        "audio" => TipoConteudoMensagem.Audio,
        "document" => TipoConteudoMensagem.Documento,
        "location" => TipoConteudoMensagem.Localizacao,
        "interactive" or "button" => TipoConteudoMensagem.Botao,
        _ => TipoConteudoMensagem.Outro
    };

    private static StatusMensagem? MapearStatus(string status) => status switch
    {
        "sent" => StatusMensagem.Enviada,
        "delivered" => StatusMensagem.Entregue,
        "read" => StatusMensagem.Lida,
        "failed" => StatusMensagem.Falhou,
        _ => null
    };

    private static string ComputeSha256(string valor)
    {
        var hash = SHA256.HashData(Encoding.UTF8.GetBytes(valor ?? ""));
        return Convert.ToHexString(hash).ToLowerInvariant();
    }
}
