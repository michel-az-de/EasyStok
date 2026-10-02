using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using EasyStock.Application.Events.Atendimento;
using EasyStock.Application.Ports.Output.Atendimento;
using EasyStock.Application.Ports.Output.Integration;
using EasyStock.Application.Ports.Output.Persistence.Atendimento;
using EasyStock.Application.Services.Atendimento;
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
    IdentificarClientePorTelefoneUseCase identificarCliente,
    SaudacaoAtendimento saudacao,
    RoteadorAcoesBotao roteadorAcoes,
    OptOutPorPalavra optOut,
    IEscaladorConversa escalador,
    ILogger<ProcessarEventoWhatsAppUseCase> logger,
    IPublicadorEventoIntegracao? publicadorEventos = null)
{
    private const string Provedor = "meta_whatsapp";

    /// <summary>Tipos gravados para a dona ver, mas que não pedem resposta: o agente não responde a um emoji.</summary>
    private static readonly HashSet<string> TiposSemTurnoDoAgente = new(StringComparer.Ordinal) { "reaction", "unsupported", "system" };

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
        ConfiguracaoAtendimento configuracao;
        IdentificacaoCliente? identificacao = null;
        try
        {
            var existenteConversa = await conversaRepository.ObterAbertaPorContatoAsync(empresaId, CanalConversa.WhatsApp, msg.De, ct);
            var nomePerfil = entrada.Contatos.FirstOrDefault(c => c.WaId == msg.De)?.Nome;
            conversa = existenteConversa ?? Conversa.Abrir(empresaId, msg.De, enviadaEm, nomePerfil);
            // #1332: responder no wa_id que escreveu, não na grafia antiga com/sem o nono dígito.
            existenteConversa?.AtualizarContatoWhatsApp(msg.De);

            if (existenteConversa is null)
            {
                // S05: identifica (ou cria o lead) na mesma transação que abre a conversa.
                identificacao = await identificarCliente.ExecuteAsync(
                    new IdentificarClientePorTelefoneInput(empresaId, conversa.ContatoIdExterno, nomePerfil), ct);
                conversa.VincularCliente(identificacao.Cliente.Id);
            }

            conversa.RegistrarEntrada(enviadaEm);
            if (existenteConversa is null)
                await conversaRepository.AddAsync(conversa, ct);

            var botaoId = msg.BotaoRespostaId ?? msg.BotaoPayload;
            var texto = msg.Tipo is "interactive" or "button" ? null : msg.TextoCorpo;

            mensagemEntidade = Mensagem.Entrada(empresaId, conversa.Id, enviadaEm, MapearTipoConteudo(msg.Tipo), texto, msg.Wamid, botaoId);
            if (msg.MidiaId is not null)
                mensagemEntidade.AguardarMidia(msg.MidiaId, DateTime.UtcNow); // #1397: pendência durável
            await conversaRepository.AddMensagemAsync(mensagemEntidade, ct);

            // S24: cliente bloqueado não é saudado nem atendido pelo agente; a conversa nasce com a
            // dona (Assumir + nota interna + aviso). O evento entra no outbox deste mesmo commit (ADR-0030).
            if (identificacao?.Cliente.Bloqueado == true)
                await escalador.EscalarAsync(empresaId, conversa, EscalarConversaUseCase.MotivoClienteBloqueado(identificacao.Cliente), DateTime.UtcNow, ct);

            // S42: conversa nova dispara a automática de entrada (primeiro contato, fora do horário ou loja
            // fechada) pelo outbox deste mesmo commit. Cliente bloqueado não recebe nada automático (S24),
            // nem entrada que o agente não vai responder (botão "acao:", reação; issue 1285).
            if (existenteConversa is null && identificacao?.Cliente.Bloqueado != true
                && !SemRespostaAutomatica(msg.Tipo, botaoId) && publicadorEventos is not null)
                await publicadorEventos.PublicarAsync(empresaId, ConversaAbertaEvent.TipoEvento, "Conversa", conversa.Id,
                    new ConversaAbertaEvent(conversa.Id, conversa.ClienteId), ct: ct);

            configuracao = await AtualizarUltimaMensagemRecebidaAsync(empresaId, enviadaEm);

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

        await DispararEfeitosAposGravarAsync(empresaId, conversa, mensagemEntidade, msg, configuracao, identificacao, ct);
        return true;
    }

    /// <summary>
    /// Depois do commit, nada aqui pode pedir reenvio: a mensagem já está gravada e o reenvio seria
    /// pulado pelo wamid. Falha é logada.
    /// </summary>
    private async Task DispararEfeitosAposGravarAsync(
        Guid empresaId, Conversa conversa, Mensagem mensagem, MensagemRecebidaWhatsApp msg,
        ConfiguracaoAtendimento configuracao, IdentificacaoCliente? identificacao, CancellationToken ct)
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

            // S24: conversa de cliente bloqueado já nasceu com a dona; nada automático responde.
            if (identificacao?.Cliente.Bloqueado == true)
            {
                await eventPublisher.PublicarAsync("conversa.mensagem_recebida", empresaId,
                    new { conversaId = conversa.Id, mensagemId = mensagem.Id }, ct);
                return;
            }

            // RN-01: a saudação sai antes do agente, sem LLM, para caber nos 5 s. Sem agente para cumprir
            // o "já te respondo" (botão "acao:", reação), não há saudação (issue 1285).
            if (identificacao is not null && !SemRespostaAutomatica(msg.Tipo, mensagem.BotaoId))
                await EnviarSaudacaoAsync(empresaId, conversa, configuracao, identificacao, ct);

            if (EhAcaoDeBotao(mensagem.BotaoId))
            {
                // S06: "acao:<nome>:<payload>" é resolvido sem LLM; o turno do agente não é enfileirado.
                await ExecutarAcaoDeBotaoAsync(empresaId, conversa, mensagem.BotaoId!, ct);
            }
            else if (TiposSemTurnoDoAgente.Contains(msg.Tipo))
            {
                // Reação, mensagem sem suporte ou de sistema: fica gravada para a dona; o agente não responde.
            }
            else if (await optOut.TentarAsync(empresaId, conversa, mensagem.Texto, DateTime.UtcNow, ct))
            {
                // S38: "SAIR" revogou o marketing do canal e já confirmou; o agente não responde.
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

    /// <summary>
    /// Roda a ação do botão e confirma. Falha aqui não derruba o webhook: a mensagem já foi gravada
    /// e a dona a vê no console.
    /// </summary>
    private async Task ExecutarAcaoDeBotaoAsync(Guid empresaId, Conversa conversa, string botaoId, CancellationToken ct)
    {
        try
        {
            if (await roteadorAcoes.ExecutarAsync(empresaId, conversa, botaoId, DateTime.UtcNow, ct))
                await unitOfWork.CommitAsync();
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Webhook WhatsApp: falha executando ação de botão na conversa {ConversaId}.", conversa.Id);
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

    /// <summary>
    /// Envia a saudação e grava como <c>Mensagem(Saida, Sistema)</c>. Falha aqui nunca derruba o
    /// processamento: a mensagem de entrada já foi confirmada e o agente ainda precisa ser enfileirado.
    /// </summary>
    private async Task EnviarSaudacaoAsync(
        Guid empresaId, Conversa conversa, ConfiguracaoAtendimento configuracao, IdentificacaoCliente identificacao, CancellationToken ct)
    {
        try
        {
            var texto = await saudacao.MontarAsync(empresaId, configuracao, identificacao, ct);

            Mensagem saida;
            try
            {
                var envio = await cloudClient.EnviarTextoAsync(conversa.ContatoIdExterno, texto, ct: ct);
                saida = Mensagem.Saida(empresaId, conversa.Id, AutorMensagem.Sistema, DateTime.UtcNow,
                    TipoConteudoMensagem.Texto, texto, envio.Wamid);
            }
            catch (Exception ex)
            {
                logger.LogWarning(ex, "Webhook WhatsApp: falha ao enviar a saudação da conversa {ConversaId}.", conversa.Id);
                saida = Mensagem.Saida(empresaId, conversa.Id, AutorMensagem.Sistema, DateTime.UtcNow,
                    TipoConteudoMensagem.Texto, texto);
                saida.RegistrarFalhaEnvio(ex.Message, ClassificadorFalhaEnvio.Classificar(ex), saida.EnviadaEm);
            }

            conversa.RegistrarSaida(saida.EnviadaEm);
            await conversaRepository.AddMensagemAsync(saida, ct);
            await unitOfWork.CommitAsync();
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Webhook WhatsApp: falha registrando a saudação da conversa {ConversaId}.", conversa.Id);
            // Não deixa o insert pendente contaminar o commit da próxima mensagem do lote.
            unitOfWork.DescartarAlteracoesPendentes();
        }
    }

    private async Task<ConfiguracaoAtendimento> AtualizarUltimaMensagemRecebidaAsync(Guid empresaId, DateTime agora)
    {
        var configuracao = await configuracaoAtendimentoRepository.GetByEmpresaIdAsync(empresaId);
        var nova = configuracao is null;
        configuracao ??= ConfiguracaoAtendimento.CriarPadrao(empresaId);
        configuracao.RegistrarMensagemRecebida(agora);

        if (nova)
            await configuracaoAtendimentoRepository.AddAsync(configuracao);
        else
            await configuracaoAtendimentoRepository.UpdateAsync(configuracao);

        return configuracao;
    }

    private static bool EhAcaoDeBotao(string? botaoId) =>
        botaoId is not null && botaoId.StartsWith("acao:", StringComparison.Ordinal);

    private static bool SemRespostaAutomatica(string tipo, string? botaoId) =>
        EhAcaoDeBotao(botaoId) || TiposSemTurnoDoAgente.Contains(tipo);

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
