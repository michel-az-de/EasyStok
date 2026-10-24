using EasyStock.Application.Ports.Output.Ai;
using EasyStock.Application.Ports.Output.Atendimento;
using EasyStock.Application.Ports.Output.Persistence.Atendimento;
using EasyStock.Application.Services.Atendimento.Ferramentas;
using EasyStock.Application.UseCases.Atendimento;
using EasyStock.Application.UseCases.Cliente.Dossie;
using EasyStock.Domain.Entities.Atendimento;
using EasyStock.Domain.Enums.Atendimento;

namespace EasyStock.Application.Services.Atendimento;

/// <summary>O que o turno fez: útil para log e teste.</summary>
public sealed record ResultadoTurnoAgente(bool ChamouLlm, bool Respondeu, bool Escalou)
{
    public static readonly ResultadoTurnoAgente Ignorado = new(false, false, false);
}

/// <summary>
/// Turno do agente de atendimento (S06): monta o contexto (últimas 20 mensagens, dossiê do cliente,
/// notas <c>[interno]</c>, tom de S08), chama o LLM com as ferramentas, executa as ferramentas pedidas
/// e repete até a resposta final, no máximo <see cref="MaximoIteracoes"/> chamadas. Grava a resposta
/// como <c>Mensagem(Saida, Agente)</c>, envia pela Cloud API e registra o consumo em <c>UsoIa</c>.
///
/// <para>Guardas: conversa que não está <see cref="SituacaoConversa.Automatica"/> (RN-04), fora da janela
/// de 24 h, ou sem entrada pendente do cliente (job repetido) → não chama o LLM nem envia nada. Atendimento
/// automático desligado na configuração (#1475), LLM indisponível (<c>Anthropic:Enabled=false</c> ou sem
/// chave) ou canal sem agente → escala para a dona.
/// Se a dona assumir durante a chamada ao LLM, a resposta é descartada (#1288).</para>
/// </summary>
public sealed class AgenteAtendimentoService(
    IAgenteLlmClient llm,
    IConversaRepository conversaRepository,
    IConfiguracaoAtendimentoRepository configuracaoRepository,
    IClienteRepository clienteRepository,
    IEnumerable<IFerramentaAgente> ferramentas,
    IEscaladorConversa escalador,
    IWhatsAppCloudClient cloudClient,
    IUsoIaRepository usoIaRepository,
    IUnitOfWork unitOfWork,
    ObterDossieClienteUseCase dossieUseCase,
    ICadernoRepository caderno,
    ILogger<AgenteAtendimentoService> logger)
{
    public const int MaximoIteracoes = 6;
    public const int UltimasMensagens = 20;

    private readonly IReadOnlyDictionary<string, IFerramentaAgente> _ferramentas =
        ferramentas.ToDictionary(f => f.Nome, StringComparer.Ordinal);

    public const string MotivoAgenteDesligado = "agente desligado (LLM indisponível)";
    public const string MotivoAtendimentoAutomaticoDesligado = "atendimento automático desligado na configuração";

    public async Task<ResultadoTurnoAgente> ProcessarTurnoAsync(Guid empresaId, Guid conversaId, DateTime agora, CancellationToken ct = default)
    {
        var dados = await conversaRepository.ObterComMensagensAsync(empresaId, conversaId, UltimasMensagens, ct);
        if (dados is null) return ResultadoTurnoAgente.Ignorado;

        var conversa = dados.Conversa;
        if (conversa.Situacao != SituacaoConversa.Automatica || !conversa.DentroDaJanela(agora))
            return ResultadoTurnoAgente.Ignorado;

        var pendentes = EntradasPendentes(dados.Mensagens);
        if (pendentes.Count == 0)
            return ResultadoTurnoAgente.Ignorado;

        var configuracao = await configuracaoRepository.GetByEmpresaIdAsync(empresaId)
            ?? ConfiguracaoAtendimento.CriarPadrao(empresaId);

        // Ninguém responderia: a dona desligou o atendimento automático no console (#1475), LLM desligado
        // (Anthropic:Enabled=false ou sem chave) ou canal sem agente (o agente envia pelo cliente do WhatsApp;
        // em outro canal sairia pelo canal errado). Passa de fato para a dona, senão a conversa fica
        // Automatica sem ninguém e fora do lembrete (#1288).
        var motivoSemAgente = !configuracao.Ativo ? MotivoAtendimentoAutomaticoDesligado
            : !llm.Disponivel ? MotivoAgenteDesligado
            : !conversa.TemAgente ? $"o canal {conversa.Canal} não tem agente"
            : null;
        if (motivoSemAgente is not null)
        {
            logger.LogInformation("Agente: conversa {ConversaId} fica para a dona ({Motivo}).", conversaId, motivoSemAgente);
            await escalador.EscalarAsync(empresaId, conversa, motivoSemAgente, agora, ct);
            await unitOfWork.CommitAsync();
            return new ResultadoTurnoAgente(ChamouLlm: false, Respondeu: false, Escalou: true);
        }

        var mensagens = GeracaoAgenteAtendimento.MontarConversacao(dados.Mensagens, pendentes);
        if (mensagens.Count == 0 || mensagens[^1].Papel != MensagemLlm.Usuario)
            return ResultadoTurnoAgente.Ignorado;

        var cliente = conversa.ClienteId is { } clienteId
            ? await clienteRepository.GetByIdAsync(empresaId, clienteId)
            : null;

        // S24: bloqueado no meio da conversa → a dona assume; o agente não responde.
        if (cliente?.Bloqueado == true)
        {
            await escalador.EscalarAsync(empresaId, conversa, EscalarConversaUseCase.MotivoClienteBloqueado(cliente), agora, ct);
            await unitOfWork.CommitAsync();
            return new ResultadoTurnoAgente(ChamouLlm: false, Respondeu: false, Escalou: true);
        }

        var system = await GeracaoAgenteAtendimento.MontarSystemAsync(
            empresaId, configuracao, conversa, cliente, dados.Mensagens, agora, caderno, dossieUseCase, ct);
        var gravacao = new GravacaoSaidaTurno(conversaRepository, unitOfWork, logger, empresaId, dados.Mensagens, agora);
        var contexto = new ContextoTurnoAgente(empresaId, conversa, agora, gravacao.GravarAsync);

        var geracao = await GeracaoAgenteAtendimento.GerarAsync(
            llm, system, mensagens, _ferramentas, contexto, MaximoIteracoes, logger, ct);
        var tokens = geracao.Tokens;
        var motivoEscalada = geracao.MotivoFalha;
        var concluiu = geracao.Concluiu;

        // As entradas que foram ao LLM ficam processadas: um job repetido não responde de novo e a
        // mensagem que chegar durante o turno continua pendente para o próximo (#1288).
        await gravacao.MarcarProcessadasAsync(ct);

        // A dona pode ter assumido (ou encerrado) enquanto o LLM respondia: a Situacao carregada no
        // início está velha. Fora do automático o agente não envia nem escala (#1288).
        if (await conversaRepository.ObterSituacaoAsync(empresaId, conversaId, ct) != SituacaoConversa.Automatica)
        {
            logger.LogInformation("Agente: conversa {ConversaId} saiu do automático durante o turno; resposta descartada.", conversaId);
            if (tokens > 0)
                await RegistrarUsoAsync(empresaId, tokens, agora);
            await unitOfWork.CommitAsync();
            return new ResultadoTurnoAgente(ChamouLlm: true, Respondeu: contexto.HouveEnvio,
                Escalou: conversa.Situacao == SituacaoConversa.Assumida);
        }

        var respondeu = contexto.HouveEnvio;
        const string encaminhamento = "Vou encaminhar sua mensagem para a equipe conferir e responder por aqui.";
        if (conversa.Situacao == SituacaoConversa.Assumida)
        {
            respondeu = await EnviarRespostaAsync(gravacao, conversa, encaminhamento, ct) || respondeu;
        }
        else if (conversa.Situacao == SituacaoConversa.Encerrada && contexto.RespostaVerificada is { Length: > 0 } despedida)
            respondeu = await EnviarRespostaAsync(gravacao, conversa, despedida, ct) || respondeu;
        else if (conversa.Situacao == SituacaoConversa.Automatica)
        {
            if (concluiu && motivoEscalada is null && contexto.RespostaVerificada is { Length: > 0 } verificada)
                respondeu = await EnviarRespostaAsync(gravacao, conversa, verificada, ct) || respondeu;
            else if (!contexto.EnvioVerificado || motivoEscalada is not null || !concluiu)
            {
                // #1704: a conversa vai ao cliente; afirmação que nenhuma fonte do turno sustenta fica para a equipe.
                var barrada = !concluiu || motivoEscalada is not null ? null
                    : contexto.FerramentaFalhou ? "ferramenta falhou no turno"
                    : GuardaRespostaAgente.Avaliar(geracao.Texto,
                        contexto.ResultadosDasFerramentas,
                        _ferramentas.Keys);
                if (concluiu && motivoEscalada is null && barrada is null)
                    respondeu = await EnviarRespostaAsync(gravacao, conversa, geracao.Texto!, ct) || respondeu;
                else
                {
                    respondeu = await EnviarRespostaAsync(gravacao, conversa, encaminhamento, ct) || respondeu;
                    motivoEscalada ??= concluiu ? $"resposta do agente barrada ({barrada}); precisa de conferência humana"
                        : $"o agente atingiu o limite de {MaximoIteracoes} iterações sem concluir a resposta";
                }
            }
        }

        if (motivoEscalada is not null && conversa.Situacao == SituacaoConversa.Automatica)
            await escalador.EscalarAsync(empresaId, conversa, motivoEscalada, agora, ct);

        if (tokens > 0)
            await RegistrarUsoAsync(empresaId, tokens, agora);

        await unitOfWork.CommitAsync();
        return new ResultadoTurnoAgente(true, respondeu,
            motivoEscalada is not null || conversa.Situacao == SituacaoConversa.Assumida);
    }

    private async Task<bool> EnviarRespostaAsync(GravacaoSaidaTurno gravacao, Conversa conversa, string texto, CancellationToken ct)
    {
        var (empresaId, agora) = (gravacao.EmpresaId, gravacao.Agora);
        if (string.IsNullOrWhiteSpace(texto)) return false;
        texto = TextoWhatsApp.SemTravessao(texto);
        if (texto.Length > Mensagem.TextoTamanhoMaximo) texto = texto[..Mensagem.TextoTamanhoMaximo];

        Mensagem saida;
        var enviou = false;
        try
        {
            var envio = await cloudClient.EnviarTextoAsync(conversa.ContatoIdExterno, texto, ct: ct);
            saida = Mensagem.Saida(empresaId, conversa.Id, AutorMensagem.Agente, agora, TipoConteudoMensagem.Texto, texto, envio.Wamid);
            enviou = true;
        }
        catch (Exception ex) when (ex is not OperationCanceledException || !ct.IsCancellationRequested)
        {
            logger.LogWarning(ex, "Agente de atendimento: falha ao enviar a resposta da conversa {ConversaId}.", conversa.Id);
            saida = Mensagem.Saida(empresaId, conversa.Id, AutorMensagem.Agente, agora, TipoConteudoMensagem.Texto, texto);
            saida.RegistrarFalhaEnvio(ClassificadorFalhaEnvio.Motivo(ex), ClassificadorFalhaEnvio.Classificar(ex), saida.EnviadaEm);
        }

        // encerrar_conversa pode ter fechado a conversa neste turno: a despedida ainda vai para o histórico.
        if (conversa.EstaAberta) conversa.RegistrarSaida(agora);
        await conversaRepository.AddMensagemAsync(saida, ct);
        await gravacao.GravarAsync(ct);
        return enviou;
    }

    private async Task RegistrarUsoAsync(Guid empresaId, int tokens, DateTime agora)
    {
        var uso = await usoIaRepository.GetAsync(empresaId, agora.Year, agora.Month);
        if (uso is null)
        {
            await usoIaRepository.AddAsync(new UsoIa
            {
                Id = Guid.NewGuid(),
                EmpresaId = empresaId,
                Ano = agora.Year,
                Mes = agora.Month,
                TotalGeracoes = 1,
                TotalTokens = tokens,
                AtualizadoEm = agora
            });
            return;
        }

        uso.TotalGeracoes++;
        uso.TotalTokens += tokens;
        uso.AtualizadoEm = agora;
        await usoIaRepository.UpdateAsync(uso);
    }

    /// <summary>
    /// Entradas do cliente que ninguém respondeu ainda (#1288). A ordem por <c>EnviadaEm</c> não basta:
    /// a entrada usa o timestamp da Meta (truncado em segundos) e a resposta do agente o início do
    /// turno, então uma mensagem que chegou durante o turno pode ficar antes da resposta. Vale
    /// <see cref="Mensagem.ProcessadaEm"/>: entrada não processada é pendente, salvo se a dona escreveu
    /// depois dela. Janela sem nenhuma entrada processada (histórico anterior ao #1288) segue a regra
    /// antiga: qualquer resposta posterior, do agente ou da dona, conta.
    /// </summary>
    private static IReadOnlyList<Mensagem> EntradasPendentes(IReadOnlyList<Mensagem> historico)
    {
        var legado = !historico.Any(m => m.Autor == AutorMensagem.Cliente && m.ProcessadaEm is not null);
        var pendentes = new List<Mensagem>();
        for (var i = 0; i < historico.Count; i++)
        {
            var msg = historico[i];
            if (msg.Autor != AutorMensagem.Cliente || msg.ProcessadaEm is not null) continue;

            var respondida = historico.Skip(i + 1).Any(m =>
                m.Autor.EhHumanoDaEmpresa() || (legado && m.Autor == AutorMensagem.Agente));
            if (!respondida) pendentes.Add(msg);
        }

        return pendentes;
    }

    /// <summary>
    /// Grava cada saída do turno logo depois do envio (#1791), junto com as entradas que o turno levou ao LLM
    /// marcadas como processadas: o status e o eco da Meta encontram a mensagem pelo wamid, e um commit final que
    /// falhe não faz o próximo job responder de novo. Falha aqui não derruba o envio: o que ficou rastreado vai no
    /// commit seguinte.
    /// </summary>
    private sealed class GravacaoSaidaTurno(
        IConversaRepository conversaRepository, IUnitOfWork unitOfWork, ILogger logger, Guid empresaId, IReadOnlyList<Mensagem> historico, DateTime agora)
    {
        private bool _entradasMarcadas;

        public Guid EmpresaId => empresaId;
        public DateTime Agora => agora;

        public async Task GravarAsync(CancellationToken ct)
        {
            try
            {
                await MarcarProcessadasAsync(ct);
                await unitOfWork.CommitAsync();
            }
            catch (Exception ex) when (ex is not OperationCanceledException || !ct.IsCancellationRequested)
            {
                logger.LogWarning(ex, "Agente: falha ao gravar a saída já enviada; segue para o commit do fim do turno.");
            }
        }

        /// <summary>
        /// Marca no rastreamento, uma vez por turno, as entradas que foram ao LLM. O histórico vem sem tracking:
        /// a entidade rastreada é buscada pelo <c>wamid</c>.
        /// </summary>
        public async Task MarcarProcessadasAsync(CancellationToken ct)
        {
            if (_entradasMarcadas) return;
            foreach (var entrada in historico.Where(m => m.Autor == AutorMensagem.Cliente && m.ProcessadaEm is null))
            {
                if (entrada.ExternoId is not { } externoId) continue;
                var rastreada = await conversaRepository.ObterMensagemPorExternoIdAsync(empresaId, externoId, ct);
                rastreada?.MarcarProcessada(agora);
            }
            _entradasMarcadas = true;
        }
    }
}
