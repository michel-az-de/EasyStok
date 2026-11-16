using EasyStock.Application.Common;

namespace EasyStock.Infra.Postgre.IntegrationTests.Workflows;

/// <summary>
/// #2770: os use cases do caixa carimbam e filtram pelo relógio real (HorarioBrasil.Hoje, DateTime.UtcNow), então a
/// prova não pode deslocar tudo para outro horário. Ela semeia dentro do dia operacional de "agora" e consulta o dia
/// em que os lançamentos de fato caíram. Sem isso, nos primeiros minutos depois da meia-noite de São Paulo um
/// "agora menos 5 min" cai no dia anterior.
/// </summary>
internal static class InstantesDoDiaOperacional
{
    /// <summary>
    /// Âncora para derivar os instantes da prova: o próprio <paramref name="agoraUtc"/>, empurrado para dentro do dia
    /// operacional dele. Garante que <c>âncora - recuoMaximo</c> e <c>âncora + avancoMaximo</c> caem no mesmo dia, e
    /// como todo instante é a âncora mais ou menos um deslocamento fixo, a ordem e o espaçamento entre eles não mudam.
    /// </summary>
    public static DateTime Ancora(DateTime agoraUtc, TimeSpan recuoMaximo, TimeSpan avancoMaximo = default)
    {
        var (ini, fim) = HorarioBrasil.JanelaDiaUtc(HorarioBrasil.DataOperacional(agoraUtc));
        var menor = ini + recuoMaximo;
        var maior = fim.AddTicks(-1) - avancoMaximo;
        return agoraUtc < menor ? menor : agoraUtc > maior ? maior : agoraUtc;
    }
}
