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

    /// <summary>
    /// #3347 (D4): a falha foi definitiva: o provedor respondeu com um status que a nova tentativa não repete
    /// (<c>FalhaTransitoria</c> devolve falso: 400, 401, 402, 403, 404, 3xx e afins). Só vale junto de <see cref="FalhaLlm"/>,
    /// que continua verdadeiro nas duas classes: o desfecho Erro depende dele.
    /// </summary>
    public bool FalhaLlmDefinitiva { get; private set; }

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

    internal void RegistrarFalhaLlm(bool definitiva)
    {
        FalhaLlm = true;
        FalhaLlmDefinitiva = definitiva;
    }
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
    /// resposta final, no máximo <paramref name="maximoIteracoes"/> voltas (cada uma com até 3 chamadas ao modelo). Falha do LLM não sobe:
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
        string? systemVolatil = null,
        Guid? turnoAgenteId = null,
        TimeSpan? prazoTotal = null)
    {
        // #2862: ordem fixa (ordinal, não de cultura) para o prefixo de ferramentas ser igual entre turnos.
        var definicoes = ferramentas.Values
            .OrderBy(f => f.Nome, StringComparer.Ordinal)
            .Select(f => new FerramentaLlm(f.Nome, f.Descricao, f.SchemaJson))
            .ToList();
        var tokens = 0;
        var consumo = new ConsumoGeracaoAgente();
        var situacaoInicial = contexto.Conversa.Situacao;

        // #3291 (R3): o prazo corre desde a primeira volta (ferramentas gastam dele) e corta só a espera pelo modelo.
        // Ferramenta e envio seguem com o ct de quem chamou:
        // turno que já mexeu no mundo não é cortado no meio. O desligamento do serviço (ct cancelado) segue subindo
        // como cancelamento e nunca vira prazo.
        using var prazo = CancellationTokenSource.CreateLinkedTokenSource(ct);
        if (prazoTotal is { } total && total > TimeSpan.Zero) prazo.CancelAfter(total);
        bool PrazoEstourou() => prazo.IsCancellationRequested && !ct.IsCancellationRequested;
        GeracaoAgente SemPrazo()
        {
            contexto.LimparRespostaVerificada();
            logger.LogWarning("Agente de atendimento: a conversa {ConversaId} passou do prazo do turno ({Prazo}); vai para a equipe.",
                contexto.Conversa.Id, prazoTotal);
            return new GeracaoAgente(null, tokens, false, MotivoDoPrazo(prazoTotal), consumo);
        }

        for (var iteracao = 0; iteracao < maximoIteracoes; iteracao++)
        {
            // A ferramenta da volta anterior pode ter levado o turno além do prazo: sem outra chamada ao modelo.
            if (PrazoEstourou()) return SemPrazo();

            RespostaLlm resposta;
            var repetiu = false;
            try
            {
                var requisicao = new RequisicaoLlm(system, [.. mensagens], definicoes, modelo, systemVolatil)
                { EmpresaId = contexto.Conversa.EmpresaId, Finalidade = FinalidadeIa.Atendimento, TurnoAgenteId = turnoAgenteId };
                resposta = await EnviarComUmaNovaTentativaAsync(llm, requisicao, contexto.Conversa.Id, logger, prazo.Token);
                // Só sobe o teto quando há para onde subir: a saída da chamada cortada é o teto que ela usou (revisão da #3274), e
                // com saida_max cadastrado o cliente já pediu o máximo do modelo (TetoNoMaximo): a repetição sairia igual (#3313).
                if (resposta.StopReason == RespostaLlm.StopMaxTokens && !resposta.TetoNoMaximo && resposta.TokensSaida < MaxTokensNovaTentativa)
                {
                    // #3273 (R1): o teto acabou antes do modelo terminar (com modelo que raciocina, o pensamento come o teto).
                    // A chamada cortada foi paga: entra na conta. Uma nova tentativa pedindo teto maior; texto cortado nunca segue.
                    logger.LogWarning("Agente de atendimento: o modelo estourou o teto de saída na conversa {ConversaId}; tenta de novo pedindo {Teto} tokens.",
                        contexto.Conversa.Id, MaxTokensNovaTentativa);
                    repetiu = true;
                    tokens += TotalDeTokens(resposta);
                    consumo.Somar(resposta);
                    // Uma chamada só: com teto 4 vezes maior o prazo de 60 s é o risco, e repetir o pedido sem teto maior o estouraria de novo.
                    resposta = await llm.EnviarAsync(requisicao with { MaxTokensMinimo = MaxTokensNovaTentativa }, prazo.Token);
                }
            }
            // O prazo é do turno, não do provedor: não é falha do modelo e não se repete.
            catch (Exception) when (PrazoEstourou())
            {
                return SemPrazo();
            }
            catch (Exception ex) when (ex is not OperationCanceledException || !ct.IsCancellationRequested)
            {
                contexto.LimparRespostaVerificada();
                // #3347 (D4): a regra da nova tentativa decide a classe gravada: status que ela não repete é falha definitiva.
                consumo.RegistrarFalhaLlm(definitiva: !FalhaTransitoria(ex));
                logger.LogError(ex, "Agente de atendimento: falha chamando o LLM na conversa {ConversaId}.", contexto.Conversa.Id);
                return new GeracaoAgente(null, tokens, false, "o agente não conseguiu responder (falha ao chamar o LLM)", consumo);
            }

            tokens += TotalDeTokens(resposta);
            consumo.Somar(resposta);

            if (resposta.StopReason == RespostaLlm.StopRefusal)
            {
                // #3321 (D4): o modelo recusou. O texto, se veio, é a recusa e não a resposta: nunca vai ao cliente, e repetir o
                // mesmo pedido tende a repetir a recusa. A conversa vai para a equipe, com motivo próprio.
                contexto.LimparRespostaVerificada();
                logger.LogWarning("Agente de atendimento: o modelo recusou responder na conversa {ConversaId}; vai para a equipe.",
                    contexto.Conversa.Id);
                return new GeracaoAgente(null, tokens, false, MotivoRecusa + " (refusal)", consumo);
            }

            if (resposta.StopReason == RespostaLlm.StopMaxTokens)
            {
                // Estourou de novo, ou não houve nova tentativa (o teto já era o máximo do modelo, ou a saída cortada já tinha
                // 16384 tokens ou mais): o texto, se veio, está cortado e não vai ao cliente. A conversa vai para a equipe.
                contexto.LimparRespostaVerificada();
                logger.LogWarning("Agente de atendimento: o teto de saída estourou na conversa {ConversaId}, que vai para a equipe (nova tentativa feita: {NovaTentativa}).",
                    contexto.Conversa.Id, repetiu);
                return new GeracaoAgente(null, tokens, false, MotivoTetoDeSaida + " (max_tokens)", consumo);
            }

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
                // Ferramenta que lançou pode ter agido (comanda que a Meta entregou, pedido gravado): devolver ao modelo o faria
                // repetir o efeito. O turno acaba aqui e a finalização escala como falha de ferramenta.
                if (contexto.FerramentaLancou) return new GeracaoAgente(null, tokens, true, null, consumo);
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

    /// <summary>#3273 (R1): teto da chamada que repete depois de <c>max_tokens</c>; sobe só o piso, nunca baixa o configurado.</summary>
    public const int MaxTokensNovaTentativa = 16384;

    /// <summary>
    /// #3273 (R1): começo do motivo quando a resposta é cortada por <c>max_tokens</c> sem nova tentativa (o teto já era o máximo do
    /// modelo, #3313, ou a saída cortada já tinha 16384 tokens ou mais) ou quando a nova tentativa estoura também.
    /// </summary>
    public const string MotivoTetoDeSaida = "o agente atingiu o teto de saída do modelo";

    /// <summary>#3291 (R3): começo do motivo quando o turno passa do prazo, contado desde a primeira volta.</summary>
    public const string MotivoPrazoDoTurno = "o agente passou do prazo do turno";

    /// <summary>#3321 (D4): começo do motivo quando o modelo recusa responder (<c>stop_reason</c> <c>refusal</c>).</summary>
    public const string MotivoRecusa = "o modelo recusou responder";

    private static string MotivoDoPrazo(TimeSpan? prazo) =>
        prazo is { } p ? $"{MotivoPrazoDoTurno} ({(int)p.TotalSeconds} s)" : MotivoPrazoDoTurno;

    // #2862: cache lido e escrito contam no total da cota (lida do TurnoAgente); sem eles o número cai com o cache ligado.
    private static int TotalDeTokens(RespostaLlm r) => r.TokensEntrada + r.TokensSaida + r.TokensCacheLido + r.TokensCacheEscrito;

    /// <summary>
    /// #3273 (R2): só o que pode mudar em 300 ms se repete (rede, prazo, 5xx, 408, 429). 400, 401, 402 e afins são definitivos.
    /// #3347 (D4): a mesma regra escolhe o motivo gravado no turno (<c>falha_llm</c> ou <c>falha_llm_definitiva</c>): mudá-la muda os dois.
    /// </summary>
    internal static bool FalhaTransitoria(Exception ex) =>
        ex is not HttpRequestException { StatusCode: { } status } || (int)status >= 500
            || status is System.Net.HttpStatusCode.RequestTimeout or System.Net.HttpStatusCode.TooManyRequests;

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
        catch (Exception ex) when (ex is not ProvedorIaIndisponivelException && FalhaTransitoria(ex)
                                   && (ex is not OperationCanceledException || !ct.IsCancellationRequested))
        {
            logger.LogWarning(ex, "Agente de atendimento: a chamada ao LLM falhou na conversa {ConversaId}; tenta de novo uma vez.", conversaId);
            await Task.Delay(EsperaAntesDaNovaTentativa, ct);
            return await llm.EnviarAsync(requisicao, ct);
        }
    }

    private static readonly TimeSpan EsperaAntesDaNovaTentativa = TimeSpan.FromMilliseconds(300);

    // #3340 (R2I-02): recusa que conta como falha do turno são só as ações que comprometem algo com o cliente: o link que
    // não saiu ("linkGerado":false) e o pedido que não foi criado ("itens_invalidos", "pedido_recusado"). Os demais "erro"
    // (consulta sem resultado, dado que falta) são conversa normal: o modelo pergunta ao cliente e não escala.
    private static bool FerramentaRecusou(string resultado)
    {
        try
        {
            using var doc = System.Text.Json.JsonDocument.Parse(resultado);
            var raiz = doc.RootElement;
            if (raiz.ValueKind != System.Text.Json.JsonValueKind.Object) return false;
            return (raiz.TryGetProperty("erro", out var erro) && erro.ValueKind == System.Text.Json.JsonValueKind.String
                    && erro.GetString() is "itens_invalidos" or "pedido_recusado")
                || (raiz.TryGetProperty("linkGerado", out var link) && link.ValueKind == System.Text.Json.JsonValueKind.False
                    && !RecusaQueSoOrienta(raiz));
        }
        catch (System.Text.Json.JsonException)
        {
            return false;
        }
    }

    // Recusa do link que só diz ao modelo qual é o próximo passo (resumo antes do link, esperar o ok, pedido já pago):
    // ele resolve na própria volta, então não é falha do turno e não escala o pedido comum.
    private static readonly HashSet<string> MotivosQueSoOrientam = ["resumo_nao_enviado", "aguardando_ok", "pedido_ja_pago"];

    private static bool RecusaQueSoOrienta(System.Text.Json.JsonElement raiz) =>
        raiz.TryGetProperty("motivo", out var motivo) && motivo.ValueKind == System.Text.Json.JsonValueKind.String
        && motivo.GetString() is { } texto && MotivosQueSoOrientam.Contains(texto);

    // Qualquer "erro" devolvido (consulta sem resultado, dado que falta) invalida a resposta verificada de uma ferramenta
    // anterior, como antes da #3340; só não escala.
    private static bool ResultadoTemErro(string resultado)
    {
        try
        {
            using var doc = System.Text.Json.JsonDocument.Parse(resultado);
            return doc.RootElement.ValueKind == System.Text.Json.JsonValueKind.Object && doc.RootElement.TryGetProperty("erro", out _);
        }
        catch (System.Text.Json.JsonException)
        {
            return false;
        }
    }

    private static async Task<BlocoResultadoFerramentaLlm> ExecutarFerramentaAsync(
        IReadOnlyDictionary<string, IFerramentaAgente> ferramentas, ContextoTurnoAgente contexto,
        BlocoUsoFerramentaLlm uso, ILogger logger, CancellationToken ct)
    {
        contexto.DescartarRespostaDeConsulta();
        if (!ferramentas.TryGetValue(uso.Nome, out var ferramenta))
        {
            contexto.LimparRespostaVerificada();
            contexto.RegistrarFalhaDeFerramenta();
            return new BlocoResultadoFerramentaLlm(uso.Id, $"Ferramenta desconhecida: {uso.Nome}.", EhErro: true);
        }

        try
        {
            var resultado = await ferramenta.ExecutarAsync(contexto, uso.Entrada, ct);
            // #3340 (R2I-02): recusa devolvida pela ferramenta é falha: o "já enviei" do modelo não sai ao cliente.
            if (FerramentaRecusou(resultado))
            {
                contexto.LimparRespostaVerificada();
                contexto.RegistrarFalhaDeFerramenta();
                return new BlocoResultadoFerramentaLlm(uso.Id, resultado, EhErro: true);
            }
            if (ResultadoTemErro(resultado)) contexto.DescartarRespostaDeConsulta();
            contexto.ResultadosDasFerramentas.Add(resultado);
            return new BlocoResultadoFerramentaLlm(uso.Id, resultado);
        }
        catch (Exception ex) when (ex is not OperationCanceledException || !ct.IsCancellationRequested)
        {
            contexto.LimparRespostaVerificada();
            contexto.RegistrarFalhaDeFerramenta(lancou: true);
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
        // #2833: a apagada para todos sai junto: o cliente não a vê mais, e o agente não pode se apoiar nela.
        foreach (var msg in ordenado.Where(m => m.Autor != AutorMensagem.Sistema && !FalhouDeVez(m) && m.ApagadaEm is null))
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
