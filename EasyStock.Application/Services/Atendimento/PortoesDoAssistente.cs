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

/// <summary>#2853: uma etapa do turno descrita para o mapa do fluxo, sem estado de conversa.</summary>
public sealed record PortaoDoFluxo(string Codigo, string Titulo, string Checa, bool Escala, string? QuemMuda);

/// <summary>
/// #2496: as etapas que decidem se o robô responde, na ordem do turno. O <see cref="AgenteAtendimentoService"/> para
/// na primeira que barra; a checagem da conversa no console mostra todas. Uma classe só, para a tela nunca dizer uma
/// coisa e o robô fazer outra.
/// </summary>
public sealed class PortoesDoAssistente(
    IAgenteLlmClient llm,
    IConversaRepository conversas,
    ITurnoAgenteRepository turnos,
    IOptions<LimiteRespostasAgenteOptions> limiteRespostas,
    IContatoDaEquipeAvisada? equipe = null)
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
    public const string Cota = "cota";

    public const string MotivoCotaEsgotada = "cota mensal de tokens do Assistente esgotada: a equipe responde até virar o mês";

    /// <summary>
    /// #2853: as etapas na ordem do turno, para o mapa do fluxo no console. <c>Escala</c>: barrando, a conversa passa para a
    /// equipe (as três primeiras só não respondem). <c>QuemMuda</c>: onde a etapa se ajusta, ou nulo quando é regra fixa.
    /// </summary>
    public static readonly IReadOnlyList<PortaoDoFluxo> Catalogo =
    [
        new(Situacao, "Quem atende", "A conversa está com o Assistente, não com a equipe.", false, "Assumir ou devolver na conversa"),
        new(Vinculo, "Cadastro da conversa", "Nenhuma troca de cliente em revisão.", false, null),
        new(Janela, "Janela de 24 h", "O cliente escreveu nas últimas 24 h (regra da Meta).", false, null),
        new(Chave, "Assistente na loja", "A chave do Assistente está ligada.", true, "Chave nesta tela"),
        new(Modelo, "Modelo de IA", "O servidor tem modelo e chave de API.", true, "Configuração do servidor"),
        new(Canal, "Canal", "A conversa é do WhatsApp ou do chat do site.", true, null),
        new(Equipe, "Contato da equipe", "Quem escreve não é contato que recebe os avisos da equipe.", true, "Devolver na conversa"),
        new(Bloqueado, "Cliente bloqueado", "O cadastro do cliente não está bloqueado.", true, "Cadastro do cliente"),
        new(Pausado, "Encaminhado à responsável", "O cliente não foi encaminhado à responsável.", true, "Devolver na conversa"),
        new(Limite, "Respostas na última hora", "O robô não passou do limite de respostas por hora na conversa.", true, "Configuração do servidor"),
        new(Cota, "Cota do mês", "A loja não gastou a cota mensal de tokens.", true, "Cota na aba Uso"),
    ];

    /// <summary>
    /// S69 (#2853): cota mensal de tokens da loja. Soma o mesmo registro dos turnos que o painel de uso mostra, no mês de
    /// Brasília. Barrando, o turno não chama o modelo e a conversa passa para a equipe.
    /// </summary>
    public async Task<EtapaAssistente> DaCotaAsync(
        Guid empresaId, ConfiguracaoAtendimento configuracao, DateTime agora, CancellationToken ct)
    {
        if (configuracao.CotaTokensMes is not { } cota || cota <= 0)
            return EtapaAssistente.Fora(Cota, "Cota do mês", "Sem cota mensal de tokens.");

        var usados = await UsadosNoMesAsync(turnos, empresaId, agora, ct);
        var detalhe = $"{usados:N0} de {cota:N0} tokens usados no mês.";
        return usados >= cota
            ? EtapaAssistente.Barrou(Cota, "Cota do mês", detalhe + " Cota esgotada: a equipe atende.", MotivoCotaEsgotada)
            : EtapaAssistente.Passou(Cota, "Cota do mês", detalhe);
    }

    public static async Task<long> UsadosNoMesAsync(ITurnoAgenteRepository turnos, Guid empresaId, DateTime agora, CancellationToken ct)
    {
        var (de, ate) = HorarioBrasil.JanelaMesUtc(HorarioBrasil.DataOperacional(agora));
        return (await turnos.SomarAsync(empresaId, de, ate, ct)).TotalTokens;
    }

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
            await llm.DisponivelAsync(empresaId, ct)
                ? EtapaAssistente.Passou(Modelo, "Modelo de IA", "Disponível no servidor.")
                : EtapaAssistente.Barrou(Modelo, "Modelo de IA", "Desligado ou sem chave no servidor.",
                    AgenteAtendimentoService.MotivoAgenteDesligado),
            conversa.TemAgente
                ? EtapaAssistente.Passou(Canal, "Canal", $"O Assistente atende no canal {(conversa.Canal == CanalConversa.ChatSite ? "chat do site" : "WhatsApp")}.")
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
        var maximo = limiteRespostas.Value.MaximoPorHora;
        if (maximo <= 0)
            return EtapaAssistente.Fora(Limite, "Respostas na última hora", "Sem limite configurado.");

        var respostas = await conversas.ContarRespostasDoAgenteDesdeAsync(empresaId, conversaId, agora.AddHours(-1), ct);
        var detalhe = $"{respostas} de {maximo} respostas do Assistente na última hora.";
        return respostas >= maximo
            ? EtapaAssistente.Barrou(Limite, "Respostas na última hora", detalhe + " Parou para evitar loop.",
                AgenteAtendimentoService.MotivoLimiteRespostas(maximo))
            : EtapaAssistente.Passou(Limite, "Respostas na última hora", detalhe);
    }

    /// <summary>
    /// #3298 (R11): a pessoa, o limite de respostas e a cota do mês, nesta ordem, e só até onde alguém consome: quem para na
    /// primeira que barra (<see cref="PrimeiraQueBarraDoClienteEDoCustoAsync"/>) não lê o banco das seguintes, e a checagem
    /// do console lê todas. A ordem mora aqui, para o turno, a retomada, a resposta sem modelo e a tela não divergirem.
    /// </summary>
    public async IAsyncEnumerable<EtapaAssistente> EtapasDoClienteEDoCustoAsync(
        Guid empresaId, Guid conversaId, ConfiguracaoAtendimento configuracao, Cliente? cliente, DateTime agora,
        [System.Runtime.CompilerServices.EnumeratorCancellation] CancellationToken ct)
    {
        foreach (var etapa in DaPessoa(cliente)) yield return etapa;
        yield return await DoLimiteAsync(empresaId, conversaId, agora, ct);
        yield return await DaCotaAsync(empresaId, configuracao, agora, ct);
    }

    /// <summary>
    /// A primeira etapa de <see cref="EtapasDoClienteEDoCustoAsync"/> que barra, ou nulo. As três têm
    /// <see cref="EtapaAssistente.MotivoEscalada"/> quando barram: quem recebe a etapa passa a conversa para a equipe com ele.
    /// </summary>
    public async Task<EtapaAssistente?> PrimeiraQueBarraDoClienteEDoCustoAsync(
        Guid empresaId, Guid conversaId, ConfiguracaoAtendimento configuracao, Cliente? cliente, DateTime agora, CancellationToken ct)
    {
        await foreach (var etapa in EtapasDoClienteEDoCustoAsync(empresaId, conversaId, configuracao, cliente, agora, ct))
            if (etapa.Barra) return etapa;
        return null;
    }

    /// <summary>
    /// #3356 (R11): todas as etapas na ordem do <see cref="Catalogo"/> (conversa, loja, pessoa, limite e cota), lidas só
    /// quando quem consome chega nelas: a tela consome até o fim, a retomada para na primeira que barra e não lê o grupo
    /// seguinte. A preguiça é entre grupos; o da loja sai inteiro (modelo, canal e contato da equipe), como no turno. O
    /// turno segue em etapas de propósito: entre um grupo e outro ele lê a mensagem, a configuração e o cadastro e, ao
    /// barrar, escala com o motivo do portão.
    /// </summary>
    public async IAsyncEnumerable<EtapaAssistente> EtapasAsync(
        Guid empresaId, Conversa conversa, ConfiguracaoAtendimento configuracao, Cliente? cliente, DateTime agora,
        [System.Runtime.CompilerServices.EnumeratorCancellation] CancellationToken ct)
    {
        foreach (var etapa in DaConversa(conversa, agora)) yield return etapa;
        foreach (var etapa in await DaLojaAsync(empresaId, configuracao, conversa, ct)) yield return etapa;
        await foreach (var etapa in EtapasDoClienteEDoCustoAsync(empresaId, conversa.Id, configuracao, cliente, agora, ct))
            yield return etapa;
    }
}
