using System.Text.Json;
using EasyStock.Application.Ports.Output.Notifications;
using EasyStock.Domain.Entities.Notifications;
using EasyStock.Domain.Enums.Notifications;

namespace EasyStock.Application.Services.Notifications;

public sealed class NotificadorService(
    IEventoNotificacaoRepository eventoRepository,
    IRotinaRepository rotinaRepository,
    ITemplateRepository templateRepository,
    IConsentimentoRepository consentimentoRepository,
    IConfiguracaoCanalRepository configuracaoCanalRepository,
    IBloqueioNotificacaoRepository bloqueioRepository,
    IOutboxNotificacaoRepository outboxRepository,
    IRendererTemplate renderer,
    ResolvedorCanal resolvedorCanal,
    IUnitOfWork unitOfWork,
    ILogger<NotificadorService> logger,
    IResolvedorAudiencia? resolvedorAudiencia = null) : INotificadorService
{
    /// <summary>
    /// Chave do payload com a identidade de negócio do aviso (S13: <c>pedidoId|status</c>). Presente, vira a
    /// <c>IdempotencyKey</c> do outbox no lugar do id do evento.
    /// </summary>
    public const string ChaveIdempotenciaPayload = "chaveIdempotencia";

    /// <summary>
    /// Instante UTC (ISO 8601) a partir do qual a mensagem pode sair (S26). Sem a chave, sai na hora.
    /// </summary>
    public const string EnviarAposPayload = "enviarApos";

    /// <summary>Chave do payload que marca o evento como teste (N13): o assunto sai prefixado por <see cref="PrefixoTeste"/>.</summary>
    public const string TestePayload = "teste";

    public const string PrefixoTeste = "[TESTE] ";

    private readonly ConstrutorMensagemOutbox _construtor = new(templateRepository, renderer);

    public async Task PublicarEventoAsync(
        TipoEventoNotificacao tipo,
        Guid empresaId,
        Guid? usuarioDestinoId,
        string payloadJson,
        IDictionary<string, object?>? varsAdicionais = null,
        CancellationToken ct = default)
    {
        var evento = EventoNotificacao.Criar(tipo, empresaId, payloadJson);
        await eventoRepository.AddAsync(evento, ct);

        await ProcessarEventoInternoAsync(evento, usuarioDestinoId, varsAdicionais, ct);

        await unitOfWork.CommitAsync();
    }

    public async Task<Guid> EnfileirarEventoAsync(
        TipoEventoNotificacao tipo,
        Guid empresaId,
        string payloadJson,
        Guid? refEntidadeId = null,
        CancellationToken ct = default,
        string? correlationId = null)
    {
        // ADR-0030: só estagia o evento Pendente na UoW atual (sem ProcessarEventoInternoAsync,
        // sem CommitAsync). O caller commita junto com a mutação de negócio (atômico). O
        // Avaliador processa fora de banda — nada aguardado/falível após o commit do negócio.
        var evento = EventoNotificacao.Criar(tipo, empresaId, payloadJson, refEntidadeId, correlationId);
        await eventoRepository.AddAsync(evento, ct);
        return evento.Id;
    }

    public async Task AvaliarEventoAsync(EventoNotificacao evento, CancellationToken ct = default)
    {
        Guid? usuarioDestinoId = null;

        try
        {
            var doc = JsonDocument.Parse(evento.PayloadJson);
            if (doc.RootElement.TryGetProperty("usuarioId", out var uid) &&
                Guid.TryParse(uid.GetString(), out var parsedId))
            {
                usuarioDestinoId = parsedId;
            }
        }
        catch (JsonException) { /* payload inválido — continua sem usuário específico */ }

        try
        {
            await ProcessarEventoInternoAsync(evento, usuarioDestinoId, varsAdicionais: null, ct);
            await unitOfWork.CommitAsync();
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested)
        {
            // Shutdown — não marca como falhado, evento volta a ser pendente na próxima rodada.
            throw;
        }
        catch (Exception ex)
        {
            // O commit que falhou deixa a entidade inválida rastreada: sem descartá-la, o commit seguinte reenvia o
            // mesmo INSERT e falha também (veneno em cascata, N1). Limpa antes de gravar o desfecho do evento.
            unitOfWork.DescartarAlteracoesPendentes();

            try
            {
                if (unitOfWork.EhViolacaoDeUnicidade(ex))
                {
                    // 23505 na IdempotencyKey do outbox: outra avaliação já enfileirou esta mensagem. O fato está
                    // coberto, o evento fecha como processado e não como erro.
                    logger.LogInformation(
                        "Evento {EventoId} (Tipo={Tipo}) já tinha a mensagem enfileirada (23505) — processado",
                        evento.Id, evento.Tipo);
                    evento.MarcarComoProcessado();
                }
                else
                {
                    // Defesa contra "evento veneno": qualquer exceção não tratada vira Falhado
                    // pra evitar loop infinito com starvation dos demais (orderBy OcorridoEm + Take 200).
                    logger.LogError(ex,
                        "Falha não recuperável ao avaliar evento {EventoId} (Tipo={Tipo}) — marcado como Falhado",
                        evento.Id, evento.Tipo);
                    evento.MarcarComoFalhado($"Erro não tratado: {ex.GetType().Name}: {ex.Message}");
                }

                await eventoRepository.UpdateAsync(evento, ct);
                await unitOfWork.CommitAsync();
            }
            catch (Exception saveEx)
            {
                // Se nem conseguimos persistir o status do evento, propaga o erro original.
                logger.LogError(saveEx,
                    "Erro adicional ao tentar gravar o desfecho do evento {EventoId}",
                    evento.Id);
                throw;
            }
        }
    }

    private async Task ProcessarEventoInternoAsync(
        EventoNotificacao evento,
        Guid? usuarioDestinoId,
        IDictionary<string, object?>? varsAdicionais,
        CancellationToken ct)
    {
        var agora = DateTime.UtcNow;

        var bloqueiosGlobais = await bloqueioRepository.ListarAtivosAsync(null, null, ct);
        var bloqueiosEmpresa = await bloqueioRepository.ListarAtivosAsync(evento.EmpresaId, null, ct);
        var todosBloqueios = bloqueiosGlobais.Concat(bloqueiosEmpresa).ToList();

        if (todosBloqueios.Any(b => b.EstaAtivo(agora) && b.Canal == null && b.EmpresaId == null))
        {
            logger.LogInformation("Kill switch global ativo — evento {EventoId} suprimido", evento.Id);
            await FecharEventoAsync(evento, ct);
            return;
        }

        var rotina = SeletorRotina.Escolher(
            await rotinaRepository.ListarAtivasAsync(evento.Tipo, evento.EmpresaId, ct), evento.EmpresaId);

        if (rotina is null)
        {
            logger.LogDebug(
                "Nenhuma rotina ativa para TipoEvento={TipoEvento} EmpresaId={EmpresaId}",
                evento.Tipo, evento.EmpresaId);
            await FecharEventoAsync(evento, ct);
            return;
        }

        // N5: a pausa geral da empresa cala o evento, menos o de segurança (a global já saiu acima).
        if (rotina.Categoria != CategoriaConteudoNotificacao.Seguranca
            && todosBloqueios.Any(b => b.EstaAtivo(agora) && b.Canal == null && b.EmpresaId == evento.EmpresaId))
        {
            logger.LogInformation("Pausa da empresa ativa — evento {EventoId} suprimido", evento.Id);
            await FecharEventoAsync(evento, ct);
            return;
        }

        var canaisPreferidos = CanaisDaRotina.Ler(rotina);
        var restricao = CanaisDaRotina.LerRestricao(evento.PayloadJson);
        if (restricao is not null)
            canaisPreferidos = canaisPreferidos.Where(restricao.Contains).ToList();

        if (canaisPreferidos.Count == 0)
        {
            await FecharEventoAsync(evento, ct);
            return;
        }

        var configuracoes = await configuracaoCanalRepository.ListarAsync(evento.EmpresaId, ct);
        var configuracoesFallback = await configuracaoCanalRepository.ListarAsync(null, ct);
        var todasConfiguracoes = configuracoes
            .Concat(configuracoesFallback.Where(gf => configuracoes.All(ef => ef.Canal != gf.Canal)))
            .ToList();

        // O InApp que a categoria Operacional acrescenta sozinho só vale com template do tipo; com restrição de canais
        // no payload não se acrescenta nada.
        var inAppTemTemplate = restricao is null
            && rotina.Categoria == CategoriaConteudoNotificacao.Operacional
            && !canaisPreferidos.Contains(CanalNotificacao.InApp)
            && await _construtor.TemTemplateAsync(rotina, evento, CanalNotificacao.InApp, ct);

        var vars = ConstrutorMensagemOutbox.LerVariaveis(evento.PayloadJson, varsAdicionais);

        // N4: a rotina que declara audiência manda para cada pessoa elegível (uma mensagem por pessoa e canal); sem ela, ou
        // com ela desligada, o destinatário sai das chaves do payload, como sempre.
        var audiencia = resolvedorAudiencia is null
            ? null
            : await resolvedorAudiencia.ResolverAsync(rotina, evento.EmpresaId, usuarioDestinoId, ct);

        var destinatarios = audiencia is null
            ? [new Destino(usuarioDestinoId, null, vars, null)]
            : audiencia.Select(p => new Destino(
                p.UsuarioId, p.Consentimentos, vars, new ContatoAudiencia(p.Email, p.Telefone))).ToList();

        if (destinatarios.Count == 0)
        {
            logger.LogInformation(
                "Audiência vazia para evento {EventoId} (Tipo={Tipo}, rotina {Rotina}): ninguém elegível",
                evento.Id, evento.Tipo, rotina.Codigo);
            await FecharEventoAsync(evento, ct);
            return;
        }

        var todos = CanaisDaRotina.LerModo(rotina.ParametrosJson) == ModoCanais.Todos;
        var chaveNegocio = vars.TryGetValue(ChaveIdempotenciaPayload, out var chave) && chave is string c
            && !string.IsNullOrWhiteSpace(c);

        var motivos = new List<string>();
        var criadas = 0;
        var repetidas = 0;
        var algumCanalPermitido = false;
        foreach (var destino in destinatarios)
        {
            var consentimentos = destino.Consentimentos
                ?? (usuarioDestinoId.HasValue
                    ? await consentimentoRepository.ListarPorUsuarioAsync(usuarioDestinoId.Value, ct)
                    : (IReadOnlyList<ConsentimentoNotificacao>)[]);

            var canaisPermitidos = resolvedorCanal.ResolverCanaisPermitidos(
                rotina.Categoria,
                canaisPreferidos,
                consentimentos,
                todasConfiguracoes,
                todosBloqueios,
                agora,
                evento.EmpresaId,
                inAppTemTemplate);

            if (canaisPermitidos.Count == 0)
            {
                logger.LogInformation(
                    "Nenhum canal permitido para evento {EventoId} usuário {UsuarioId}",
                    evento.Id, destino.UsuarioId);
                continue;
            }

            algumCanalPermitido = true;
            var mensagemPara = new DestinatarioMensagem(destino.UsuarioId, destino.Variaveis, destino.Contato);
            for (var i = 0; i < canaisPermitidos.Count; i++)
            {
                var canal = canaisPermitidos[i];
                // Fallback: os canais seguintes ficam para o dispatcher tentar se este falhar. Todos: cada mensagem é
                // independente, sem fallback (mandar de novo por outro canal duplicaria).
                IReadOnlyList<CanalNotificacao> restantes = todos ? [] : canaisPermitidos.Skip(i + 1).ToList();

                var resultado = await _construtor.ConstruirAsync(evento, rotina, canal, mensagemPara, restantes, agora, ct);
                if (resultado.Mensagem is null)
                {
                    logger.LogWarning(
                        "Canal {Canal} pulado no evento {EventoId}: {Motivo}", canal, evento.Id, resultado.Detalhe);
                    motivos.Add(resultado.Detalhe ?? $"Canal {canal} sem mensagem");
                    continue;
                }

                if (chaveNegocio && await outboxRepository.ExisteAsync(resultado.Mensagem.IdempotencyKey, ct))
                {
                    logger.LogInformation(
                        "Evento {EventoId} repete um fato já enfileirado (Tipo={Tipo} canal {Canal}) — sem nova mensagem",
                        evento.Id, evento.Tipo, canal);
                    repetidas++;
                    if (!todos) break;
                    continue;
                }

                await outboxRepository.AddAsync(resultado.Mensagem, ct);
                criadas++;
                if (!todos) break;
            }
        }

        if (!algumCanalPermitido)
        {
            await FecharEventoAsync(evento, ct);
            return;
        }

        if (criadas == 0 && repetidas == 0)
        {
            evento.MarcarComoFalhado(string.Join("; ", motivos));
            await eventoRepository.UpdateAsync(evento, ct);
            return;
        }

        await FecharEventoAsync(evento, ct);
    }

    /// <summary>Um destinatário do evento: o do payload (sem contato de audiência) ou uma pessoa da audiência.</summary>
    private sealed record Destino(
        Guid? UsuarioId,
        IReadOnlyList<ConsentimentoNotificacao>? Consentimentos,
        IDictionary<string, object?> Variaveis,
        ContatoAudiencia? Contato);

    private async Task FecharEventoAsync(EventoNotificacao evento, CancellationToken ct)
    {
        evento.MarcarComoProcessado();
        await eventoRepository.UpdateAsync(evento, ct);
    }
}
