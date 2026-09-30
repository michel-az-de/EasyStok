using System.Diagnostics;
using System.Text;
using EasyStock.Application.Ports.Output.Ai;
using EasyStock.Application.Ports.Output.Persistence.Atendimento;
using EasyStock.Application.UseCases.Atendimento.Inbox;
using EasyStock.Domain.Enums.Atendimento;

namespace EasyStock.Application.UseCases.Atendimento;

public sealed record PerguntarAssistenteDonaCommand(Guid EmpresaId, string Pergunta, Guid? ConversaId);

public sealed record RespostaAssistenteDonaResult(string Resposta, int TokensEntrada, int TokensSaida, long LatenciaMs);

/// <summary>Sem <c>Anthropic:Enabled</c>/<c>Anthropic:ApiKey</c> o assistente não responde (HTTP 503).</summary>
public sealed class AssistenteDonaIndisponivelException()
    : InvalidOperationException("Assistente indisponível: configure Anthropic:Enabled=true e Anthropic:ApiKey no servidor.");

/// <summary>
/// Assistente da dona (S47): tira dúvidas (CDC, atendimento, pergunta livre) com contexto opcional da
/// conversa. Usa o mesmo cliente Messages do agente (S06), com system prompt próprio e <b>sem
/// ferramentas</b>. É somente leitura: a resposta volta só para o console e nunca vira
/// <c>Mensagem</c> nem sai para o cliente. Tokens e latência vão para o log.
/// </summary>
public sealed class AssistenteDonaUseCase(
    IAgenteLlmClient llm,
    IConversaRepository conversaRepository,
    ILogger<AssistenteDonaUseCase> logger)
{
    public const int TamanhoMaximoPergunta = 2000;
    public const int MensagensDeContexto = 20;

    internal const string SystemPrompt =
        "Você é o assistente interno da dona de um pequeno negócio de alimentação no Brasil. " +
        "Você fala SOMENTE com a dona, nunca com o cliente. Ajude com dúvidas de atendimento, " +
        "direitos do consumidor (CDC), trocas, reembolsos, atrasos e redação de respostas. " +
        "Seja direto e prático, em português brasileiro, em no máximo 3 parágrafos curtos. " +
        "Quando citar o CDC, indique o artigo. Se sugerir um texto para o cliente, deixe claro que é " +
        "uma sugestão para a dona revisar e enviar ela mesma. Não invente fatos sobre o pedido.";

    public async Task<RespostaAssistenteDonaResult> ExecuteAsync(
        PerguntarAssistenteDonaCommand command, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(command);
        var pergunta = command.Pergunta?.Trim() ?? string.Empty;
        if (pergunta.Length == 0)
            throw new UseCaseValidationException("Informe a pergunta.");
        if (pergunta.Length > TamanhoMaximoPergunta)
            throw new UseCaseValidationException($"A pergunta não pode passar de {TamanhoMaximoPergunta} caracteres.");

        if (!llm.Disponivel)
            throw new AssistenteDonaIndisponivelException();

        var contexto = command.ConversaId is { } conversaId
            ? await MontarContextoAsync(command.EmpresaId, conversaId, ct)
            : null;

        var texto = contexto is null
            ? pergunta
            : $"Contexto da conversa com o cliente (somente leitura):\n{contexto}\n\nPergunta da dona: {pergunta}";

        var requisicao = new RequisicaoLlm(
            SystemPrompt,
            [new MensagemLlm(MensagemLlm.Usuario, [new BlocoTextoLlm(texto)])],
            []);

        var cronometro = Stopwatch.StartNew();
        var resposta = await llm.EnviarAsync(requisicao, ct);
        cronometro.Stop();

        var textoResposta = string.Join("\n", resposta.Conteudo.OfType<BlocoTextoLlm>().Select(b => b.Texto)).Trim();

        logger.LogInformation(
            "Assistente da dona respondeu empresa {EmpresaId} (conversa {ConversaId}): {TokensEntrada} tokens de entrada, {TokensSaida} de saída, {LatenciaMs} ms",
            command.EmpresaId, command.ConversaId, resposta.TokensEntrada, resposta.TokensSaida, cronometro.ElapsedMilliseconds);

        return new RespostaAssistenteDonaResult(textoResposta, resposta.TokensEntrada, resposta.TokensSaida, cronometro.ElapsedMilliseconds);
    }

    private async Task<string> MontarContextoAsync(Guid empresaId, Guid conversaId, CancellationToken ct)
    {
        var dados = await conversaRepository.ObterComMensagensAsync(empresaId, conversaId, MensagensDeContexto, ct)
            ?? throw new ConversaNaoEncontradaException(conversaId);

        var sb = new StringBuilder();
        foreach (var m in dados.Mensagens.Where(m => !string.IsNullOrWhiteSpace(m.Texto)).OrderBy(m => m.EnviadaEm))
            sb.Append(m.Direcao == DirecaoMensagem.Entrada ? "Cliente: " : "Loja: ").AppendLine(m.Texto);
        return sb.ToString();
    }
}
