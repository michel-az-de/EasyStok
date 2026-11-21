using System.Diagnostics;
using System.Net;
using EasyStock.Application.Ports.Output.Ai;
using EasyStock.Domain.Entities.Ia;
using EasyStock.Domain.Enums.Ia;

namespace EasyStock.Application.Services.Ia;

/// <summary>
/// #3063 (S2a de #2948): envolve o cliente de LLM e grava cada tentativa em <c>chamada_ia</c>. A resposta e a exceção
/// passam intactas; cancelamento não grava; falha ao gravar só vira log. Provedor, modelo e custo ficam nulos até o
/// roteador da S2b.
/// </summary>
public sealed class RegistroChamadaIaLlmClient(
    IAgenteLlmClient interno,
    IRegistroChamadaIa registro,
    TimeProvider relogio,
    ILogger<RegistroChamadaIaLlmClient> logger,
    TimeSpan? prazoRegistro = null) : IAgenteLlmClient
{
    /// <summary>
    /// #3083: acima do pior caso frio (modelo do EF, conexão, SET do tenant), que passou de 3 s no CI e perdia a linha;
    /// abaixo do CommandTimeout de 30 s do Npgsql.
    /// </summary>
    public static readonly TimeSpan PrazoRegistroPadrao = TimeSpan.FromSeconds(20);

    public bool Disponivel => interno.Disponivel;

    public IAgenteLlmClient Interno => interno;

    public async Task<RespostaLlm> EnviarAsync(RequisicaoLlm requisicao, CancellationToken ct = default)
    {
        var cronometro = Stopwatch.StartNew();
        RespostaLlm resposta;
        try
        {
            resposta = await interno.EnviarAsync(requisicao, ct);
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested)
        {
            // Só o cancelamento de quem chamou não grava; timeout do cliente (HttpClient, prazo do CLI) é Erro.
            throw;
        }
        catch (Exception ex)
        {
            await GravarAsync(requisicao, null, Desfecho(ex), cronometro.ElapsedMilliseconds);
            throw;
        }
        await GravarAsync(requisicao, resposta, DesfechoChamadaIa.Ok, cronometro.ElapsedMilliseconds);
        return resposta;
    }

    private static DesfechoChamadaIa Desfecho(Exception ex) => ex is HttpRequestException { StatusCode: { } status }
        ? status switch
        {
            HttpStatusCode.PaymentRequired => DesfechoChamadaIa.SemSaldo402,
            HttpStatusCode.TooManyRequests => DesfechoChamadaIa.Limite429,
            _ => DesfechoChamadaIa.Erro,
        }
        : DesfechoChamadaIa.Erro;

    private async Task GravarAsync(RequisicaoLlm requisicao, RespostaLlm? resposta, DesfechoChamadaIa desfecho, long duracaoMs)
    {
        if (requisicao.EmpresaId == Guid.Empty)
        {
            logger.LogWarning("Chamada de IA sem empresa ({Finalidade}): uso não registrado.", requisicao.Finalidade);
            return;
        }
        try
        {
            var chamada = ChamadaIa.Registrar(requisicao.EmpresaId, null, null, resposta?.Modelo, requisicao.Finalidade, null,
                resposta?.TokensEntrada ?? 0, resposta?.TokensSaida ?? 0, resposta?.TokensCacheLido ?? 0,
                resposta?.TokensCacheEscrito ?? 0, null, (int)Math.Min(duracaoMs, int.MaxValue), desfecho,
                relogio.GetUtcNow().UtcDateTime);
            // Sem o token de quem chamou (a resposta já veio), mas com prazo próprio: banco lento não segura a IA.
            using var prazo = new CancellationTokenSource(prazoRegistro ?? PrazoRegistroPadrao);
            await registro.RegistrarAsync(chamada, prazo.Token);
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "RegistroChamadaIa perdido: falha ao registrar chamada de IA da empresa {EmpresaId} ({Finalidade}).",
                requisicao.EmpresaId, requisicao.Finalidade);
        }
    }
}
