using EasyStock.Application.Ports.Output.Ai;
using EasyStock.Application.Ports.Output.Persistence.Atendimento;
using EasyStock.Application.Services.Atendimento;
using EasyStock.Application.Services.Atendimento.Ferramentas;
using EasyStock.Application.Services.Notifications;
using EasyStock.Application.UseCases.Atendimento.Inbox;
using EasyStock.Application.UseCases.Cliente.Dossie;
using EasyStock.Domain.Entities.Atendimento;
using EasyStock.Domain.Enums.Atendimento;
using Microsoft.Extensions.Options;

namespace EasyStock.Application.UseCases.Atendimento;

public sealed record FerramentaDoAssistente(string Nome, string Descricao);

/// <param name="Ativo">Chave "Atendimento automático" da loja.</param>
/// <param name="Responde">Se o robô responderia agora à próxima mensagem do cliente nesta conversa.</param>
/// <param name="Bloqueio">Detalhe da primeira etapa que barra; nulo quando responde.</param>
/// <param name="EntradasPendentes">Mensagens do cliente que ninguém respondeu ainda.</param>
/// <param name="Prompt">Instruções exatas que o robô recebe nesta conversa: prompt da loja, caderno e dossiê.</param>
public sealed record ChecagemAssistenteResult(
    bool Ativo,
    bool Responde,
    string? Bloqueio,
    int EntradasPendentes,
    IReadOnlyList<EtapaAssistente> Etapas,
    string Prompt,
    IReadOnlyList<FerramentaDoAssistente> Ferramentas);

/// <summary>
/// #2496: checagem do assistente numa conversa. Roda as mesmas etapas do turno (<see cref="PortoesDoAssistente"/>)
/// sem escalar, sem chamar o modelo e sem gravar nada, e mostra todas, não só a primeira que barra.
/// </summary>
public sealed class ChecarAssistenteConversaUseCase(
    IAgenteLlmClient llm,
    IConversaRepository conversas,
    IConfiguracaoAtendimentoRepository configuracoes,
    IClienteRepository clientes,
    ICadernoRepository caderno,
    ObterDossieClienteUseCase dossies,
    IEnumerable<IFerramentaAgente> ferramentas,
    IContatoDaEquipeAvisada? equipe = null,
    IOptions<LimiteRespostasAgenteOptions>? limiteRespostas = null)
{
    public async Task<ChecagemAssistenteResult> ExecuteAsync(Guid empresaId, Guid conversaId, DateTime agora, CancellationToken ct = default)
    {
        var dados = await conversas.ObterComMensagensAsync(empresaId, conversaId, AgenteAtendimentoService.UltimasMensagens, ct)
            ?? throw new ConversaNaoEncontradaException(conversaId);
        var conversa = dados.Conversa;
        var configuracao = await configuracoes.GetByEmpresaIdAsync(empresaId) ?? ConfiguracaoAtendimento.CriarPadrao(empresaId);
        var cliente = conversa.ClienteId is { } clienteId ? await clientes.GetByIdAsync(empresaId, clienteId) : null;
        var portoes = new PortoesDoAssistente(llm, conversas, equipe, limiteRespostas);

        var etapas = PortoesDoAssistente.DaConversa(conversa, agora).Select(e => ParaProximaMensagem(e, conversa))
            .Concat(await portoes.DaLojaAsync(empresaId, configuracao, conversa, ct))
            .Concat(PortoesDoAssistente.DaPessoa(cliente))
            .Append(await portoes.DoLimiteAsync(empresaId, conversaId, agora, ct))
            .ToList();
        var bloqueio = etapas.FirstOrDefault(e => e.Barra);
        var prompt = await GeracaoAgenteAtendimento.MontarSystemAsync(
            empresaId, configuracao, conversa, cliente, dados.Mensagens, agora, caderno, dossies, ct);

        return new ChecagemAssistenteResult(
            configuracao.Ativo,
            bloqueio is null,
            bloqueio?.Detalhe,
            AgenteAtendimentoService.EntradasPendentes(dados.Mensagens).Count,
            etapas,
            prompt,
            ferramentas.Select(f => new FerramentaDoAssistente(f.Nome, f.Descricao)).OrderBy(f => f.Nome, StringComparer.Ordinal).ToList());
    }

    // A pergunta é "responde à próxima mensagem": a mensagem nova do cliente reabre a janela de 24 h, e numa
    // conversa concluída abre outra, que começa com o robô.
    private static EtapaAssistente ParaProximaMensagem(EtapaAssistente etapa, Conversa conversa) => etapa switch
    {
        { Codigo: PortoesDoAssistente.Janela, Barra: true } => EtapaAssistente.Fora(etapa.Codigo, etapa.Titulo,
            "Mais de 24 h sem mensagem do cliente. A próxima mensagem dele reabre a janela."),
        { Codigo: PortoesDoAssistente.Situacao, Barra: true } when conversa.Situacao == SituacaoConversa.Encerrada =>
            EtapaAssistente.Fora(etapa.Codigo, etapa.Titulo,
                "Conversa concluída. A próxima mensagem do cliente abre outra, que começa com o robô."),
        _ => etapa,
    };
}
