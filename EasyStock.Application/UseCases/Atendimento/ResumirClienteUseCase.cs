using EasyStock.Application.Ports.Output.Ai;
using EasyStock.Application.Services.Atendimento;
using EasyStock.Application.UseCases.Atendimento.Inbox;
using EasyStock.Application.UseCases.Cliente.Dossie;

namespace EasyStock.Application.UseCases.Atendimento;

/// <param name="Texto">Uma ou duas frases para a atendente; nulo quando a IA está desligada ou falhou.</param>
/// <param name="PorIa">Falso quando o texto é a regra fixa (cliente sem compra).</param>
public sealed record ResumoClienteResult(string? Texto, bool PorIa);

/// <summary>
/// Resumo do cliente no topo do Resumo do Balcão (#1839): quem é, como costuma comprar e o que pede.
/// Lê o mesmo dossiê do painel (<see cref="ObterDossieClienteUseCase"/>) e o mesmo texto que o agente
/// recebe (<see cref="ResumoDossieParaAgente"/>), numa chamada só, sem ferramentas. Nunca sai para o
/// cliente. Sem IA configurada ou com falha, devolve texto nulo: a tela mostra só os números. O texto fica 12 h
/// em cache por cliente, pedidos, última compra e notas: abrir de novo não paga outra chamada.
/// </summary>
public sealed class ResumirClienteUseCase(
    IAgenteLlmClient llm, ObterDossieClienteUseCase dossies, ICacheService cache, ILogger<ResumirClienteUseCase> logger)
{
    public const string SystemPrompt =
        "Você resume para a atendente de uma casa de massas artesanais quem é o cliente da conversa, a partir do " +
        "histórico do CRM. Escreva em português brasileiro, no máximo 2 frases curtas, sem markdown e sem travessão. " +
        "Diga o que ajuda a atender agora: se é cliente frequente ou novo, o que costuma pedir, preferências, " +
        "restrições ou alertas das notas. Não invente nada que não esteja no histórico. Linhas marcadas como internas " +
        "são notas da equipe. Não fale de pagamento se o histórico não mostrar.";

    public const string SemCompra = "Ainda não comprou com a casa.";

    public async Task<ResumoClienteResult> ExecuteAsync(Guid empresaId, Guid conversaId, CancellationToken ct = default)
    {
        var dossie = await dossies.ObterPorConversaAsync(empresaId, conversaId, ct)
            ?? throw new ConversaNaoEncontradaException(conversaId);
        if (dossie.TotalPedidos == 0 && dossie.Notas.Count == 0) return new(SemCompra, false);
        if (!llm.Disponivel) return new(null, false);
        var notas = string.Join(',', dossie.Notas.Select(n => $"{n.Id:N}:{n.CriadoEm:O}"));
        var chave = $"resumo-cliente:{empresaId:N}:{dossie.Cliente.Id ?? conversaId:N}:{dossie.TotalPedidos}:{dossie.UltimaCompraEm:O}:{notas}";
        if (await cache.GetAsync<ResumoClienteResult>(chave) is { } guardado) return guardado;
        try
        {
            var resposta = await llm.EnviarAsync(new RequisicaoLlm(
                SystemPrompt,
                [new MensagemLlm(MensagemLlm.Usuario, [new BlocoTextoLlm(ResumoDossieParaAgente.Montar(dossie))])],
                []), ct);
            var texto = TextoWhatsApp.SemTravessao(
                string.Join(" ", resposta.Conteudo.OfType<BlocoTextoLlm>().Select(b => b.Texto)).Trim());
            logger.LogInformation("Resumo do cliente por IA: empresa {EmpresaId}, conversa {ConversaId}, {TokensEntrada}+{TokensSaida} tokens",
                empresaId, conversaId, resposta.TokensEntrada, resposta.TokensSaida);
            var resultado = new ResumoClienteResult(texto.Length == 0 ? null : texto, texto.Length > 0);
            if (resultado.PorIa) await cache.SetAsync(chave, resultado, TimeSpan.FromHours(12));
            return resultado;
        }
        // O timeout do cliente HTTP também é OperationCanceledException; só o cancelamento de quem pediu sobe.
        catch (Exception ex) when (ex is not OperationCanceledException || !ct.IsCancellationRequested)
        {
            logger.LogWarning(ex, "Resumo do cliente por IA falhou: empresa {EmpresaId}, conversa {ConversaId}", empresaId, conversaId);
            return new(null, false);
        }
    }
}
