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
/// e repete até a resposta final, no máximo <see cref="MaximoIteracoes"/> chamadas. Grava a resposta
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
    IPedidoRepository? pedidos = null,
    Notifications.IContatoDaEquipeAvisada? equipe = null,
    IOptions<LimiteRespostasAgenteOptions>? limiteRespostas = null,
    ITurnoAgenteRepository? turnos = null,
    IOptions<PrecosModelosAgenteOptions>? precos = null,
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
        var portoes = new PortoesDoAssistente(llm, conversaRepository, equipe, limiteRespostas);
        var portaoDaLoja = (await portoes.DaLojaAsync(empresaId, configuracao, conversa, ct)).FirstOrDefault(e => e.Barra);
        var motivoSemAgente = portaoDaLoja?.MotivoEscalada;
        if (motivoSemAgente is not null)
        {
            LogsDoAssistente.PortaoBarrou(logger, conversaId, portaoDaLoja!.Codigo);
            await escalador.EscalarAsync(empresaId, conversa, motivoSemAgente, agora, ct);
            await RegistrarTurnoDoPortaoAsync(DesfechoTurnoAgente.Escalou, portaoDaLoja.Codigo);
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
        // #2249: pessoa encaminhada à responsável. Fica com a equipe até alguém devolver a conversa ao robô.
        if (PortoesDoAssistente.DaPessoa(cliente).FirstOrDefault(e => e.Barra) is { MotivoEscalada: { } motivoPessoa } portaoDaPessoa)
        {
            LogsDoAssistente.PortaoBarrou(logger, conversaId, portaoDaPessoa.Codigo);
            await escalador.EscalarAsync(empresaId, conversa, motivoPessoa, agora, ct);
            await RegistrarTurnoDoPortaoAsync(DesfechoTurnoAgente.Escalou, portaoDaPessoa.Codigo);
            await unitOfWork.CommitAsync();
            return new ResultadoTurnoAgente(ChamouLlm: false, Respondeu: false, Escalou: true);
        }

        // #2285: anti-loop de custo. Outro robô do lado do cliente (ou um loop de mensagens) faria o agente
        // responder e gastar sem fim; no limite da hora, a conversa para nele e a escalada avisa a dona.
        if (await portoes.DoLimiteAsync(empresaId, conversaId, agora, ct) is { Barra: true, MotivoEscalada: { } motivoLimite })
        {
            LogsDoAssistente.PortaoBarrou(logger, conversaId, PortoesDoAssistente.Limite);
            await escalador.EscalarAsync(empresaId, conversa, motivoLimite, agora, ct);
            await RegistrarTurnoDoPortaoAsync(DesfechoTurnoAgente.Escalou, PortoesDoAssistente.Limite);
            await unitOfWork.CommitAsync();
            return new ResultadoTurnoAgente(ChamouLlm: false, Respondeu: false, Escalou: true);
        }

        // S69 (#2853): cota mensal esgotada. Não chama o modelo, passa para a equipe e deixa o turno no histórico como cota.
        if (await PortoesDoAssistente.DaCotaAsync(turnos, empresaId, configuracao, agora, ct) is { Barra: true, MotivoEscalada: { } motivoCota })
        {
            LogsDoAssistente.PortaoBarrou(logger, conversaId, PortoesDoAssistente.Cota);
            await escalador.EscalarAsync(empresaId, conversa, motivoCota, agora, ct);
            await RegistrarTurnoDoPortaoAsync(DesfechoTurnoAgente.Cota, PortoesDoAssistente.Cota);
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
            // #3291 (R3): prazo total só das chamadas ao modelo; ferramenta e envio terminam com o ct do serviço.
            prazoTotal: (prazoTurno?.Value ?? new PrazoDoTurnoAgenteOptions()).Prazo);
        var motivoEscalada = geracao.MotivoFalha;
        var concluiu = geracao.Concluiu;

        var final = await protecao.ExecutarAsync(Finalizar, ct, gravar: false);
        if (final is not null) return final;
        var desfechoSemResposta = geracao.Consumo?.FalhaLlm == true ? DesfechoTurnoAgente.Erro : DesfechoTurnoAgente.Interrompido;
        LogsDoAssistente.TurnoInterrompido(logger, conversaId, desfechoSemResposta.ToString());
        await RegistrarTurnoAsync(desfechoSemResposta, geracao.Consumo?.FalhaLlm == true ? MotivoDoTurno.FalhaLlm : MotivoDoTurno.Interrompido, ct);
        await unitOfWork.CommitAsync();
        return new ResultadoTurnoAgente(true, contexto.HouveEnvio, false);

        // S67 (#2534): um registro por turno que chamou o modelo, só com números e ids. #3291 (D2): é a única fonte do
        // consumo do turno; o agregado mensal UsoIa saiu do caminho (o gasto mensal vem de TurnoAgente e chamada_ia).
        // #3175: turno de portao que escala nao chama o modelo: 0 tokens e o codigo do portao como motivo.
        async Task RegistrarTurnoDoPortaoAsync(DesfechoTurnoAgente desfecho, string codigoDoPortao)
        {
            if (turnos is null) return;
            await turnos.AddAsync(TurnoAgente.Registrar(empresaId, conversaId, pendentes[^1].Id, null, null, 0, 0, 0, null, [], 0,
                desfecho, (int)Math.Min(cronometro.ElapsedMilliseconds, int.MaxValue), agora, MotivoDoTurno.DoPortao(codigoDoPortao)), ct);
        }

        async Task RegistrarTurnoAsync(DesfechoTurnoAgente desfecho, string? motivo, CancellationToken token)
        {
            if (turnos is null) return;
            var consumo = geracao.Consumo ?? new ConsumoGeracaoAgente();
            await turnos.AddAsync(TurnoAgente.Registrar(empresaId, conversaId, pendentes[^1].Id, gravacao.UltimaSaidaId,
                consumo.Modelo, consumo.TokensEntrada, consumo.TokensSaida, consumo.TokensCacheLido,
                precos?.Value.Estimar(consumo.Modelo, consumo.TokensEntrada, consumo.TokensSaida, consumo.TokensCacheLido),
                consumo.Ferramentas, consumo.Passos, desfecho, (int)Math.Min(cronometro.ElapsedMilliseconds, int.MaxValue), agora, motivo, turnoId), token);
        }

        async Task<ResultadoTurnoAgente> Finalizar(CancellationToken token)
        {
        ct = token;

        // #3175: o codigo do motivo, nunca o texto dele nem o que o modelo escreveu.
        var codigoDoMotivo = geracao.MotivoFalha is null ? null
            : MotivoDoTurno.DaFalhaDaGeracao(geracao.MotivoFalha, geracao.Consumo?.FalhaLlm == true);

        // As entradas que foram ao LLM ficam processadas: um job repetido não responde de novo e a
        // mensagem que chegar durante o turno continua pendente para o próximo (#1288).
        await gravacao.MarcarProcessadasAsync(ct);

        var respondeu = contexto.HouveEnvio;
        // #2249: o texto de cada ação é molde da loja; quem não é cliente recebe o de encaminhamento.
        var primeiroNome = NomesDeContato.PrimeiroNome(cliente?.Nome) ?? NomesDeContato.PrimeiroNome(conversa.ContatoNome);
        var encaminhamento = MoldeMensagemContato.Preencher(configuracao.MoldeEscalada(), primeiroNome, configuracao.NomeDona);
        // #3068: no turno retomado o cliente já pode ter recebido o encaminhamento da escalada anterior; não se repete.
        var jaAvisado = entradaRetomada is not null && dados.Mensagens.SkipWhile(m => m.Id != entradaRetomada.Id)
            .Any(m => m.Autor == AutorMensagem.Agente && m.Status != StatusMensagem.Falhou);
        if (conversa.Situacao == SituacaoConversa.Assumida && !jaAvisado)
        {
            var aoContato = contexto.Encaminhou
                ? MoldeMensagemContato.Preencher(configuracao.MensagemEncaminhamento, primeiroNome, configuracao.NomeDona)
                : encaminhamento;
            respondeu = await EnviarRespostaAsync(gravacao, conversa, aoContato, ct) || respondeu;
        }
        else if (conversa.Situacao == SituacaoConversa.Encerrada && contexto.RespostaVerificada is { Length: > 0 } despedida)
            respondeu = await EnviarRespostaAsync(gravacao, conversa, despedida, ct) || respondeu;
        else if (conversa.Situacao == SituacaoConversa.Automatica)
        {
            // #2515: sem texto do agente, o menu adiado basta como resposta. #2544: a foto do produto também.
            var soMenu = concluiu && motivoEscalada is null && string.IsNullOrWhiteSpace(geracao.Texto)
                && (contexto.TemEnvioAdiado || contexto.HouveEnvio);
            if (concluiu && motivoEscalada is null && contexto.RespostaVerificada is { Length: > 0 } verificada)
                respondeu = await EnviarRespostaAsync(gravacao, conversa, verificada, ct) || respondeu;
            else if (!soMenu && (!contexto.EnvioVerificado || motivoEscalada is not null || !concluiu))
            {
                // #1704: a conversa vai ao cliente; afirmação que nenhuma fonte do turno sustenta fica para a equipe.
                var barrada = !concluiu || motivoEscalada is not null ? null
                    : contexto.FerramentaFalhou ? LogsDoAssistente.CategoriaFerramentaFalhou
                    : GuardaRespostaAgente.Avaliar(geracao.Texto,
                        contexto.ResultadosDasFerramentas,
                        _ferramentas.Keys,
                        configuracao.NomeDona);
                if (concluiu && motivoEscalada is null && barrada is null)
                    respondeu = await EnviarRespostaAsync(gravacao, conversa, geracao.Texto!, ct) || respondeu;
                else
                {
                    respondeu = await EnviarRespostaAsync(gravacao, conversa, encaminhamento, ct) || respondeu;
                    if (barrada is not null)
                    {
                        var categoria = LogsDoAssistente.CategoriaDaGuarda(barrada);
                        LogsDoAssistente.GuardaBarrou(logger, conversaId, categoria);
                        codigoDoMotivo ??= MotivoDoTurno.DaGuarda(categoria);
                    }
                    else if (!concluiu) codigoDoMotivo ??= MotivoDoTurno.Iteracoes;
                    motivoEscalada ??= concluiu ? $"resposta do agente barrada ({barrada}); precisa de conferência humana"
                        : $"o agente atingiu o limite de {MaximoIteracoes} iterações sem concluir a resposta";
                }
            }
        }

        // #2515: o menu da ferramenta sai depois do texto; conversa escalada ou texto barrado não o recebe.
        // Menu que não sai deixa o cliente convidado a escolher sem opções: a equipe confere, e quem não recebeu nada
        // recebe o encaminhamento.
        if (motivoEscalada is null && conversa.Situacao == SituacaoConversa.Automatica && contexto.TemEnvioAdiado)
        {
            if (await contexto.EnviarAdiadoAsync(ct))
                respondeu = true;
            else
            {
                if (!respondeu)
                    respondeu = await EnviarRespostaAsync(gravacao, conversa, encaminhamento, ct);
                motivoEscalada = "o menu de janelas não saiu ao cliente; precisa de conferência humana";
                codigoDoMotivo = MotivoDoTurno.Menu;
            }
        }

        if (motivoEscalada is not null && conversa.Situacao == SituacaoConversa.Automatica)
            await escalador.EscalarAsync(empresaId, conversa, motivoEscalada, agora, ct);

        var escalou = motivoEscalada is not null || conversa.Situacao == SituacaoConversa.Assumida;
        // Escalou sem codigo de falha, guarda, iteracao ou menu: foi o modelo, pela ferramenta escalar_para_dona.
        await RegistrarTurnoAsync(geracao.Consumo?.FalhaLlm == true ? DesfechoTurnoAgente.Erro
            : escalou ? DesfechoTurnoAgente.Escalou : DesfechoTurnoAgente.Respondeu,
            codigoDoMotivo ?? (escalou ? MotivoDoTurno.EscaladaModelo : null), ct);

        await unitOfWork.CommitAsync();
        return new ResultadoTurnoAgente(true, respondeu, escalou);
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
        var portoes = new PortoesDoAssistente(llm, conversaRepository, equipe, limiteRespostas);
        var barra = PortoesDoAssistente.DaConversa(conversa, agora).Where(e => e.Codigo != PortoesDoAssistente.Situacao)
            .Concat(await portoes.DaLojaAsync(empresaId, configuracao, conversa, ct))
            .Concat(PortoesDoAssistente.DaPessoa(cliente))
            .Append(await portoes.DoLimiteAsync(empresaId, conversa.Id, agora, ct))
            .Append(await PortoesDoAssistente.DaCotaAsync(turnos, empresaId, configuracao, agora, ct))
            .FirstOrDefault(e => e.Barra);
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

    private async Task<bool> EnviarRespostaAsync(GravacaoSaidaTurno gravacao, Conversa conversa, string texto, CancellationToken ct)
    {
        var (empresaId, agora) = (gravacao.EmpresaId, gravacao.Agora);
        if (string.IsNullOrWhiteSpace(texto)) return false;
        texto = TextoWhatsApp.SemTravessao(texto);
        if (texto.Length > Mensagem.TextoTamanhoMaximo) texto = texto[..Mensagem.TextoTamanhoMaximo];

        Mensagem saida;
        var enviou = false;
        if (conversa.Canal == CanalConversa.ChatSite)
        {
            // #2876: o widget lê a saída gravada pelo id "chatsite:..."; nada sai por provedor externo.
            saida = Mensagem.Saida(empresaId, conversa.Id, AutorMensagem.Agente, agora, TipoConteudoMensagem.Texto, texto,
                CanalChatSite.PrefixoIdExterno + Guid.NewGuid().ToString("N"));
            enviou = true;
        }
        else
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
        gravacao.UltimaSaidaId = saida.Id;
        await gravacao.GravarAsync(ct);
        return enviou;
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

        /// <summary>S67: a última resposta que o turno gravou, para o registro do turno.</summary>
        public Guid? UltimaSaidaId { get; set; }

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
