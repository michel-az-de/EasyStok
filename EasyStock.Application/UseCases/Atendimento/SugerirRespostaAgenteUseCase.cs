using System.Diagnostics;
using EasyStock.Application.Ports.Output.Ai;
using EasyStock.Application.Ports.Output.Persistence.Atendimento;
using EasyStock.Application.Services.Atendimento;
using EasyStock.Application.Services.Atendimento.Ferramentas;
using EasyStock.Application.UseCases.Atendimento.Inbox;
using EasyStock.Application.UseCases.Cliente.Dossie;
using EasyStock.Domain.Entities.Atendimento;

namespace EasyStock.Application.UseCases.Atendimento;

public sealed record SugerirRespostaAgenteCommand(Guid EmpresaId, Guid ConversaId);

public sealed record SugestaoAgenteResult(string Texto, int Tokens, long LatenciaMs);

/// <summary>Agente sem LLM (<c>Anthropic:Enabled=false</c> ou sem chave) ou sem resposta: HTTP 503.</summary>
public sealed class AgenteIndisponivelException(string mensagem) : InvalidOperationException(mensagem)
{
    public const string MensagemDesligado =
        "Agente indisponível: configure Anthropic:Enabled=true e Anthropic:ApiKey no servidor.";
}

/// <summary>
/// "Sugerir" do painel do agente no console (#1420): a mesma geração do turno do agente (prompt, caderno,
/// dossiê, histórico com transcrições), só com as ferramentas de consulta. Somente leitura: o texto volta
/// para a dona revisar; nada vira <c>Mensagem</c>, nada sai para o cliente e a situação da conversa não muda.
/// </summary>
public sealed class SugerirRespostaAgenteUseCase(
    IAgenteLlmClient llm,
    IConversaRepository conversaRepository,
    IConfiguracaoAtendimentoRepository configuracaoRepository,
    IClienteRepository clienteRepository,
    IEnumerable<IFerramentaAgente> ferramentas,
    ObterDossieClienteUseCase dossieUseCase,
    ICadernoRepository caderno,
    TimeProvider relogio,
    ILogger<SugerirRespostaAgenteUseCase> logger)
{
    /// <summary>Ferramentas sem efeito colateral. As demais criam pedido, enviam botões, escalam ou gravam.</summary>
    public static readonly IReadOnlySet<string> FerramentasDeConsulta = new HashSet<string>(StringComparer.Ordinal)
    {
        "consultar_cardapio", "consultar_caderno", "consultar_pedido", "listar_endereco_salvo",
    };

    internal const string InstrucaoSugestao =
        "MODO SUGESTÃO: a dona assumiu esta conversa e pediu uma sugestão. Escreva só o texto da próxima " +
        "mensagem da loja para o cliente, pronto para ela revisar e enviar; nada do que você escrever sai sozinho. " +
        "Você só tem ferramentas de consulta: não crie pedido, não encerre nem escale. Se a resposta depender de " +
        "algo que só a dona decide, escreva a mensagem mesmo assim e deixe esse ponto entre colchetes.";

    internal const string PedidoDaDona = "[pedido interno da dona] Sugira a próxima mensagem da loja para o cliente.";

    private readonly IReadOnlyDictionary<string, IFerramentaAgente> _consultas = ferramentas
        .Where(f => FerramentasDeConsulta.Contains(f.Nome))
        .ToDictionary(f => f.Nome, StringComparer.Ordinal);

    public async Task<SugestaoAgenteResult> ExecuteAsync(SugerirRespostaAgenteCommand command, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(command);
        if (!llm.Disponivel)
            throw new AgenteIndisponivelException(AgenteIndisponivelException.MensagemDesligado);

        var agora = relogio.GetUtcNow().UtcDateTime;
        var dados = await conversaRepository.ObterComMensagensAsync(
            command.EmpresaId, command.ConversaId, AgenteAtendimentoService.UltimasMensagens, ct)
            ?? throw new ConversaNaoEncontradaException(command.ConversaId);
        var conversa = dados.Conversa;

        var configuracao = await configuracaoRepository.GetByEmpresaIdAsync(command.EmpresaId)
            ?? ConfiguracaoAtendimento.CriarPadrao(command.EmpresaId);
        var cliente = conversa.ClienteId is { } clienteId
            ? await clienteRepository.GetByIdAsync(command.EmpresaId, clienteId)
            : null;

        var system = await GeracaoAgenteAtendimento.MontarSystemAsync(
            command.EmpresaId, configuracao, conversa, cliente, dados.Mensagens, agora, caderno, dossieUseCase, ct)
            + "\n\n" + InstrucaoSugestao;

        // O pedido da dona fecha a conversa como fala do usuário, que a API do LLM exige no fim.
        var mensagens = GeracaoAgenteAtendimento.MontarConversacao(dados.Mensagens, []);
        GeracaoAgenteAtendimento.AcrescentarTexto(mensagens, MensagemLlm.Usuario, PedidoDaDona);

        var cronometro = Stopwatch.StartNew();
        var geracao = await GeracaoAgenteAtendimento.GerarAsync(
            llm, system, mensagens, _consultas, new ContextoTurnoAgente(command.EmpresaId, conversa, agora),
            AgenteAtendimentoService.MaximoIteracoes, logger, ct);
        cronometro.Stop();

        logger.LogInformation(
            "Sugestão do agente para a dona: empresa {EmpresaId}, conversa {ConversaId}, {Tokens} tokens, {LatenciaMs} ms.",
            command.EmpresaId, command.ConversaId, geracao.Tokens, cronometro.ElapsedMilliseconds);

        if (string.IsNullOrWhiteSpace(geracao.Texto))
            throw new AgenteIndisponivelException(
                $"O agente não conseguiu sugerir agora ({geracao.MotivoFalha ?? "sem resposta final"}). Tente de novo.");

        return new SugestaoAgenteResult(TextoWhatsApp.SemTravessao(geracao.Texto), geracao.Tokens, cronometro.ElapsedMilliseconds);
    }
}
