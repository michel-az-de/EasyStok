using System.Net;
using EasyStock.Application.Ports.Output.Ai;
using EasyStock.Domain.Entities.Ia;
using EasyStock.Domain.Enums.Ia;

namespace EasyStock.Application.Services.Ia;

/// <summary>
/// #3064: o que o registro da S2a e o roteador da S2b compartilham ao gravar <c>chamada_ia</c>: o desfecho de uma falha
/// e a gravação com prazo próprio, que só vira log quando falha.
/// </summary>
internal static class GravacaoChamadaIa
{
    /// <summary>#3083: acima do pior caso frio, abaixo do CommandTimeout de 30 s do Npgsql.</summary>
    public static readonly TimeSpan PrazoPadrao = TimeSpan.FromSeconds(20);

    /// <summary>A1: o que não saiu não é chamada, e cancelamento de quem chamou tem tokens desconhecidos.</summary>
    public static bool NaoGrava(Exception ex, CancellationToken ct) =>
        ex is ProvedorIaIndisponivelException || (ex is OperationCanceledException && ct.IsCancellationRequested);

    public static DesfechoChamadaIa Desfecho(Exception ex) => ex is HttpRequestException { StatusCode: { } status }
        ? status switch
        {
            HttpStatusCode.PaymentRequired => DesfechoChamadaIa.SemSaldo402,
            HttpStatusCode.TooManyRequests => DesfechoChamadaIa.Limite429,
            _ => DesfechoChamadaIa.Erro,
        }
        : DesfechoChamadaIa.Erro;

    public static async Task GravarAsync(IRegistroChamadaIa registro, ChamadaIa chamada, TimeSpan? prazo, ILogger logger)
    {
        try
        {
            // Sem o token de quem chamou (a resposta já veio), mas com prazo próprio: banco lento não segura a IA.
            using var limite = new CancellationTokenSource(prazo ?? PrazoPadrao);
            await registro.RegistrarAsync(chamada, limite.Token);
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "RegistroChamadaIa perdido: falha ao registrar chamada de IA da empresa {EmpresaId} ({Finalidade}).",
                chamada.EmpresaId, chamada.Finalidade);
        }
    }
}
