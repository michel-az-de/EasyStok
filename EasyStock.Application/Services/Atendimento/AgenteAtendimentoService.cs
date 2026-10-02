using System.Text;
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
/// de 24 h, ou sem entrada pendente do cliente (job repetido) → não chama o LLM nem envia nada. LLM
/// indisponível (<c>Anthropic:Enabled=false</c> ou sem chave) ou canal sem agente → escala para a dona.
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

        // Ninguém responderia: LLM desligado (Anthropic:Enabled=false ou sem chave) ou canal sem agente (o
        // agente envia pelo cliente do WhatsApp; em outro canal sairia pelo canal errado). Passa de fato
        // para a dona, senão a conversa fica Automatica sem ninguém e fora do lembrete (#1288).
        var motivoSemAgente = !llm.Disponivel ? MotivoAgenteDesligado
            : !conversa.TemAgente ? $"o canal {conversa.Canal} não tem agente"
            : null;
        if (motivoSemAgente is not null)
        {
            logger.LogInformation("Agente: conversa {ConversaId} fica para a dona ({Motivo}).", conversaId, motivoSemAgente);
            await escalador.EscalarAsync(empresaId, conversa, motivoSemAgente, agora, ct);
            await unitOfWork.CommitAsync();
            return new ResultadoTurnoAgente(ChamouLlm: false, Respondeu: false, Escalou: true);
        }

        var mensagens = MontarConversacao(dados.Mensagens, pendentes);
        if (mensagens.Count == 0 || mensagens[^1].Papel != MensagemLlm.Usuario)
            return ResultadoTurnoAgente.Ignorado;

        var configuracao = await configuracaoRepository.GetByEmpresaIdAsync(empresaId)
            ?? ConfiguracaoAtendimento.CriarPadrao(empresaId);
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

        // S54: caderno da loja (núcleo + índice) logo depois do prompt base e antes do dossiê, para o começo do
        // prompt ser igual entre conversas da mesma empresa e aproveitar o cache do provedor.
        var blocoCaderno = CadernoParaAgente.Montar(await caderno.ListarAsync(empresaId, incluirArquivados: false, ct));
        var system = PromptAtendimento.Montar(configuracao)
            + (blocoCaderno.Length > 0 ? "\n\n" + blocoCaderno : string.Empty)
            + "\n\n" + MontarDossie(conversa, cliente, dados.Mensagens, agora);

        // S25: histórico do cadastro (tags, pedidos, favorito, notas [interno]) quando há cliente vinculado.
        if (cliente is not null
            && await dossieUseCase.ExecuteAsync(new ObterDossieClienteQuery(empresaId, cliente.Id), ct) is { } dossie)
            system += "\n\n" + ResumoDossieParaAgente.Montar(dossie);
        var definicoes = _ferramentas.Values
            .Select(f => new FerramentaLlm(f.Nome, f.Descricao, f.SchemaJson))
            .ToList();
        var contexto = new ContextoTurnoAgente(empresaId, conversa, agora);

        var tokens = 0;
        string? textoFinal = null;
        string? motivoEscalada = null;
        var concluiu = false;

        for (var iteracao = 0; iteracao < MaximoIteracoes; iteracao++)
        {
            RespostaLlm resposta;
            try
            {
                resposta = await llm.EnviarAsync(new RequisicaoLlm(system, [.. mensagens], definicoes), ct);
            }
            catch (Exception ex) when (ex is not OperationCanceledException || !ct.IsCancellationRequested)
            {
                logger.LogError(ex, "Agente de atendimento: falha chamando o LLM na conversa {ConversaId}.", conversaId);
                motivoEscalada = "o agente não conseguiu responder (falha ao chamar o LLM)";
                break;
            }

            tokens += resposta.TokensEntrada + resposta.TokensSaida;

            if (resposta.StopReason != RespostaLlm.StopToolUse)
            {
                concluiu = true;
                textoFinal = string.Join("\n", resposta.Conteudo.OfType<BlocoTextoLlm>().Select(b => b.Texto)).Trim();
                if (textoFinal.Length == 0)
                    motivoEscalada = $"o agente não produziu resposta (stop_reason={resposta.StopReason})";
                break;
            }

            mensagens.Add(new MensagemLlm(MensagemLlm.Assistente, resposta.Conteudo));
            var resultados = new List<BlocoLlm>();
            foreach (var uso in resposta.Conteudo.OfType<BlocoUsoFerramentaLlm>())
                resultados.Add(await ExecutarFerramentaAsync(contexto, uso, ct));
            mensagens.Add(new MensagemLlm(MensagemLlm.Usuario, resultados));
        }

        // As entradas que foram ao LLM ficam processadas: um job repetido não responde de novo e a
        // mensagem que chegar durante o turno continua pendente para o próximo (#1288).
        await MarcarProcessadasAsync(empresaId, dados.Mensagens, agora, ct);

        // A dona pode ter assumido (ou encerrado) enquanto o LLM respondia: a Situacao carregada no
        // início está velha. Fora do automático o agente não envia nem escala (#1288).
        if (await conversaRepository.ObterSituacaoAsync(empresaId, conversaId, ct) != SituacaoConversa.Automatica)
        {
            logger.LogInformation("Agente: conversa {ConversaId} saiu do automático durante o turno; resposta descartada.", conversaId);
            if (tokens > 0)
                await RegistrarUsoAsync(empresaId, tokens, agora);
            await unitOfWork.CommitAsync();
            return new ResultadoTurnoAgente(ChamouLlm: true, Respondeu: false, Escalou: false);
        }

        var respondeu = false;
        if (!concluiu && motivoEscalada is null)
        {
            // Limite de iterações: avisa o cliente e passa para a dona.
            respondeu = await EnviarRespostaAsync(empresaId, conversa, configuracao.FraseEspera, agora, ct);
            motivoEscalada = $"o agente atingiu o limite de {MaximoIteracoes} iterações sem concluir a resposta";
        }
        else if (!string.IsNullOrWhiteSpace(textoFinal))
        {
            respondeu = await EnviarRespostaAsync(empresaId, conversa, textoFinal, agora, ct);
        }

        if (motivoEscalada is not null)
            await escalador.EscalarAsync(empresaId, conversa, motivoEscalada, agora, ct);

        if (tokens > 0)
            await RegistrarUsoAsync(empresaId, tokens, agora);

        await unitOfWork.CommitAsync();
        return new ResultadoTurnoAgente(true, respondeu,
            motivoEscalada is not null || conversa.Situacao == SituacaoConversa.Assumida);
    }

    private async Task<BlocoResultadoFerramentaLlm> ExecutarFerramentaAsync(
        ContextoTurnoAgente contexto, BlocoUsoFerramentaLlm uso, CancellationToken ct)
    {
        if (!_ferramentas.TryGetValue(uso.Nome, out var ferramenta))
            return new BlocoResultadoFerramentaLlm(uso.Id, $"Ferramenta desconhecida: {uso.Nome}.", EhErro: true);

        try
        {
            return new BlocoResultadoFerramentaLlm(uso.Id, await ferramenta.ExecutarAsync(contexto, uso.Entrada, ct));
        }
        catch (Exception ex) when (ex is not OperationCanceledException || !ct.IsCancellationRequested)
        {
            logger.LogError(ex, "Agente de atendimento: ferramenta {Ferramenta} falhou na conversa {ConversaId}.",
                ferramenta.Nome, contexto.Conversa.Id);
            return new BlocoResultadoFerramentaLlm(uso.Id, "A ferramenta falhou; não invente o resultado.", EhErro: true);
        }
    }

    private async Task<bool> EnviarRespostaAsync(Guid empresaId, Conversa conversa, string texto, DateTime agora, CancellationToken ct)
    {
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
            saida.RegistrarFalhaEnvio(ex.Message, ClassificadorFalhaEnvio.Classificar(ex), saida.EnviadaEm);
        }

        // encerrar_conversa pode ter fechado a conversa neste turno: a despedida ainda vai para o histórico.
        if (conversa.EstaAberta) conversa.RegistrarSaida(agora);
        await conversaRepository.AddMensagemAsync(saida, ct);
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
                m.Autor == AutorMensagem.Dona || (legado && m.Autor == AutorMensagem.Agente));
            if (!respondida) pendentes.Add(msg);
        }

        return pendentes;
    }

    /// <summary>
    /// Marca no rastreamento as entradas que este turno levou ao LLM. O histórico vem sem tracking:
    /// a entidade rastreada é buscada pelo <c>wamid</c>.
    /// </summary>
    private async Task MarcarProcessadasAsync(Guid empresaId, IReadOnlyList<Mensagem> historico, DateTime agora, CancellationToken ct)
    {
        foreach (var entrada in historico.Where(m => m.Autor == AutorMensagem.Cliente && m.ProcessadaEm is null))
        {
            if (entrada.ExternoId is not { } externoId) continue;
            var rastreada = await conversaRepository.ObterMensagemPorExternoIdAsync(empresaId, externoId, ct);
            rastreada?.MarcarProcessada(agora);
        }
    }

    /// <summary>
    /// Cliente → <c>user</c>; agente e dona → <c>assistant</c>. Mensagens do sistema não entram na
    /// conversa (vão para o dossiê). Mensagens seguidas do mesmo papel viram uma só. As
    /// <paramref name="pendentes"/> vão para o fim: o agente ainda não as viu (#1288).
    /// </summary>
    private static List<MensagemLlm> MontarConversacao(IReadOnlyList<Mensagem> historico, IReadOnlyList<Mensagem> pendentes)
    {
        var resultado = new List<MensagemLlm>();
        var ordenado = historico.Where(m => !pendentes.Contains(m)).Concat(pendentes);
        foreach (var msg in ordenado.Where(m => m.Autor != AutorMensagem.Sistema))
        {
            var papel = msg.Autor == AutorMensagem.Cliente ? MensagemLlm.Usuario : MensagemLlm.Assistente;
            var texto = msg.Autor switch
            {
                AutorMensagem.Cliente => TextoDoCliente(msg),
                AutorMensagem.Dona => $"(dona) {msg.Texto}",
                _ => msg.Texto ?? string.Empty
            };
            if (string.IsNullOrWhiteSpace(texto)) continue;

            if (resultado.Count > 0 && resultado[^1].Papel == papel)
                resultado[^1] = resultado[^1] with { Conteudo = [.. resultado[^1].Conteudo, new BlocoTextoLlm(texto)] };
            else
                resultado.Add(new MensagemLlm(papel, [new BlocoTextoLlm(texto)]));
        }

        // A API exige que a conversa comece pelo usuário.
        while (resultado.Count > 0 && resultado[0].Papel != MensagemLlm.Usuario)
            resultado.RemoveAt(0);

        return resultado;
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

        var nome = cliente?.Nome is { } n && n != IdentificarClientePorTelefoneUseCase.NomePadraoLead ? n : conversa.ContatoNome;
        sb.AppendLine($"- Nome: {(string.IsNullOrWhiteSpace(nome) ? "não informado" : nome)}.");
        sb.AppendLine(cliente is null || cliente.OrderCount == 0
            ? "- Situação: lead (ainda não comprou)."
            : $"- Situação: cliente com {cliente.OrderCount} pedido(s).");
        sb.AppendLine(conversa.PedidoEmAndamentoId is { } pedidoId
            ? $"- Pedido em andamento: {pedidoId}."
            : "- Pedido em andamento: nenhum registrado nesta conversa.");
        if (conversa.ContextoJson != "{}")
            sb.AppendLine($"- Contexto salvo: {conversa.ContextoJson}");
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
            foreach (var auto in automaticas) sb.AppendLine($"- {auto.Texto}");
        }

        return sb.ToString().TrimEnd();
    }
}
