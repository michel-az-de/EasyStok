using System.Diagnostics;
using EasyStock.Application.Ports.Output.Atendimento;
using EasyStock.Application.Ports.Output.Persistence.Atendimento;
using EasyStock.Application.Services.Atendimento.Ferramentas;
using EasyStock.Domain.Entities.Atendimento;
using EasyStock.Domain.Enums.Atendimento;
using Microsoft.Extensions.Options;

namespace EasyStock.Application.Services.Atendimento;

/// <summary>
/// #3298 (R11): o fim do turno do agente, fora da orquestração do <see cref="AgenteAtendimentoService"/>: o que o cliente
/// recebe (resposta do modelo, encaminhamento ou despedida), a guarda da saída, o envio, a escalada e o registro do turno
/// em <c>TurnoAgente</c>. O serviço o monta a cada turno com as próprias dependências, como faz com
/// <see cref="PortoesDoAssistente"/>.
/// </summary>
internal sealed class FinalizacaoDoTurno(
    IConversaRepository conversaRepository, IEscaladorConversa escalador, IWhatsAppCloudClient cloudClient,
    IUnitOfWork unitOfWork, ITurnoAgenteRepository turnos, IOptions<PrecosModelosAgenteOptions> precos, ILogger logger)
{
    /// <summary>O turno que acabou: o que a orquestração já decidiu e o que o modelo devolveu.</summary>
    public sealed record Turno(
        Guid EmpresaId, Guid ConversaId, DateTime Agora, Conversa Conversa, Cliente? Cliente, ConfiguracaoAtendimento Configuracao,
        IReadOnlyList<Mensagem> Historico, Mensagem? EntradaRetomada, Guid UltimaEntradaId, Guid TurnoId, Stopwatch Cronometro,
        GeracaoAgente Geracao, ContextoTurnoAgente Contexto, GravacaoSaidaTurno Gravacao, IEnumerable<string> Ferramentas);

    /// <summary>
    /// Decide o que o cliente recebe, envia, escala se for o caso, registra o turno e comita. Roda sob a proteção do vínculo,
    /// com o <paramref name="ct"/> da transação dela.
    /// </summary>
    public async Task<ResultadoTurnoAgente> FinalizarAsync(Turno turno, CancellationToken ct)
    {
        var (empresaId, conversaId, agora) = (turno.EmpresaId, turno.ConversaId, turno.Agora);
        var (conversa, configuracao, gravacao) = (turno.Conversa, turno.Configuracao, turno.Gravacao);
        var (geracao, contexto) = (turno.Geracao, turno.Contexto);
        var motivoEscalada = geracao.MotivoFalha;
        var concluiu = geracao.Concluiu;

        // #3175: o codigo do motivo, nunca o texto dele nem o que o modelo escreveu.
        var codigoDoMotivo = geracao.MotivoFalha is null ? null
            : MotivoDoTurno.DaFalhaDaGeracao(geracao.MotivoFalha, geracao.Consumo?.FalhaLlm == true);

        // As entradas que foram ao LLM ficam processadas: um job repetido não responde de novo e a
        // mensagem que chegar durante o turno continua pendente para o próximo (#1288).
        await gravacao.MarcarProcessadasAsync(ct);

        var respondeu = contexto.HouveEnvio;
        // #2249: o texto de cada ação é molde da loja; quem não é cliente recebe o de encaminhamento.
        var primeiroNome = NomesDeContato.PrimeiroNome(turno.Cliente?.Nome) ?? NomesDeContato.PrimeiroNome(conversa.ContatoNome);
        var encaminhamento = MoldeMensagemContato.Preencher(configuracao.MoldeEscalada(), primeiroNome, configuracao.NomeDona);
        // #3068: no turno retomado o cliente já pode ter recebido o encaminhamento da escalada anterior; não se repete.
        var entradaRetomada = turno.EntradaRetomada;
        var jaAvisado = entradaRetomada is not null && turno.Historico.SkipWhile(m => m.Id != entradaRetomada.Id)
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
                        turno.Ferramentas,
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
                        : $"o agente atingiu o limite de {AgenteAtendimentoService.MaximoIteracoes} iterações sem concluir a resposta";
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
        await RegistrarAsync(turno, geracao.Consumo?.FalhaLlm == true ? DesfechoTurnoAgente.Erro
            : escalou ? DesfechoTurnoAgente.Escalou : DesfechoTurnoAgente.Respondeu,
            codigoDoMotivo ?? (escalou ? MotivoDoTurno.EscaladaModelo : null), ct);

        await unitOfWork.CommitAsync();
        return new ResultadoTurnoAgente(true, respondeu, escalou);
    }

    /// <summary>
    /// A proteção do vínculo descartou o fim do turno (a conversa mudou de mãos durante a chamada ao modelo): nada além do
    /// que já saiu vai ao cliente, e o turno fica no histórico como erro do modelo ou interrompido.
    /// </summary>
    public async Task<ResultadoTurnoAgente> InterrompidoAsync(Turno turno, CancellationToken ct)
    {
        var falhouOModelo = turno.Geracao.Consumo?.FalhaLlm == true;
        var desfecho = falhouOModelo ? DesfechoTurnoAgente.Erro : DesfechoTurnoAgente.Interrompido;
        LogsDoAssistente.TurnoInterrompido(logger, turno.ConversaId, desfecho.ToString());
        await RegistrarAsync(turno, desfecho, falhouOModelo ? MotivoDoTurno.FalhaLlm : MotivoDoTurno.Interrompido, ct);
        await unitOfWork.CommitAsync();
        return new ResultadoTurnoAgente(true, turno.Contexto.HouveEnvio, false);
    }

    // S67 (#2534): um registro por turno que chamou o modelo, só com números e ids. #3291 (D2): é a única fonte do
    // consumo do turno; o agregado mensal UsoIa saiu do caminho (o gasto mensal vem de TurnoAgente e chamada_ia).
    private async Task RegistrarAsync(Turno turno, DesfechoTurnoAgente desfecho, string? motivo, CancellationToken ct)
    {
        var consumo = turno.Geracao.Consumo ?? new ConsumoGeracaoAgente();
        await turnos.AddAsync(TurnoAgente.Registrar(turno.EmpresaId, turno.ConversaId, turno.UltimaEntradaId, turno.Gravacao.UltimaSaidaId,
            consumo.Modelo, consumo.TokensEntrada, consumo.TokensSaida, consumo.TokensCacheLido,
            precos.Value.Estimar(consumo.Modelo, consumo.TokensEntrada, consumo.TokensSaida, consumo.TokensCacheLido),
            consumo.Ferramentas, consumo.Passos, desfecho, (int)Math.Min(turno.Cronometro.ElapsedMilliseconds, int.MaxValue), turno.Agora,
            motivo, turno.TurnoId), ct);
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
}
