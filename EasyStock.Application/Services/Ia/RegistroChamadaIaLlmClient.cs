using System.Diagnostics;
using EasyStock.Application.Ports.Output.Ai;
using EasyStock.Domain.Entities.Ia;
using EasyStock.Domain.Enums.Ia;

namespace EasyStock.Application.Services.Ia;

/// <summary>
/// #3063 (S2a de #2948): envolve o cliente de LLM e grava cada tentativa em <c>chamada_ia</c>. A resposta e a exceção
/// passam intactas; cancelamento e o que não saiu (#3064, A1) não gravam; falha ao gravar só vira log. Provedor, modelo e
/// custo ficam nulos: é o caminho do <c>.env</c>; o caminho por loja grava no <see cref="RoteadorIaLlmClient"/>.
/// </summary>
public sealed class RegistroChamadaIaLlmClient(
    IAgenteLlmClient interno,
    IRegistroChamadaIa registro,
    TimeProvider relogio,
    ILogger<RegistroChamadaIaLlmClient> logger,
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
            await GravarAsync(requisicao, null, GravacaoChamadaIa.Desfecho(ex), cronometro.ElapsedMilliseconds);
            throw;
        }
        await GravarAsync(requisicao, resposta, DesfechoChamadaIa.Ok, cronometro.ElapsedMilliseconds);
        return resposta;
    }

    private async Task GravarAsync(RequisicaoLlm requisicao, RespostaLlm? resposta, DesfechoChamadaIa desfecho, long duracaoMs)
    {
        if (requisicao.EmpresaId == Guid.Empty)
        {
            logger.LogWarning("Chamada de IA sem empresa ({Finalidade}): uso não registrado.", requisicao.Finalidade);
            return;
        }
        ChamadaIa chamada;
        try
        {
            chamada = ChamadaIa.Registrar(requisicao.EmpresaId, null, null, resposta?.Modelo, requisicao.Finalidade, null,
                resposta?.TokensEntrada ?? 0, resposta?.TokensSaida ?? 0, resposta?.TokensCacheLido ?? 0,
                resposta?.TokensCacheEscrito ?? 0, null, (int)Math.Min(duracaoMs, int.MaxValue), desfecho,
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
