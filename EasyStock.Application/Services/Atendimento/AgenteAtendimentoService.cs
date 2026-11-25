using System.Diagnostics;
using EasyStock.Application.Ports.Output.Ai;
using EasyStock.Application.Ports.Output.Atendimento;
using EasyStock.Application.Ports.Output.Persistence.Atendimento;
using EasyStock.Application.Services.Atendimento.Ferramentas;
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
/// e repete até a resposta final, no máximo <see cref="MaximoIteracoes"/> voltas. Grava a resposta
/// como <c>Mensagem(Saida, Agente)</c>, envia pela Cloud API e registra o turno e o consumo dele em <c>TurnoAgente</c>.
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
    IUnitOfWork unitOfWork,
    ObterDossieClienteUseCase dossieUseCase,
    ICadernoRepository caderno,
    ILogger<AgenteAtendimentoService> logger,
    // #3298 (R7): o controle de custo (registro do turno, limite de respostas e preços) é regra de negócio: sem ele a cota
    // vira "não se aplica" e o turno não é registrado, sem erro. Obrigatório, a fiação errada falha ao montar o serviço.
    ITurnoAgenteRepository turnos,
    IOptions<LimiteRespostasAgenteOptions> limiteRespostas,
    IOptions<PrecosModelosAgenteOptions> precos,
    IPedidoRepository? pedidos = null,
    Notifications.IContatoDaEquipeAvisada? equipe = null,
    IOptions<ModelosAprovadosAgenteOptions>? modelos = null,
    IOptions<RetomadaAssistenteOptions>? retomadaOpcoes = null,
    IOperacaoEventPublisher? eventos = null,
    IOptions<PrazoDoTurnoAgenteOptions>? prazoTurno = null)
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

    /// <param name="retomada">#3068: turno da varredura de conversa parada com a equipe; o turno decide se retoma.</param>
    public async Task<ResultadoTurnoAgente> ProcessarTurnoAsync(
        Guid empresaId, Guid conversaId, DateTime agora, CancellationToken ct = default, bool retomada = false)
    {
        var cronometro = Stopwatch.StartNew();
        var dados = await conversaRepository.ObterComMensagensAsync(empresaId, conversaId, UltimasMensagens, ct);
        if (dados is null) return ResultadoTurnoAgente.Ignorado;

        var conversa = dados.Conversa;
        var prazoRetomada = (retomadaOpcoes?.Value ?? new RetomadaAssistenteOptions()).Prazo;
        Mensagem? entradaRetomada = null;
        if (retomada)
        {
            if (await TentarRetomarAsync(empresaId, dados, agora, prazoRetomada, ct) is not var (relido, entrada))
                return ResultadoTurnoAgente.Ignorado;
            // O turno segue com o que foi lido sob o lock: é o histórico em que a retomada foi decidida.
            (dados, conversa, entradaRetomada) = (relido, relido.Conversa, entrada);
            // #3121: fora da transação da retomada. Dentro dela o commit ainda não aconteceu e a conversa está travada.
            await AvisarConsoleDaRetomadaAsync(empresaId, conversaId, ct);
        }

        if (PortoesDoAssistente.DaConversa(conversa, agora).FirstOrDefault(e => e.Barra) is { } portaoDaConversa)
        {
            // #3121: situação e vínculo em revisão barram a cada mensagem do cliente enquanto durarem, e a tela já os
            // mostra; no log viram ruído.
            if (portaoDaConversa.Codigo is not (PortoesDoAssistente.Situacao or PortoesDoAssistente.Vinculo))
                LogsDoAssistente.PortaoBarrou(logger, conversaId, portaoDaConversa.Codigo);
            return ResultadoTurnoAgente.Ignorado;
        }

        var pendentes = EntradasPendentes(dados.Mensagens);
        // #3068: a entrada que o robô já tinha processado ao escalar volta como entrada nova do turno retomado, mesmo
        // com reação ou figurinha pendente depois dela: sem isso o modelo responderia à reação e não à pergunta.
        if (entradaRetomada is not null && pendentes.All(m => m.Id != entradaRetomada.Id))
            pendentes = [.. pendentes.Append(entradaRetomada).OrderBy(m => m.EnviadaEm)];
        if (pendentes.Count == 0)
            return ResultadoTurnoAgente.Ignorado;

        var configuracao = await configuracaoRepository.GetByEmpresaIdAsync(empresaId)
            ?? ConfiguracaoAtendimento.CriarPadrao(empresaId);

        // Ninguém responderia: a dona desligou o atendimento automático no console (#1475), LLM desligado
        // (Anthropic:Enabled=false ou sem chave) ou canal sem agente (o agente envia pelo cliente do WhatsApp;
        // em outro canal sairia pelo canal errado). Passa de fato para a dona, senão a conversa fica
        // Automatica sem ninguém e fora do lembrete (#1288).
        // #2211: quem recebe os avisos pelo número da loja e responde a eles fala com gente, nunca com o robô,
        // até a equipe devolver a conversa a ele (#2493). As etapas são as mesmas da checagem do console (#2496).
        var portoes = new PortoesDoAssistente(llm, conversaRepository, turnos, limiteRespostas, equipe);
        if ((await portoes.DaLojaAsync(empresaId, configuracao, conversa, ct)).FirstOrDefault(e => e.Barra) is { } portaoDaLoja)
            return await EscalarPeloPortaoAsync(portaoDaLoja);

        var mensagens = GeracaoAgenteAtendimento.MontarConversacao(dados.Mensagens, pendentes);
        if (mensagens.Count == 0 || mensagens[^1].Papel != MensagemLlm.Usuario)
            return ResultadoTurnoAgente.Ignorado;

        var cliente = conversa.ClienteId is { } clienteId
            ? await clienteRepository.GetByIdAsync(empresaId, clienteId)
            : null;

        // S24: bloqueado no meio da conversa → a dona assume; o agente não responde.
        // #2249: pessoa encaminhada à responsável. Fica com a equipe até alguém devolver a conversa ao robô.
        // #2285: anti-loop de custo. Outro robô do lado do cliente (ou um loop de mensagens) faria o agente
        // responder e gastar sem fim; no limite da hora, a conversa para nele e a escalada avisa a dona.
        // S69 (#2853): cota mensal esgotada. Não chama o modelo, passa para a equipe e deixa o turno no histórico como cota.
        if (await portoes.PrimeiraQueBarraDoClienteEDoCustoAsync(empresaId, conversaId, configuracao, cliente, agora, ct) is { } portao)
            return await EscalarPeloPortaoAsync(portao);

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

        // #3149: dicionário gravado que a leitura descarta não some calado (uma linha por turno, sem o conteúdo).
        if (DicionarioDaLoja.Descartado(configuracao.DicionarioJson)) LogsDoAssistente.DicionarioDescartado(logger, conversaId);
        var system = await GeracaoAgenteAtendimento.MontarSystemTurnoAsync(
            empresaId, configuracao, conversa, cliente, dados.Mensagens, agora, caderno, dossieUseCase, ct);
        var gravacao = new GravacaoSaidaTurno(conversaRepository, unitOfWork, logger, empresaId, dados.Mensagens, agora);
        var contexto = new ContextoTurnoAgente(empresaId, conversa, agora, gravacao.GravarAsync);
        var protecao = new ProtecaoVinculoTurno(conversaRepository, unitOfWork, contexto);
        // #2853: a desligada pela loja nem vai ao modelo; pedida assim mesmo, o laço responde "desconhecida" sem executar.
        var desligadas = FerramentasDaLoja.DaLoja(configuracao.FerramentasDesligadasJson);
        var ferramentasProtegidas = _ferramentas.Where(par => !desligadas.Contains(par.Key)).ToDictionary(par => par.Key,
            par => (IFerramentaAgente)new FerramentaProtegida(par.Value, protecao));

        // #3287 (R5): o id do turno nasce antes das chamadas, para chamada_ia apontar para ele.
        var turnoId = Guid.NewGuid();
        var geracao = await GeracaoAgenteAtendimento.GerarAsync(
            llm, system.Estavel, mensagens, ferramentasProtegidas, contexto, MaximoIteracoes, logger, ct,
            modelos?.Value.DoTurno(configuracao.ModeloAgente), // S68: modelo da loja só se aprovado
            // #2862: data e dossiê fora do bloco cacheado. #3068: a orientação da retomada vai junto, só neste turno.
            systemVolatil: system.Volatil + (entradaRetomada is null ? string.Empty : RetomadaDoAssistente.Orientacao(prazoRetomada)),
            turnoAgenteId: turnoId,
            // #3291 (R3): prazo do turno (relógio corrido, ferramentas incluídas) que só interrompe o modelo; ferramenta e envio terminam com o ct do serviço.
            prazoTotal: (prazoTurno?.Value ?? new PrazoDoTurnoAgenteOptions()).Prazo);
        // #3298 (R11): o fim do turno (resposta, guarda, escalada e registro) é do colaborador. Se a conversa mudou de mãos
        // durante a chamada ao modelo, a proteção descarta o fim e o turno só fica no histórico.
        var turno = new FinalizacaoDoTurno.Turno(
            empresaId, conversaId, agora, conversa, cliente, configuracao, dados.Mensagens, entradaRetomada,
            pendentes[^1].Id, turnoId, cronometro, geracao, contexto, gravacao, _ferramentas.Keys);
        var finalizacao = new FinalizacaoDoTurno(conversaRepository, escalador, cloudClient, unitOfWork, turnos, precos, logger);
        return await protecao.ExecutarAsync(token => finalizacao.FinalizarAsync(turno, token), ct, gravar: false)
            ?? await finalizacao.InterrompidoAsync(turno, ct);

        // S67 (#2534): um registro por turno, só com números e ids. #3175: turno de portao que escala nao chama o modelo:
        // 0 tokens e o codigo do portao como motivo.
        async Task RegistrarTurnoDoPortaoAsync(DesfechoTurnoAgente desfecho, string codigoDoPortao)
        {
            await turnos.AddAsync(TurnoAgente.Registrar(empresaId, conversaId, pendentes[^1].Id, null, null, 0, 0, 0, null, [], 0,
                desfecho, (int)Math.Min(cronometro.ElapsedMilliseconds, int.MaxValue), agora, MotivoDoTurno.DoPortao(codigoDoPortao)), ct);
        }

        // O portão que barra passa a conversa para a equipe com o motivo dele e deixa o turno no histórico, sem chamar o
        // modelo (#3298, R11). A cota tem desfecho próprio; os demais, escalou. Toda etapa que escala traz motivo; sem ele
        // o detalhe vai no lugar, nunca um portão que barra deixa o modelo responder.
        async Task<ResultadoTurnoAgente> EscalarPeloPortaoAsync(EtapaAssistente portao)
        {
            LogsDoAssistente.PortaoBarrou(logger, conversaId, portao.Codigo);
            await escalador.EscalarAsync(empresaId, conversa, portao.MotivoEscalada ?? portao.Detalhe, agora, ct);
            await RegistrarTurnoDoPortaoAsync(
                portao.Codigo == PortoesDoAssistente.Cota ? DesfechoTurnoAgente.Cota : DesfechoTurnoAgente.Escalou, portao.Codigo);
            await unitOfWork.CommitAsync();
            return new ResultadoTurnoAgente(ChamouLlm: false, Respondeu: false, Escalou: true);
        }
    }

    /// <summary>
    /// #3068: devolve ao Assistente a conversa que a equipe deixou sem resposta além do prazo. Confere antes todos os
    /// portões menos "quem atende" (é o que a retomada muda): barrando, a conversa fica como a equipe deixou, sem nova
    /// escalada nem aviso. Grava a retomada e a nota interna antes do modelo, porque a proteção do turno relê a situação
    /// no banco a cada envio. Decide com a conversa travada: a equipe responde e assume sob o mesmo lock
    /// (<see cref="UseCases.Atendimento.Inbox.EnviarMensagemConsoleUseCase"/>), então quem chega depois relê e desiste.
    /// </summary>
    private async Task<(ConversaComMensagens Dados, Mensagem Entrada)?> TentarRetomarAsync(
        Guid empresaId, ConversaComMensagens lido, DateTime agora, TimeSpan prazo, CancellationToken ct)
    {
        // Triagem sem lock: a varredura reenfileira a conversa a cada passada até o prazo vencer.
        if (RetomadaDoAssistente.EntradaSemResposta(lido.Conversa, lido.Mensagens, agora, prazo) is null)
            return null;

        try
        {
            return await unitOfWork.ExecuteInTransactionSemRetryAsync<(ConversaComMensagens, Mensagem)?>(async token =>
            {
                if (await conversaRepository.ObterPorIdComLockAsync(empresaId, lido.Conversa.Id, token) is null)
                    return null;
                var dados = await conversaRepository.ObterComMensagensAsync(empresaId, lido.Conversa.Id, UltimasMensagens, token);
                if (dados is null
                    || RetomadaDoAssistente.EntradaSemResposta(dados.Conversa, dados.Mensagens, agora, prazo) is not { } entrada)
                    return null;
                return await RetomarTravadaAsync(empresaId, dados.Conversa, entrada, agora, prazo, token) ? (dados, entrada) : null;
            }, ct);
        }
        catch
        {
            // Rollback SQL não limpa as entidades rastreadas: a retomada desfeita não pode vazar para outro commit.
            unitOfWork.DescartarAlteracoesPendentes();
            throw;
        }
    }

    private async Task<bool> RetomarTravadaAsync(
        Guid empresaId, Conversa conversa, Mensagem entrada, DateTime agora, TimeSpan prazo, CancellationToken ct)
    {
        var configuracao = await configuracaoRepository.GetByEmpresaIdAsync(empresaId)
            ?? ConfiguracaoAtendimento.CriarPadrao(empresaId);
        var cliente = conversa.ClienteId is { } clienteId
            ? await clienteRepository.GetByIdAsync(empresaId, clienteId)
            : null;
        var portoes = new PortoesDoAssistente(llm, conversaRepository, turnos, limiteRespostas, equipe);
        // Todos os portões menos "quem atende", na ordem do turno, parando no primeiro que barra.
        var barra = await portoes.EtapasAsync(empresaId, conversa, configuracao, cliente, agora, ct)
            .FirstOrDefaultAsync(e => e.Barra && e.Codigo != PortoesDoAssistente.Situacao, ct);
        if (barra is not null)
        {
            LogsDoAssistente.RetomadaDispensada(logger, conversa.Id, barra.Codigo);
            // A marca evita a varredura reavaliar a mesma mensagem a cada passada; a próxima do cliente abre outra chance.
            conversa.DispensarRetomadaPorTempo(entrada.Id);
            await unitOfWork.CommitAsync();
            return false;
        }

        var motivoAnterior = conversa.MotivoEscalada;
        conversa.RetomarPorTempo(entrada.Id);
        await conversaRepository.AddMensagemAsync(
            Mensagem.Saida(empresaId, conversa.Id, AutorMensagem.Sistema, agora, TipoConteudoMensagem.Texto,
                RetomadaDoAssistente.Nota(prazo, motivoAnterior)),
            ct);
        await unitOfWork.CommitAsync();
        // Warning: produção só grava de Warning para cima, e a retomada precisa aparecer no log.
        logger.LogWarning("Agente: conversa {ConversaId} retomada pelo Assistente após {Segundos} s sem resposta da equipe.",
            conversa.Id, prazo.TotalSeconds);
        return true;
    }

    /// <summary>
    /// #3121: o console atualiza a conversa retomada sem esperar a próxima volta da sincronização. Melhor esforço, depois
    /// da transação da retomada: ela já está gravada e não pode cair por causa do aviso.
    /// </summary>
    private async Task AvisarConsoleDaRetomadaAsync(Guid empresaId, Guid conversaId, CancellationToken ct)
    {
        if (eventos is null) return;
        try
        {
            await eventos.PublicarAsync(EventosOperacao.ConversaAtualizada, empresaId, new ConversaAtualizadaOperacao(conversaId), ct);
        }
        catch (Exception ex) when (ex is not OperationCanceledException || !ct.IsCancellationRequested)
        {
            logger.LogWarning(ex, "Agente: aviso em tempo real da retomada falhou na conversa {ConversaId}.", conversaId);
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
            (ferramenta is CriarPedidoFerramenta or IFerramentaForaDaTransacao
                ? await protecao.ExecutarPedidoAsync(token => ferramenta.ExecutarAsync(contexto, entrada, token), ct)
                : await protecao.ExecutarAsync(token => ferramenta.ExecutarAsync(contexto, entrada, token), ct))
            ?? "O contexto da conversa mudou. Nenhuma ação foi executada.";
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
    internal static IReadOnlyList<Mensagem> EntradasPendentes(IReadOnlyList<Mensagem> historico)
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
}
