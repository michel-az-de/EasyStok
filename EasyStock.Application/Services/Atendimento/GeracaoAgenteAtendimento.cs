using System.Text;
using EasyStock.Application.Ports.Output.Ai;
using EasyStock.Domain.Enums.Ia;
using EasyStock.Application.Ports.Output.Persistence.Atendimento;
using EasyStock.Application.Services.Atendimento.Ferramentas;
using EasyStock.Application.UseCases.Atendimento;
using EasyStock.Application.UseCases.Cliente.Dossie;
using EasyStock.Domain.Entities.Atendimento;
using EasyStock.Domain.Enums.Atendimento;

namespace EasyStock.Application.Services.Atendimento;

/// <summary>
/// Resultado do laço do LLM. <see cref="Concluiu"/> falso sem <see cref="MotivoFalha"/> = atingiu o
/// limite de iterações sem resposta final. <see cref="Consumo"/> (S67) traz o detalhe para o registro do turno.
/// </summary>
public sealed record GeracaoAgente(string? Texto, int Tokens, bool Concluiu, string? MotivoFalha, ConsumoGeracaoAgente? Consumo = null);

/// <summary>
/// S67 (#2534): o que o laço gastou e pediu, para o registro do turno. Cada resposta do modelo é um passo; o modelo é
/// o da última resposta. Ferramenta fica sem ok quando falhou, não existe ou não rodou porque a conversa mudou.
/// </summary>
public sealed class ConsumoGeracaoAgente
{
    private readonly List<FerramentaDoTurno> _ferramentas = [];

    public int TokensEntrada { get; private set; }
    public int TokensSaida { get; private set; }
    public int TokensCacheLido { get; private set; }
    public string? Modelo { get; private set; }
    public int Passos { get; private set; }

    /// <summary>A chamada ao modelo falhou (depois da nova tentativa).</summary>
    public bool FalhaLlm { get; private set; }

    public IReadOnlyList<FerramentaDoTurno> Ferramentas => _ferramentas;

    internal void Somar(RespostaLlm resposta)
    {
        // #2862: a escrita de cache entra na entrada, sem migração. A cota do mês soma entrada, saída e cache lido e
        // a escrita não pode sumir dela. O custo estimado fica um pouco abaixo: a escrita custa mais que a entrada comum.
        TokensEntrada += resposta.TokensEntrada + resposta.TokensCacheEscrito;
        TokensSaida += resposta.TokensSaida;
        TokensCacheLido += resposta.TokensCacheLido;
        Modelo = resposta.Modelo ?? Modelo;
        Passos++;
    }

    internal void RegistrarFerramenta(string nome, bool ok) => _ferramentas.Add(new FerramentaDoTurno(nome, ok));

    internal void RegistrarFalhaLlm() => FalhaLlm = true;
}

/// <summary>
/// Geração do agente de atendimento, sem efeito colateral próprio: monta o contexto (prompt fixo, caderno,
/// dossiê, histórico) e roda o laço LLM + ferramentas. Quem chama decide o que fazer com o texto: o turno
/// (<see cref="AgenteAtendimentoService"/>) envia a resposta verificada das ferramentas ou o texto que passar pela
/// <see cref="GuardaRespostaAgente"/>; a sugestão da dona (#1420) só devolve.
/// </summary>
/// <summary>System do turno em duas partes (#2862). <see cref="Texto"/> junta as duas, como o modelo recebia antes.</summary>
public sealed record SystemTurnoAgente(string Estavel, string Volatil)
{
    public string Texto => Estavel + Volatil;
}

