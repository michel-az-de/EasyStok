using EasyStock.Application.Ports.Output.Ai;
using EasyStock.Application.Ports.Output.Persistence.Atendimento;
using EasyStock.Application.Services.Notifications;
using EasyStock.Application.UseCases.Atendimento;
using EasyStock.Domain.Entities.Atendimento;
using EasyStock.Domain.Enums.Atendimento;
using Microsoft.Extensions.Options;

namespace EasyStock.Application.Services.Atendimento;

public enum EstadoEtapaAssistente
{
    Passa = 1,
    Barra = 2,
    NaoSeAplica = 3,
}

/// <param name="Codigo">Chave estável da etapa, para a tela e o teste.</param>
/// <param name="MotivoEscalada">Motivo gravado na conversa quando a etapa barra e passa a conversa para a equipe.</param>
public sealed record EtapaAssistente(
    string Codigo, string Titulo, EstadoEtapaAssistente Estado, string Detalhe, string? MotivoEscalada = null)
{
    public bool Barra => Estado == EstadoEtapaAssistente.Barra;

    public static EtapaAssistente Passou(string codigo, string titulo, string detalhe) =>
        new(codigo, titulo, EstadoEtapaAssistente.Passa, detalhe);

    public static EtapaAssistente Barrou(string codigo, string titulo, string detalhe, string? motivoEscalada = null) =>
        new(codigo, titulo, EstadoEtapaAssistente.Barra, detalhe, motivoEscalada);

    public static EtapaAssistente Fora(string codigo, string titulo, string detalhe) =>
        new(codigo, titulo, EstadoEtapaAssistente.NaoSeAplica, detalhe);
}

