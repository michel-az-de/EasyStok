using System.Text;
using EasyStock.Application.Ports.Output.Ai;
using EasyStock.Application.Ports.Output.Persistence.Atendimento;
using EasyStock.Application.Services.Atendimento.Ferramentas;
using EasyStock.Application.UseCases.Atendimento;
using EasyStock.Application.UseCases.Cliente.Dossie;
using EasyStock.Domain.Entities.Atendimento;
using EasyStock.Domain.Enums.Atendimento;

namespace EasyStock.Application.Services.Atendimento;

/// <summary>
/// Resultado do laço do LLM. <see cref="Concluiu"/> falso sem <see cref="MotivoFalha"/> = atingiu o
/// limite de iterações sem resposta final.
/// </summary>
public sealed record GeracaoAgente(string? Texto, int Tokens, bool Concluiu, string? MotivoFalha);

/// <summary>
/// Geração do agente de atendimento, sem efeito colateral próprio: monta o contexto (prompt fixo, caderno,
/// dossiê, histórico) e roda o laço LLM + ferramentas. Quem chama decide o que fazer com o texto: o turno
/// (<see cref="AgenteAtendimentoService"/>) envia a resposta verificada das ferramentas ou o texto que passar pela
/// <see cref="GuardaRespostaAgente"/>; a sugestão da dona (#1420) só devolve.
/// </summary>
public static class GeracaoAgenteAtendimento
{
    /// <summary>
    /// System prompt do turno. S54: caderno da loja (núcleo + índice) logo depois do prompt base e antes do
    /// dossiê, para o começo do prompt ser igual entre conversas da mesma empresa e aproveitar o cache do
    /// provedor. S25: histórico do cadastro (tags, pedidos, favorito, notas [interno]) quando há cliente.
    /// </summary>
    public static async Task<string> MontarSystemAsync(
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
        var system = PromptAtendimento.Montar(configuracao)
            + (blocoCaderno.Length > 0 ? "\n\n" + blocoCaderno : string.Empty)
            + "\n\n" + MontarDossie(conversa, cliente, historico, agora);

        if (cliente is not null
            && await dossieUseCase.ExecuteAsync(new ObterDossieClienteQuery(empresaId, cliente.Id), ct) is { } dossie)
            system += "\n\n" + ResumoDossieParaAgente.Montar(dossie);

        return system;
    }

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
        CancellationToken ct)
    {
        var definicoes = ferramentas.Values
            .Select(f => new FerramentaLlm(f.Nome, f.Descricao, f.SchemaJson))
            .ToList();
        var tokens = 0;
        var situacaoInicial = contexto.Conversa.Situacao;

        for (var iteracao = 0; iteracao < maximoIteracoes; iteracao++)
        {
            RespostaLlm resposta;
            try
            {
                resposta = await llm.EnviarAsync(new RequisicaoLlm(system, [.. mensagens], definicoes), ct);
            }
            catch (Exception ex) when (ex is not OperationCanceledException || !ct.IsCancellationRequested)
            {
                contexto.LimparRespostaVerificada();
                logger.LogError(ex, "Agente de atendimento: falha chamando o LLM na conversa {ConversaId}.", contexto.Conversa.Id);
                return new GeracaoAgente(null, tokens, false, "o agente não conseguiu responder (falha ao chamar o LLM)");
            }

            tokens += resposta.TokensEntrada + resposta.TokensSaida;

            if (resposta.StopReason != RespostaLlm.StopToolUse)
            {
                var texto = string.Join("\n", resposta.Conteudo.OfType<BlocoTextoLlm>().Select(b => b.Texto)).Trim();
                return new GeracaoAgente(texto, tokens, true,
                    texto.Length == 0 && !(situacaoInicial == SituacaoConversa.Automatica
                        && (contexto.EnvioVerificado || contexto.RespostaVerificada is { Length: > 0 }))
                        ? $"o agente não produziu resposta (stop_reason={resposta.StopReason})" : null);
            }

            mensagens.Add(new MensagemLlm(MensagemLlm.Assistente, resposta.Conteudo));
            var resultados = new List<BlocoLlm>();
            foreach (var uso in resposta.Conteudo.OfType<BlocoUsoFerramentaLlm>())
            {
                resultados.Add(await ExecutarFerramentaAsync(ferramentas, contexto, uso, logger, ct));
                if (situacaoInicial == SituacaoConversa.Automatica && contexto.Conversa.Situacao != SituacaoConversa.Automatica)
                {
                    if (contexto.Conversa.Situacao != SituacaoConversa.Encerrada) contexto.LimparRespostaVerificada();
                    return new GeracaoAgente(null, tokens, true, null);
                }
            }
            mensagens.Add(new MensagemLlm(MensagemLlm.Usuario, resultados));
        }

        return new GeracaoAgente(null, tokens, false, null);
    }

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
                AutorMensagem.Cliente => TextoDoCliente(msg),
                AutorMensagem.Dona => $"(dona) {msg.Texto}",
                AutorMensagem.Atendente => $"(equipe) {msg.Texto}",
                _ => msg.Texto ?? string.Empty
            };
            if (string.IsNullOrWhiteSpace(texto)) continue;
            if (msg.Status == StatusMensagem.Falhou) texto = $"{MarcadorNaoConfirmada} {texto}";

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