public static class GeracaoAgenteAtendimento
{
    /// <summary>
    /// System prompt do turno em duas partes (#2862). <see cref="SystemTurnoAgente.Estavel"/>: prompt fixo e caderno da
    /// loja (S54), iguais entre turnos da empresa, para o cache do provedor reaproveitar o prefixo.
    /// <see cref="SystemTurnoAgente.Volatil"/>: data e hora, dossiê e histórico do cadastro (S25), que mudam a cada turno.
    /// </summary>
    public static async Task<SystemTurnoAgente> MontarSystemTurnoAsync(
        Guid empresaId,
        ConfiguracaoAtendimento configuracao,
        Conversa conversa,
        Cliente? cliente,
        IReadOnlyList<Mensagem> historico,
        DateTime agora,
        ICadernoRepository caderno,
        ObterDossieClienteUseCase dossieUseCase,
        CancellationToken ct)
    {
        var blocoCaderno = CadernoParaAgente.Montar(await caderno.ListarAsync(empresaId, incluirArquivados: false, ct));
        var estavel = PromptAtendimento.Montar(configuracao)
            + (blocoCaderno.Length > 0 ? "\n\n" + blocoCaderno : string.Empty);

        var volatil = "\n\n" + MontarDossie(conversa, cliente, historico, agora);
        if (cliente is not null
            && await dossieUseCase.ExecuteAsync(new ObterDossieClienteQuery(empresaId, cliente.Id), ct) is { } dossie)
            volatil += "\n\n" + ResumoDossieParaAgente.Montar(dossie);

        return new SystemTurnoAgente(estavel, volatil);
    }

    /// <summary>System do turno como texto único, para quem não usa cache: as duas partes, na ordem de sempre.</summary>
    public static async Task<string> MontarSystemAsync(
        Guid empresaId,
        ConfiguracaoAtendimento configuracao,
        Conversa conversa,
        Cliente? cliente,
        IReadOnlyList<Mensagem> historico,
        DateTime agora,
        ICadernoRepository caderno,
        ObterDossieClienteUseCase dossieUseCase,
        CancellationToken ct) =>
        (await MontarSystemTurnoAsync(empresaId, configuracao, conversa, cliente, historico, agora, caderno, dossieUseCase, ct)).Texto;

    /// <summary>
    /// Laço do LLM com as <paramref name="ferramentas"/>: executa as ferramentas pedidas e repete até a
    /// resposta final, no máximo <paramref name="maximoIteracoes"/> chamadas. Falha do LLM não sobe:
    /// volta em <see cref="GeracaoAgente.MotivoFalha"/>.
    /// </summary>
    public static async Task<GeracaoAgente> GerarAsync(
        IAgenteLlmClient llm,
        string system,
        List<MensagemLlm> mensagens,
        IReadOnlyDictionary<string, IFerramentaAgente> ferramentas,
        ContextoTurnoAgente contexto,
        int maximoIteracoes,
        ILogger logger,
        CancellationToken ct,
        string? modelo = null,
        string? systemVolatil = null)
    {
        // #2862: ordem fixa (ordinal, não de cultura) para o prefixo de ferramentas ser igual entre turnos.
        var definicoes = ferramentas.Values
            .OrderBy(f => f.Nome, StringComparer.Ordinal)
            .Select(f => new FerramentaLlm(f.Nome, f.Descricao, f.SchemaJson))
            .ToList();
        var tokens = 0;
        var consumo = new ConsumoGeracaoAgente();
        var situacaoInicial = contexto.Conversa.Situacao;

        for (var iteracao = 0; iteracao < maximoIteracoes; iteracao++)
        {
            RespostaLlm resposta;
            try
            {
                resposta = await EnviarComUmaNovaTentativaAsync(
                    llm, new RequisicaoLlm(system, [.. mensagens], definicoes, modelo, systemVolatil)
                    { EmpresaId = contexto.Conversa.EmpresaId, Finalidade = FinalidadeIa.Atendimento },
                    contexto.Conversa.Id, logger, ct);
            }
            catch (Exception ex) when (ex is not OperationCanceledException || !ct.IsCancellationRequested)
            {
                contexto.LimparRespostaVerificada();
                consumo.RegistrarFalhaLlm();
                logger.LogError(ex, "Agente de atendimento: falha chamando o LLM na conversa {ConversaId}.", contexto.Conversa.Id);
                return new GeracaoAgente(null, tokens, false, "o agente não conseguiu responder (falha ao chamar o LLM)", consumo);
            }

            // #2862: cache lido e escrito contam no total da cota (UsoIa); sem eles o número cai com o cache ligado.
            tokens += resposta.TokensEntrada + resposta.TokensSaida + resposta.TokensCacheLido + resposta.TokensCacheEscrito;
            consumo.Somar(resposta);

            if (resposta.StopReason != RespostaLlm.StopToolUse)
            {
                var texto = string.Join("\n", resposta.Conteudo.OfType<BlocoTextoLlm>().Select(b => b.Texto)).Trim();
                return new GeracaoAgente(texto, tokens, true,
                    texto.Length == 0 && !(situacaoInicial == SituacaoConversa.Automatica
                        && (contexto.HouveEnvio || contexto.TemEnvioAdiado || contexto.RespostaVerificada is { Length: > 0 }))
                        ? $"o agente não produziu resposta (stop_reason={resposta.StopReason})" : null, consumo);
            }

            mensagens.Add(new MensagemLlm(MensagemLlm.Assistente, resposta.Conteudo));
            var resultados = new List<BlocoLlm>();
            foreach (var uso in resposta.Conteudo.OfType<BlocoUsoFerramentaLlm>())
            {
                var resultado = await ExecutarFerramentaAsync(ferramentas, contexto, uso, logger, ct);
                resultados.Add(resultado);
                consumo.RegistrarFerramenta(uso.Nome, !resultado.EhErro && !contexto.Interrompido);
                if (contexto.Interrompido) return new GeracaoAgente(null, tokens, false, "o contexto da conversa mudou", consumo);
                if (situacaoInicial == SituacaoConversa.Automatica && contexto.Conversa.Situacao != SituacaoConversa.Automatica)
                {
                    if (contexto.Conversa.Situacao != SituacaoConversa.Encerrada) contexto.LimparRespostaVerificada();
                    return new GeracaoAgente(null, tokens, true, null, consumo);
                }
            }
            mensagens.Add(new MensagemLlm(MensagemLlm.Usuario, resultados));
        }

        return new GeracaoAgente(null, tokens, false, null, consumo);
    }

