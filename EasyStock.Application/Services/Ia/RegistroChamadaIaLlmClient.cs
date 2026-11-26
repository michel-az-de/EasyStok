using System.Diagnostics;
using EasyStock.Application.Ports.Output.Ai;
using EasyStock.Application.Services.Atendimento;
using EasyStock.Domain.Entities.Ia;
using EasyStock.Domain.Enums.Ia;
using Microsoft.Extensions.Options;

namespace EasyStock.Application.Services.Ia;

/// <summary>
/// #3063 (S2a de #2948): envolve o cliente de LLM e grava cada tentativa em <c>chamada_ia</c>. A resposta e a exceção
/// passam intactas; cancelamento e o que não saiu (#3064, A1) não gravam; falha ao gravar só vira log. Provedor e modelo
/// cadastrado ficam nulos: é o caminho do <c>.env</c>; o caminho por loja grava no <see cref="RoteadorIaLlmClient"/>.
/// #3365 (D2): o custo da chamada sai da tabela <c>Anthropic:Precos</c>, calculado aqui uma vez, gravado na linha e devolvido
/// em <see cref="RespostaLlm.CustoUsd"/>; o turno só o soma.
/// </summary>
public sealed class RegistroChamadaIaLlmClient(
    IAgenteLlmClient interno,
    IRegistroChamadaIa registro,
    TimeProvider relogio,
    ILogger<RegistroChamadaIaLlmClient> logger,
    IOptions<PrecosModelosAgenteOptions> precos,
    TimeSpan? prazoRegistro = null) : IAgenteLlmClient
{
    /// <summary>#3083: prazo da gravação; ver <see cref="GravacaoChamadaIa.PrazoPadrao"/>.</summary>
    public static readonly TimeSpan PrazoRegistroPadrao = GravacaoChamadaIa.PrazoPadrao;

    public bool Disponivel => interno.Disponivel;

    public Task<bool> DisponivelAsync(Guid empresaId, CancellationToken ct = default) => interno.DisponivelAsync(empresaId, ct);

    public IAgenteLlmClient Interno => interno;

    public async Task<RespostaLlm> EnviarAsync(RequisicaoLlm requisicao, CancellationToken ct = default)
    {
        var cronometro = Stopwatch.StartNew();
        RespostaLlm resposta;
        try
        {
            resposta = await interno.EnviarAsync(requisicao, ct);
        }
        catch (Exception ex) when (GravacaoChamadaIa.NaoGrava(ex, ct))
        {
            // #3064 (A1): o que não saiu (disjuntor, desligado) e o cancelamento de quem chamou não gravam.
            throw;
        }
        catch (Exception ex)
        {
            await GravarAsync(requisicao, null, null, GravacaoChamadaIa.Desfecho(ex), cronometro.ElapsedMilliseconds);
            throw;
        }
        // #3365 (D2): o custo nasce aqui, uma vez, antes e fora da gravação: a linha e o turno recebem o mesmo valor, e
        // linha perdida (erro, prazo, empresa vazia) não tira o custo do turno.
        var custo = CustoDaResposta(resposta);
        await GravarAsync(requisicao, resposta, custo, DesfechoChamadaIa.Ok, cronometro.ElapsedMilliseconds);
        return custo is null ? resposta : resposta with { CustoUsd = custo };
    }

    /// <summary>
    /// Entrada e escrita de cache pelo preço de entrada, como o turno fazia; em 6 casas, a regra da linha. Modelo sem preço
    /// dá nulo, nunca zero, para o painel não mostrar "grátis".
    /// </summary>
    private decimal? CustoDaResposta(RespostaLlm resposta) =>
        precos.Value.Estimar(resposta.Modelo, resposta.TokensEntrada + resposta.TokensCacheEscrito, resposta.TokensSaida,
            resposta.TokensCacheLido) is { } custo ? ChamadaIa.ArredondarCusto(custo) : null;

    private async Task GravarAsync(RequisicaoLlm requisicao, RespostaLlm? resposta, decimal? custo, DesfechoChamadaIa desfecho, long duracaoMs)
    {
        if (requisicao.EmpresaId == Guid.Empty)
        {
            logger.LogWarning("Chamada de IA sem empresa ({Finalidade}): uso não registrado.", requisicao.Finalidade);
            return;
        }
        ChamadaIa chamada;
        try
        {
            chamada = ChamadaIa.Registrar(requisicao.EmpresaId, null, null, resposta?.Modelo, requisicao.Finalidade, requisicao.TurnoAgenteId,
                resposta?.TokensEntrada ?? 0, resposta?.TokensSaida ?? 0, resposta?.TokensCacheLido ?? 0,
                resposta?.TokensCacheEscrito ?? 0, custo, (int)Math.Min(duracaoMs, int.MaxValue), desfecho,
                relogio.GetUtcNow().UtcDateTime);
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "RegistroChamadaIa perdido: falha ao registrar chamada de IA da empresa {EmpresaId} ({Finalidade}).",
                requisicao.EmpresaId, requisicao.Finalidade);
            return;
        }
        await GravacaoChamadaIa.GravarAsync(registro, chamada, prazoRegistro, logger);
    }
}
