using EasyStock.Application.Ports.Output.Ai;
using EasyStock.Application.Ports.Output.Atendimento;
using EasyStock.Application.Ports.Output.Persistence.Atendimento;
using EasyStock.Application.Services.Atendimento.Ferramentas;
using EasyStock.Application.UseCases.Atendimento;
using EasyStock.Application.UseCases.Cliente.Dossie;
using EasyStock.Domain.Entities.Atendimento;
using EasyStock.Domain.Enums.Atendimento;
using Microsoft.Extensions.Options;

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
    ILogger<AgenteAtendimentoService> logger,
    IPedidoRepository? pedidos = null,
    Notifications.IContatoDaEquipeAvisada? equipe = null,
    IOptions<LimiteRespostasAgenteOptions>? limiteRespostas = null)
{
    public const int MaximoIteracoes = 6;
    public const int UltimasMensagens = 20;

    private readonly IReadOnlyDictionary<string, IFerramentaAgente> _ferramentas =
        ferramentas.ToDictionary(f => f.Nome, StringComparer.Ordinal);

    public const string MotivoAgenteDesligado = "agente desligado (LLM indisponível)";
    public const string MotivoAtendimentoAutomaticoDesligado = "atendimento automático desligado na configuração";
    public const string MotivoContatoDaEquipe = "contato da equipe que recebe os avisos: o robô não responde";

    public static string MotivoLimiteRespostas(int maximo) =>
        $"o robô respondeu {maximo} vezes nesta conversa na última hora; parou para não gastar sem limite (possível loop)";

    public async Task<ResultadoTurnoAgente> ProcessarTurnoAsync(Guid empresaId, Guid conversaId, DateTime agora, CancellationToken ct = default)
    {
        var dados = await conversaRepository.ObterComMensagensAsync(empresaId, conversaId, UltimasMensagens, ct);
        if (dados is null) return ResultadoTurnoAgente.Ignorado;

        var conversa = dados.Conversa;
        if (conversa.Situacao != SituacaoConversa.Automatica || conversa.VinculoEmRevisao || !conversa.DentroDaJanela(agora))
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
        // #2211: quem recebe os avisos pelo número da loja e responde a eles fala com gente, nunca com o robô.
        var motivoSemAgente = !configuracao.Ativo ? MotivoAtendimentoAutomaticoDesligado
            : !llm.Disponivel ? MotivoAgenteDesligado
            : !conversa.TemAgente ? $"o canal {conversa.Canal} não tem agente"
            : equipe is not null && await equipe.EhInscritoAsync(empresaId, conversa.ContatoIdExterno, ct) ? MotivoContatoDaEquipe
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

        // #2249: pessoa encaminhada à responsável. Fica com a equipe até alguém devolver a conversa ao robô.
        if (cliente?.AtendimentoAutomaticoPausado == true)
        {
            await escalador.EscalarAsync(empresaId, conversa, EscalarConversaUseCase.MotivoContatoPausado(cliente), agora, ct);
            await unitOfWork.CommitAsync();
            return new ResultadoTurnoAgente(ChamouLlm: false, Respondeu: false, Escalou: true);
        }

        // #2285: anti-loop de custo. Outro robô do lado do cliente (ou um loop de mensagens) faria o agente
        // responder e gastar sem fim; no limite da hora, a conversa para nele e a escalada avisa a dona.
        var maximoPorHora = limiteRespostas?.Value.MaximoPorHora ?? LimiteRespostasAgenteOptions.PadraoPorHora;
        if (maximoPorHora > 0
            && await conversaRepository.ContarRespostasDoAgenteDesdeAsync(empresaId, conversaId, agora.AddHours(-1), ct) >= maximoPorHora)
        {
            logger.LogWarning("Agente: conversa {ConversaId} chegou a {Maximo} respostas do robô na última hora; fica para a dona.",
                conversaId, maximoPorHora);
            await escalador.EscalarAsync(empresaId, conversa, MotivoLimiteRespostas(maximoPorHora), agora, ct);
            await unitOfWork.CommitAsync();
            return new ResultadoTurnoAgente(ChamouLlm: false, Respondeu: false, Escalou: true);
        }

        // #2126: o vínculo só é limpo em parte dos caminhos de cancelamento e entrega. Pedido já finalizado
        // não é atendimento em andamento: preso, a IA respondia "quero uma lasanha" com a comanda antiga e
        // não conseguia encerrar a conversa. Mesmo critério da reabertura (#1839). Solta sob a trava da
        // geração de pedido e grava já: o turno dura a chamada ao LLM e não pode apagar um pedido novo
        // vinculado pela página do cardápio nesse meio-tempo (#1238).
        if (pedidos is not null && conversa.PedidoEmAndamentoId is { } pedidoId
            && await pedidos.GetByIdAsync(empresaId, pedidoId) is { } pedido
            && pedido.EmpresaId == empresaId && pedido.EstaFinalizado)
            await unitOfWork.ExecuteInTransactionAsync(async token =>
            {
                var noBanco = await conversaRepository.TravarParaPedidoAsync(empresaId, conversaId, token);
                conversa.DefinirPedidoEmAndamento(noBanco == pedidoId ? null : noBanco);
                await unitOfWork.CommitAsync();
            }, ct);

        var system = await GeracaoAgenteAtendimento.MontarSystemAsync(
            empresaId, configuracao, conversa, cliente, dados.Mensagens, agora, caderno, dossieUseCase, ct);
        var gravacao = new GravacaoSaidaTurno(conversaRepository, unitOfWork, logger, empresaId, dados.Mensagens, agora);
        var contexto = new ContextoTurnoAgente(empresaId, conversa, agora, gravacao.GravarAsync);
        var protecao = new ProtecaoVinculoTurno(conversaRepository, unitOfWork, contexto);
        var ferramentasProtegidas = _ferramentas.ToDictionary(par => par.Key,
            par => (IFerramentaAgente)new FerramentaProtegida(par.Value, protecao));

        var geracao = await GeracaoAgenteAtendimento.GerarAsync(
            llm, system, mensagens, ferramentasProtegidas, contexto, MaximoIteracoes, logger, ct);
        var tokens = geracao.Tokens;
        var motivoEscalada = geracao.MotivoFalha;
        var concluiu = geracao.Concluiu;

        var final = await protecao.ExecutarAsync(Finalizar, ct, gravar: false);
        if (final is not null) return final;
        if (tokens > 0) await RegistrarUsoAsync(empresaId, tokens, agora);
        await unitOfWork.CommitAsync();
        return new ResultadoTurnoAgente(true, contexto.HouveEnvio, false);

        async Task<ResultadoTurnoAgente> Finalizar(CancellationToken token)
        {
        ct = token;

        // As entradas que foram ao LLM ficam processadas: um job repetido não responde de novo e a
        // mensagem que chegar durante o turno continua pendente para o próximo (#1288).
        await gravacao.MarcarProcessadasAsync(ct);

        var respondeu = contexto.HouveEnvio;
        // #2249: o texto de cada ação é molde da loja; quem não é cliente recebe o de encaminhamento.
        var primeiroNome = NomesDeContato.PrimeiroNome(cliente?.Nome) ?? NomesDeContato.PrimeiroNome(conversa.ContatoNome);
        var encaminhamento = MoldeMensagemContato.Preencher(configuracao.MensagemEscalada, primeiroNome);
        if (conversa.Situacao == SituacaoConversa.Assumida)
        {
            var aoContato = contexto.Encaminhou
                ? MoldeMensagemContato.Preencher(configuracao.MensagemEncaminhamento, primeiroNome)
                : encaminhamento;
            respondeu = await EnviarRespostaAsync(gravacao, conversa, aoContato, ct) || respondeu;
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
    }

    // A correção e cada efeito disputam a mesma linha. O LLM roda fora da transação;
    // o lock continua retido durante envio/mutação, sem repetir um efeito externo.
    private sealed class ProtecaoVinculoTurno(IConversaRepository conversas, IUnitOfWork uow, ContextoTurnoAgente contexto)
    {
        private readonly Guid? _clienteId = contexto.Conversa.ClienteId;
        private readonly long _revisao = contexto.Conversa.VinculoRevisao;
        private SituacaoConversa _situacao = contexto.Conversa.Situacao;

        public async Task<string?> ExecutarPedidoAsync(Func<CancellationToken, Task<string>> efeito, CancellationToken ct)
        {
            // CriarPedidoAtendimento confere o snapshot e grava pedido+vínculo sob seu próprio lock.
            // Depois desse commit, o histórico impede correção. A cobrança precisa rodar fora da transação.
            if (await ExecutarAsync(_ => Task.FromResult("validado"), ct, gravar: false) is null) return null;
            try { return await efeito(ct); }
            catch
            {
                contexto.Interromper();
                uow.DescartarAlteracoesPendentes();
                throw;
            }
        }

        public async Task<T?> ExecutarAsync<T>(Func<CancellationToken, Task<T>> efeito, CancellationToken ct, bool gravar = true) where T : class
        {
            if (contexto.Interrompido) return null;
            try
            {
                return await uow.ExecuteInTransactionSemRetryAsync(async token =>
                {
                    var atual = await conversas.ObterPorIdComLockAsync(contexto.EmpresaId, contexto.Conversa.Id, token);
                    if (atual is null || atual.EmpresaId != contexto.EmpresaId || atual.ClienteId != _clienteId
                        || atual.VinculoRevisao != _revisao || atual.VinculoEmRevisao || atual.Situacao != _situacao
                        || (_situacao == SituacaoConversa.Automatica
                            && await conversas.ObterSituacaoAsync(contexto.EmpresaId, contexto.Conversa.Id, token) != SituacaoConversa.Automatica))
                    {
                        contexto.Interromper();
                        return null;
                    }
                    var resultado = await efeito(token);
                    if (gravar) await uow.CommitAsync();
                    _situacao = contexto.Conversa.Situacao;
                    return resultado;
                }, ct);
            }
            catch
            {
                // Rollback SQL não limpa as entidades rastreadas, inclusive quando a ferramenta
                // chama CommitAsync por dentro. Nenhuma delas pode vazar para o commit de consumo.
                contexto.Interromper();
                uow.DescartarAlteracoesPendentes();
                throw;
            }
        }
    }

    private sealed class FerramentaProtegida(IFerramentaAgente ferramenta, ProtecaoVinculoTurno protecao) : IFerramentaAgente
    {
        public string Nome => ferramenta.Nome;
        public string Descricao => ferramenta.Descricao;
        public string SchemaJson => ferramenta.SchemaJson;
        public async Task<string> ExecutarAsync(ContextoTurnoAgente contexto, System.Text.Json.JsonElement entrada, CancellationToken ct = default) =>
            (ferramenta is CriarPedidoFerramenta
                ? await protecao.ExecutarPedidoAsync(token => ferramenta.ExecutarAsync(contexto, entrada, token), ct)
                : await protecao.ExecutarAsync(token => ferramenta.ExecutarAsync(contexto, entrada, token), ct))
            ?? "O contexto da conversa mudou. Nenhuma ação foi executada.";
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
    /// antiga: qualquer resposta posterior, do agente ou da dona, conta. Resposta que falhou ao enviar (#2004) não conta:
    /// o cliente não a recebeu.
    /// </summary>
    private static IReadOnlyList<Mensagem> EntradasPendentes(IReadOnlyList<Mensagem> historico)
    {
        var legado = !historico.Any(m => m.Autor == AutorMensagem.Cliente && m.ProcessadaEm is not null);
        var pendentes = new List<Mensagem>();
        for (var i = 0; i < historico.Count; i++)
        {
            var msg = historico[i];
            if (msg.Autor != AutorMensagem.Cliente || msg.ProcessadaEm is not null) continue;

            var respondida = historico.Skip(i + 1).Any(m => m.Status != StatusMensagem.Falhou
                && (m.Autor.EhHumanoDaEmpresa() || (legado && m.Autor == AutorMensagem.Agente)));
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