    /// <summary>
    /// #2285: um blip do LLM (reset de conexão, 5xx, timeout) não manda a conversa para a dona: 1 nova tentativa da mesma
    /// chamada. Só a chamada ao LLM se repete: a ferramenta que o turno já executou não roda de novo, porque o laço segue
    /// adiante a partir da resposta e não de volta. O desligamento (<paramref name="ct"/> cancelado) sobe sem tentar.
    /// </summary>
    private static async Task<RespostaLlm> EnviarComUmaNovaTentativaAsync(
        IAgenteLlmClient llm, RequisicaoLlm requisicao, Guid conversaId, ILogger logger, CancellationToken ct)
    {
        try
        {
            return await llm.EnviarAsync(requisicao, ct);
        }
        // #3064: o que nem saiu (disjuntor aberto, cota estourada, sem chave) não se repete: daria outra linha de cota_estourada.
        catch (Exception ex) when (ex is not ProvedorIaIndisponivelException
                                   && (ex is not OperationCanceledException || !ct.IsCancellationRequested))
        {
            logger.LogWarning(ex, "Agente de atendimento: a chamada ao LLM falhou na conversa {ConversaId}; tenta de novo uma vez.", conversaId);
            await Task.Delay(EsperaAntesDaNovaTentativa, ct);
            return await llm.EnviarAsync(requisicao, ct);
        }
    }

    private static readonly TimeSpan EsperaAntesDaNovaTentativa = TimeSpan.FromMilliseconds(300);

    private static async Task<BlocoResultadoFerramentaLlm> ExecutarFerramentaAsync(
        IReadOnlyDictionary<string, IFerramentaAgente> ferramentas, ContextoTurnoAgente contexto,
        BlocoUsoFerramentaLlm uso, ILogger logger, CancellationToken ct)
    {
        contexto.LimparRespostaVerificada();
        if (!ferramentas.TryGetValue(uso.Nome, out var ferramenta))
        {
            contexto.RegistrarFalhaDeFerramenta();
            return new BlocoResultadoFerramentaLlm(uso.Id, $"Ferramenta desconhecida: {uso.Nome}.", EhErro: true);
        }

        try
        {
            var resultado = await ferramenta.ExecutarAsync(contexto, uso.Entrada, ct);
            contexto.ResultadosDasFerramentas.Add(resultado);
            return new BlocoResultadoFerramentaLlm(uso.Id, resultado);
        }
        catch (Exception ex) when (ex is not OperationCanceledException || !ct.IsCancellationRequested)
        {
            contexto.LimparRespostaVerificada();
            contexto.RegistrarFalhaDeFerramenta();
            logger.LogError(ex, "Agente de atendimento: ferramenta {Ferramenta} falhou na conversa {ConversaId}.",
                ferramenta.Nome, contexto.Conversa.Id);
            return new BlocoResultadoFerramentaLlm(uso.Id, "A ferramenta falhou; não invente o resultado.", EhErro: true);
        }
    }