/// <summary>
/// #2496: as etapas que decidem se o robô responde, na ordem do turno. O <see cref="AgenteAtendimentoService"/> para
/// na primeira que barra; a checagem da conversa no console mostra todas. Uma classe só, para a tela nunca dizer uma
/// coisa e o robô fazer outra.
/// </summary>
public sealed class PortoesDoAssistente(
    IAgenteLlmClient llm,
    IConversaRepository conversas,
    IContatoDaEquipeAvisada? equipe = null,
    IOptions<LimiteRespostasAgenteOptions>? limiteRespostas = null)
{
    public const string Situacao = "situacao";
    public const string Vinculo = "vinculo";
    public const string Janela = "janela";
    public const string Chave = "chave";
    public const string Modelo = "modelo";
    public const string Canal = "canal";
    public const string Equipe = "equipe";
    public const string Bloqueado = "bloqueado";
    public const string Pausado = "pausado";
    public const string Limite = "limite";

    /// <summary>Estado da conversa. Barrando, o turno não chama o modelo nem escala: só não responde.</summary>
    public static IReadOnlyList<EtapaAssistente> DaConversa(Conversa conversa, DateTime agora) =>
    [
        conversa.Situacao switch
        {
            SituacaoConversa.Automatica => EtapaAssistente.Passou(Situacao, "Quem atende", "O Assistente atende esta conversa."),
            SituacaoConversa.Assumida => EtapaAssistente.Barrou(Situacao, "Quem atende",
                string.IsNullOrWhiteSpace(conversa.MotivoEscalada)
                    ? "A equipe atende esta conversa. Devolva ao Assistente para ele voltar."
                    : $"A equipe atende esta conversa ({conversa.MotivoEscalada}). Devolva ao Assistente para ele voltar."),
            _ => EtapaAssistente.Barrou(Situacao, "Quem atende", "Conversa concluída. A próxima mensagem abre outra."),
        },
        conversa.VinculoEmRevisao
            ? EtapaAssistente.Barrou(Vinculo, "Cadastro da conversa", "A troca de cliente desta conversa está em revisão.")
            : EtapaAssistente.Passou(Vinculo, "Cadastro da conversa", "Sem revisão de cadastro pendente."),
        conversa.DentroDaJanela(agora)
            ? EtapaAssistente.Passou(Janela, "Janela de 24 h", "O cliente escreveu nas últimas 24 h.")
            : EtapaAssistente.Barrou(Janela, "Janela de 24 h",
                "Mais de 24 h sem mensagem do cliente: só modelo aprovado pela Meta pode sair."),
    ];

    /// <summary>Loja, modelo, canal e contato. Barrando, a conversa passa para a equipe com o motivo.</summary>
    public async Task<IReadOnlyList<EtapaAssistente>> DaLojaAsync(
        Guid empresaId, ConfiguracaoAtendimento configuracao, Conversa conversa, CancellationToken ct)
    {
        var etapas = new List<EtapaAssistente>
        {
            configuracao.Ativo
                ? EtapaAssistente.Passou(Chave, "Assistente na loja", "Ligado: o Assistente responde sozinho.")
                : EtapaAssistente.Barrou(Chave, "Assistente na loja", "Desligado: toda conversa vai para a equipe.",
                    AgenteAtendimentoService.MotivoAtendimentoAutomaticoDesligado),
            llm.Disponivel
                ? EtapaAssistente.Passou(Modelo, "Modelo de IA", "Disponível no servidor.")
                : EtapaAssistente.Barrou(Modelo, "Modelo de IA", "Desligado ou sem chave no servidor.",
                    AgenteAtendimentoService.MotivoAgenteDesligado),
            conversa.TemAgente
                ? EtapaAssistente.Passou(Canal, "Canal", "O Assistente atende no WhatsApp.")
                : EtapaAssistente.Barrou(Canal, "Canal", $"O Assistente não atende no canal {conversa.Canal}.",
                    $"o canal {conversa.Canal} não tem agente"),
        };

        var daEquipe = equipe is not null && await equipe.EhInscritoAsync(empresaId, conversa.ContatoIdExterno, ct);
        etapas.Add(!daEquipe
            ? EtapaAssistente.Passou(Equipe, "Contato da equipe", "Não recebe os avisos da equipe.")
            : conversa.AutomaticoLiberadoPelaEquipe
                ? EtapaAssistente.Passou(Equipe, "Contato da equipe",
                    "Recebe os avisos da equipe, mas esta conversa foi devolvida ao Assistente.")
                : EtapaAssistente.Barrou(Equipe, "Contato da equipe",
                    "Recebe os avisos da equipe: o Assistente só responde depois que alguém devolver esta conversa a ele.",
                    AgenteAtendimentoService.MotivoContatoDaEquipe));
        return etapas;
    }

    /// <summary>Cadastro vinculado. Barrando, a conversa passa para a equipe com o motivo.</summary>
    public static IReadOnlyList<EtapaAssistente> DaPessoa(Cliente? cliente) => cliente is null
        ? [
            EtapaAssistente.Fora(Bloqueado, "Cliente bloqueado", "Sem cadastro vinculado."),
            EtapaAssistente.Fora(Pausado, "Encaminhado à responsável", "Sem cadastro vinculado."),
        ]
        : [
            cliente.Bloqueado
                ? EtapaAssistente.Barrou(Bloqueado, "Cliente bloqueado", "Cadastro bloqueado: a equipe atende.",
                    EscalarConversaUseCase.MotivoClienteBloqueado(cliente))
                : EtapaAssistente.Passou(Bloqueado, "Cliente bloqueado", "Cadastro sem bloqueio."),
            cliente.AtendimentoAutomaticoPausado
                ? EtapaAssistente.Barrou(Pausado, "Encaminhado à responsável",
                    "Encaminhado à responsável: o Assistente volta quando a equipe devolver a conversa.",
                    EscalarConversaUseCase.MotivoContatoPausado(cliente))
                : EtapaAssistente.Passou(Pausado, "Encaminhado à responsável", "Não foi encaminhado."),
        ];

    /// <summary>#2285: anti-loop de custo. Barrando, a conversa passa para a equipe.</summary>
    public async Task<EtapaAssistente> DoLimiteAsync(Guid empresaId, Guid conversaId, DateTime agora, CancellationToken ct)
    {
        var maximo = limiteRespostas?.Value.MaximoPorHora ?? LimiteRespostasAgenteOptions.PadraoPorHora;
        if (maximo <= 0)
            return EtapaAssistente.Fora(Limite, "Respostas na última hora", "Sem limite configurado.");

        var respostas = await conversas.ContarRespostasDoAgenteDesdeAsync(empresaId, conversaId, agora.AddHours(-1), ct);
        var detalhe = $"{respostas} de {maximo} respostas do Assistente na última hora.";
        return respostas >= maximo
            ? EtapaAssistente.Barrou(Limite, "Respostas na última hora", detalhe + " Parou para evitar loop.",
                AgenteAtendimentoService.MotivoLimiteRespostas(maximo))
            : EtapaAssistente.Passou(Limite, "Respostas na última hora", detalhe);
    }
}
