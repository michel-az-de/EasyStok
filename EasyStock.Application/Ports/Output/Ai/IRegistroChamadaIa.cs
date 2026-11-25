using EasyStock.Domain.Entities.Ia;

namespace EasyStock.Application.Ports.Output.Ai;

/// <summary>
/// #3063 (S2a de #2948): grava uma linha de <c>chamada_ia</c> num escopo próprio, com o tenant da chamada, para não
/// misturar com as alterações pendentes de quem chamou o LLM.
/// </summary>
public interface IRegistroChamadaIa
{
    Task RegistrarAsync(ChamadaIa chamada, CancellationToken ct = default);

    /// <summary>Custo e tokens do mês corrente (de Brasília) da loja; base do painel de uso da S4.</summary>
    Task<UsoMensalIa> SomarMesAsync(Guid empresaId, DateTime agoraUtc, CancellationToken ct = default);
}

public sealed record UsoMensalIa(int Chamadas, long TokensEntrada, long TokensSaida, decimal CustoUsd);