    /// <summary>
    /// Cliente → <c>user</c>; agente e dona → <c>assistant</c>. Mensagens do sistema não entram na
    /// conversa (vão para o dossiê), nem as saídas que falharam de vez ao enviar (#2004: o cliente nunca as recebeu, e o
    /// agente responderia "já te mandei acima"). A falha incerta ou com reenvio agendado entra marcada
    /// <see cref="MarcadorNaoConfirmada"/>. Mensagens seguidas do mesmo papel viram uma só. As
    /// <paramref name="pendentes"/> vão para o fim: o agente ainda não as viu (#1288).
    /// </summary>
    public static List<MensagemLlm> MontarConversacao(IReadOnlyList<Mensagem> historico, IReadOnlyList<Mensagem> pendentes)
    {
        var resultado = new List<MensagemLlm>();
        var ordenado = historico.Where(m => !pendentes.Contains(m)).Concat(pendentes);
        foreach (var msg in ordenado.Where(m => m.Autor != AutorMensagem.Sistema && !FalhouDeVez(m)))
        {
            var papel = msg.Autor == AutorMensagem.Cliente ? MensagemLlm.Usuario : MensagemLlm.Assistente;
            var texto = msg.Autor switch
            {
                AutorMensagem.Cliente => TextoExternoPrompt.Neutralizar(TextoDoCliente(msg)),
                AutorMensagem.Dona => $"(dona) {msg.Texto}",
                AutorMensagem.Atendente => $"(equipe) {msg.Texto}",
                _ => msg.Texto ?? string.Empty
            };
            if (string.IsNullOrWhiteSpace(texto)) continue;
            if (msg.Status == StatusMensagem.Falhou) texto = $"{MarcadorNaoConfirmada} {texto}";

            var origem = pendentes.Contains(msg) ? "entrada nova" : "histórico";
            texto = $"[{origem} de {HorarioBrasil.ConverterParaBrasilia(msg.EnviadaEm):dd/MM/yyyy HH:mm}]\n{texto}";

            AcrescentarTexto(resultado, papel, texto);
        }

        // A API exige que a conversa comece pelo usuário.
        while (resultado.Count > 0 && resultado[0].Papel != MensagemLlm.Usuario)
            resultado.RemoveAt(0);

        return resultado;
    }

    /// <summary>#2004: saída que pode ter chegado ao cliente (falha incerta) ou que o reenvio automático ainda vai entregar.</summary>
    public const string MarcadorNaoConfirmada = "[não confirmada]";

    /// <summary>
    /// #2004: falha definitiva: o cliente nunca recebeu a saída e nada vai entregá-la. Falha incerta (pode ter chegado) e
    /// temporária com reenvio agendado seguem no histórico, marcadas com <see cref="MarcadorNaoConfirmada"/>.
    /// </summary>
    private static bool FalhouDeVez(Mensagem m) =>
        m.Status == StatusMensagem.Falhou
        && m.UltimaFalhaEnvio != TipoFalhaEnvio.Incerta
        && !(m.UltimaFalhaEnvio == TipoFalhaEnvio.Temporaria && m.ProximoReenvioEm is not null);

    /// <summary>Texto no fim da conversa: junta à última mensagem se o papel for o mesmo.</summary>
    public static void AcrescentarTexto(List<MensagemLlm> mensagens, string papel, string texto)
    {
        if (mensagens.Count > 0 && mensagens[^1].Papel == papel)
            mensagens[^1] = mensagens[^1] with { Conteudo = [.. mensagens[^1].Conteudo, new BlocoTextoLlm(texto)] };
        else
            mensagens.Add(new MensagemLlm(papel, [new BlocoTextoLlm(texto)]));
    }

