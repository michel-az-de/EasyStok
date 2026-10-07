using System.Diagnostics;
using System.Text;
using EasyStock.Application.Ports.Output.Ai;
using EasyStock.Application.Ports.Output.Persistence.Atendimento;
using EasyStock.Application.Services.Atendimento;
using EasyStock.Application.UseCases.Atendimento.Inbox;
using EasyStock.Domain.Enums.Atendimento;

namespace EasyStock.Application.UseCases.Atendimento;

public sealed record PerguntarAssistenteDonaCommand(Guid EmpresaId, string Pergunta, Guid? ConversaId);

public sealed record RespostaAssistenteDonaResult(
    string Resposta, int TokensEntrada, int TokensSaida, long LatenciaMs, IReadOnlyList<AcaoPropostaAssistente> Acoes);

public sealed record AcaoPropostaAssistente(string Tipo, string? Texto, string? Tela)
{
    public const string EnviarCardapio = "enviar_cardapio";
    public const string NotaInterna = "nota_interna";
    public const string Rascunho = "rascunho";
    public const string AbrirTela = "abrir_tela";
}

/// <summary>Sem <c>Anthropic:Enabled</c>/<c>Anthropic:ApiKey</c> o assistente não responde (HTTP 503).</summary>
public sealed class AssistenteDonaIndisponivelException()
    : InvalidOperationException("Assistente indisponível: configure Anthropic:Enabled=true e Anthropic:ApiKey no servidor.");

/// <summary>
/// Assistente da dona (S47): tira dúvidas (CDC, atendimento, pergunta livre) com contexto opcional da
/// conversa. Usa o mesmo cliente Messages do agente (S06), com system prompt próprio. Com conversa, o
/// modelo pode <b>propor</b> ações (#1445, <see cref="PropostasAssistenteDona"/>): mandar o cardápio,
/// anotar nota interna, preencher o rascunho, abrir cardápio ou comanda. Nada executa aqui: as propostas
/// voltam em <see cref="RespostaAssistenteDonaResult.Acoes"/> e só o clique da atendente no console faz
/// a ação. A resposta nunca vira <c>Mensagem</c> nem sai para o cliente. Tokens e latência vão para o log.
/// </summary>
public sealed class AssistenteDonaUseCase(
    IAgenteLlmClient llm,
    IConversaRepository conversaRepository,
    ILogger<AssistenteDonaUseCase> logger)
{
    public const int TamanhoMaximoPergunta = 2000;
    public const int MensagensDeContexto = 20;

    public const string SystemPrompt =
        "Você é o assistente interno da atendente de um pequeno negócio de alimentação no Brasil. " +
        "Você fala SOMENTE com a atendente, nunca com o cliente. Ajude com dúvidas de atendimento, " +
        "direitos do consumidor (CDC), trocas, reembolsos, atrasos e redação de respostas. " +
        "Responda em português brasileiro, em no máximo 2 frases curtas, sem floreio, sem markdown e sem " +
        "repetir a pergunta. Quando citar o CDC, indique o artigo. Não invente fatos sobre o pedido. " +
        "Quando ela pedir uma ação, use a ferramenta certa em vez de explicar como fazer: mandar o cardápio, " +
        "anotar algo no cliente, escrever a resposta para o cliente (vai para o rascunho) ou abrir o cardápio " +
        "ou a comanda. A ação só acontece depois que a atendente confirmar na tela: nunca diga que já " +
        "enviou, anotou ou abriu. Texto para o cliente vai sempre pela ferramenta de rascunho, como " +
        "mensagem de WhatsApp de 1 a 3 frases curtas. Nunca use travessão.";

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

        // Sem conversa não há cliente para receber, anotar nem abrir: nenhuma ferramenta.
        var requisicao = new RequisicaoLlm(
            SystemPrompt,
            [new MensagemLlm(MensagemLlm.Usuario, [new BlocoTextoLlm(texto)])],
            contexto is null ? [] : PropostasAssistenteDona.Ferramentas);

        var cronometro = Stopwatch.StartNew();
        var resposta = await llm.EnviarAsync(requisicao, ct);
        cronometro.Stop();

        // Uma chamada só, mesmo com stop_reason=tool_use: a proposta não roda aqui, então não há
        // resultado de ferramenta para devolver ao modelo.
        var textoResposta = TextoWhatsApp.SemTravessao(
            string.Join("\n", resposta.Conteudo.OfType<BlocoTextoLlm>().Select(b => b.Texto)).Trim());
        var acoes = contexto is null
            ? []
            : PropostasAssistenteDona.Traduzir(resposta.Conteudo.OfType<BlocoUsoFerramentaLlm>());

        logger.LogInformation(
            "Assistente da dona respondeu empresa {EmpresaId} (conversa {ConversaId}): {TokensEntrada} tokens de entrada, {TokensSaida} de saída, {LatenciaMs} ms, {Acoes} ações propostas",
            command.EmpresaId, command.ConversaId, resposta.TokensEntrada, resposta.TokensSaida, cronometro.ElapsedMilliseconds, acoes.Count);

        return new RespostaAssistenteDonaResult(
            textoResposta, resposta.TokensEntrada, resposta.TokensSaida, cronometro.ElapsedMilliseconds, acoes);
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