    private static string TextoDoCliente(Mensagem msg)
    {
        var marcador = msg.TipoConteudo switch
        {
            TipoConteudoMensagem.Imagem => "[imagem recebida]",
            TipoConteudoMensagem.Audio => "[áudio recebido]",
            TipoConteudoMensagem.Documento => "[documento recebido]",
            TipoConteudoMensagem.Localizacao => "[localização recebida]",
            TipoConteudoMensagem.Botao => $"[botão tocado: {msg.BotaoId}]",
            TipoConteudoMensagem.Outro => "[mensagem não suportada]",
            _ => null
        };

        if (msg.TipoConteudo == TipoConteudoMensagem.Audio && !string.IsNullOrWhiteSpace(msg.Transcricao))
            return $"[áudio] {msg.Transcricao}";
        if (marcador is null) return msg.Texto ?? string.Empty;
        return string.IsNullOrWhiteSpace(msg.Texto) ? marcador : $"{marcador} {msg.Texto}";
    }

    /// <summary>
    /// Dossiê do turno, depois do prompt fixo. Mensagem do sistema sem <c>wamid</c> nunca saiu para o
    /// cliente: é nota interna e entra marcada <see cref="PromptAtendimento.MarcadorInterno"/> (RN-08).
    /// </summary>
    private static string MontarDossie(Conversa conversa, Cliente? cliente, IReadOnlyList<Mensagem> historico, DateTime agora)
    {
        var interno = PromptAtendimento.MarcadorInterno;
        var sb = new StringBuilder();
        sb.AppendLine("Dossiê desta conversa (contexto para você, não para citar):");
        sb.AppendLine($"- Agora: {HorarioBrasil.ConverterParaBrasilia(agora):dd/MM/yyyy HH:mm} (horário de Brasília).");

        // #2280: o nome do perfil é escrito pelo cliente; numa linha só, não abre item novo no dossiê.
        var nome = TextoExternoPrompt.Linha(
            cliente?.Nome is { } n && n != IdentificarClientePorTelefoneUseCase.NomePadraoLead ? n : conversa.ContatoNome);
        sb.AppendLine($"- Nome: {(nome.Length == 0 ? "não informado" : nome)}.");
        sb.AppendLine(cliente is null || cliente.OrderCount == 0
            ? "- Situação: lead (ainda não comprou)."
            : $"- Situação: cliente com {cliente.OrderCount} pedido(s).");
        sb.AppendLine(conversa.PedidoEmAndamentoId is { } pedidoId
            ? $"- Pedido vinculado à conversa: {pedidoId}. Consulte consultar_pedido para saber a situação atual; o vínculo não comprova pedido em andamento."
            : "- Pedido em andamento: nenhum registrado nesta conversa.");
        if (ContextoConversaJson.ParaDossie(conversa) is { Length: > 0 } contextoSalvo)
            sb.AppendLine($"- Contexto salvo: {TextoExternoPrompt.Linha(contextoSalvo)}");
        if (!string.IsNullOrWhiteSpace(cliente?.Observacoes))
            sb.AppendLine($"- {interno} Observações do cadastro: {cliente.Observacoes}");

        var sistema = historico.Where(m => m.Autor == AutorMensagem.Sistema && !string.IsNullOrWhiteSpace(m.Texto)).ToList();
        var notas = sistema.Where(m => m.ExternoId is null).ToList();
        if (notas.Count > 0)
        {
            sb.AppendLine("Notas internas:");
            foreach (var nota in notas) sb.AppendLine($"- {interno} {nota.Texto}");
        }

        var automaticas = sistema.Where(m => m.ExternoId is not null).ToList();
        if (automaticas.Count > 0)
        {
            sb.AppendLine("Mensagens automáticas já enviadas ao cliente:");
            foreach (var auto in automaticas) sb.AppendLine($"- {HorarioBrasil.ConverterParaBrasilia(auto.EnviadaEm):dd/MM/yyyy HH:mm}: {auto.Texto}");
        }

        return sb.ToString().TrimEnd();
    }
}
